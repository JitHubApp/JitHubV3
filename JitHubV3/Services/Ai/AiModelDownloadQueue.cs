using System.Net.Http.Headers;
using System.IO.Compression;
using System.Security.Cryptography;

namespace JitHubV3.Services.Ai;

public sealed class AiModelDownloadQueue : IAiModelDownloadQueue
{
    private readonly object _gate = new();
    private readonly List<AiModelDownloadHandle> _queue = new();
    private readonly Dictionary<Guid, AiModelDownloadHandle> _byId = new();

    private Task? _processingTask;

    private readonly HttpClient _http;
    private readonly IAiLocalModelInventoryStore _inventoryStore;
    private readonly IAiModelDownloadNotificationService _notifications;

    public AiModelDownloadQueue(
        HttpClient http,
        IAiLocalModelInventoryStore inventoryStore,
        IAiModelDownloadNotificationService notifications)
    {
        _http = http;
        _inventoryStore = inventoryStore;
        _notifications = notifications;
    }

    public event Action? DownloadsChanged;

    public AiModelDownloadHandle Enqueue(AiModelDownloadRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new ArgumentException("ModelId is required", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RuntimeId))
        {
            throw new ArgumentException("RuntimeId is required", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.InstallPath))
        {
            throw new ArgumentException("InstallPath is required", nameof(request));
        }

        lock (_gate)
        {
            var existing = _queue.FirstOrDefault(h =>
                string.Equals(h.Request.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(h.Request.RuntimeId, request.RuntimeId, StringComparison.OrdinalIgnoreCase)
                && h.Latest.Status is (AiModelDownloadStatus.Queued or AiModelDownloadStatus.Downloading or AiModelDownloadStatus.Verifying));

            if (existing is not null)
            {
                return existing;
            }

            var id = Guid.NewGuid();
            var cts = new CancellationTokenSource();
            var handle = new AiModelDownloadHandle(id, request, cts);

            _queue.Add(handle);
            _byId.Add(handle.Id, handle);

            if (_processingTask is null || _processingTask.IsFaulted)
            {
                _processingTask = Task.Run(ProcessQueueAsync);
            }

            DownloadsChanged?.Invoke();
            return handle;
        }
    }

    public IReadOnlyList<AiModelDownloadHandle> GetActiveDownloads()
    {
        lock (_gate)
        {
            return _queue.ToArray();
        }
    }

    public AiModelDownloadHandle? TryGet(Guid downloadId)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(downloadId, out var handle) ? handle : null;
        }
    }

    public bool Cancel(Guid downloadId)
    {
        AiModelDownloadHandle? handle;
        lock (_gate)
        {
            handle = _byId.TryGetValue(downloadId, out var h) ? h : null;
        }

        if (handle is null)
        {
            return false;
        }

        handle.Cancel();
        return true;
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            AiModelDownloadHandle? next = null;
            lock (_gate)
            {
                next = _queue.FirstOrDefault(h => h.Latest.Status == AiModelDownloadStatus.Queued);
                if (next is null)
                {
                    _processingTask = null;
                    return;
                }

                next.Publish(next.Latest with { Status = AiModelDownloadStatus.Downloading });
            }

            DownloadsChanged?.Invoke();

            try
            {
                // Phase 8.2: fast-path / dedupe behavior.
                // If the model is already present on disk and recorded in inventory, treat it as completed.
                if (await IsAlreadyInstalledAsync(next.Request, next.Cancellation.Token).ConfigureAwait(false))
                {
                    next.Completion.TrySetResult(AiModelDownloadStatus.Completed);
                    next.Publish(next.Latest with { Status = AiModelDownloadStatus.Completed, Progress = 1.0, ErrorMessage = null });
                    continue;
                }
                await DownloadAsync(next).ConfigureAwait(false);

                next.Completion.TrySetResult(AiModelDownloadStatus.Completed);
                next.Publish(next.Latest with { Status = AiModelDownloadStatus.Completed, Progress = 1.0 });

                _notifications.NotifyDownloadCompleted(next.Latest);
            }
            catch (OperationCanceledException)
            {
                next.Completion.TrySetResult(AiModelDownloadStatus.Canceled);
                next.Publish(next.Latest with { Status = AiModelDownloadStatus.Canceled, ErrorMessage = null });
            }
            catch (AiModelDownloadVerificationException ex)
            {
                next.Completion.TrySetResult(AiModelDownloadStatus.VerificationFailed);
                next.Publish(next.Latest with { Status = AiModelDownloadStatus.VerificationFailed, ErrorMessage = ex.Message });

                _notifications.NotifyDownloadFailed(next.Latest);
            }
            catch (Exception ex)
            {
                next.Completion.TrySetResult(AiModelDownloadStatus.Failed);
                next.Publish(next.Latest with { Status = AiModelDownloadStatus.Failed, ErrorMessage = ex.Message });

                _notifications.NotifyDownloadFailed(next.Latest);
            }
            finally
            {
                lock (_gate)
                {
                    _queue.Remove(next);
                }

                DownloadsChanged?.Invoke();
            }
        }
    }

    private async Task<bool> IsAlreadyInstalledAsync(AiModelDownloadRequest request, CancellationToken ct)
    {
        try
        {
            var inventory = await _inventoryStore.GetInventoryAsync(ct).ConfigureAwait(false);
            var match = inventory.FirstOrDefault(i =>
                string.Equals(i.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.RuntimeId, request.RuntimeId, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                return false;
            }

            var installPath = match.InstallPath;
            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            {
                return false;
            }

            var artifactName = string.IsNullOrWhiteSpace(request.ArtifactFileName)
                ? GetDefaultArtifactFileName(request.SourceUri)
                : request.ArtifactFileName!;

            var artifactPath = Path.Combine(installPath, artifactName);
            if (File.Exists(artifactPath))
            {
                return true;
            }

            // If the artifact file name isn't present (e.g., extracted ZIP), treat any non-empty directory as installed.
            return Directory.EnumerateFileSystemEntries(installPath).Any();
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadAsync(AiModelDownloadHandle handle)
    {
        var request = handle.Request;
        var ct = handle.Cancellation.Token;

        Directory.CreateDirectory(request.InstallPath);

        var artifactFileName = string.IsNullOrWhiteSpace(request.ArtifactFileName)
            ? GetDefaultArtifactFileName(request.SourceUri)
            : request.ArtifactFileName!;

        var finalPath = Path.Combine(request.InstallPath, artifactFileName);
        var partialPath = finalPath + ".partial";

        var totalBytes = request.ExpectedBytes;

        if (request.SourceUri.IsFile)
        {
            await using var source = File.OpenRead(request.SourceUri.LocalPath);
            totalBytes ??= source.Length;
            await CopyStreamWithProgressAsync(source, partialPath, totalBytes, handle, ct).ConfigureAwait(false);
        }
        else
        {
            using var httpReq = new HttpRequestMessage(HttpMethod.Get, request.SourceUri);
            httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            totalBytes ??= resp.Content.Headers.ContentLength;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await CopyStreamWithProgressAsync(stream, partialPath, totalBytes, handle, ct).ConfigureAwait(false);
        }

        // Replace existing artifact if needed
        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        File.Move(partialPath, finalPath);

        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256))
        {
            handle.Publish(handle.Latest with { Status = AiModelDownloadStatus.Verifying, ErrorMessage = null });
            var actual = ComputeSha256Hex(finalPath);
            if (!string.Equals(NormalizeSha256Hex(actual), NormalizeSha256Hex(request.ExpectedSha256), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(finalPath); } catch { /* ignore */ }
                throw new AiModelDownloadVerificationException("Verification failed: downloaded artifact SHA256 does not match ExpectedSha256.");
            }
        }

        if (string.Equals(Path.GetExtension(finalPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            // Consider extraction as part of the post-download verification phase.
            handle.Publish(handle.Latest with { Status = AiModelDownloadStatus.Verifying, ErrorMessage = null });
        }

        if (string.Equals(Path.GetExtension(finalPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(finalPath, request.InstallPath, overwriteFiles: true);
        }

        // Persist the inventory entry as "downloaded".
        var current = await _inventoryStore.GetInventoryAsync(ct).ConfigureAwait(false);
        var updated = current
            .Where(i => !string.Equals(i.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { new AiLocalModelInventoryEntry(request.ModelId, request.RuntimeId, request.InstallPath) })
            .ToArray();

        await _inventoryStore.SetInventoryAsync(updated, ct).ConfigureAwait(false);

        handle.Publish(handle.Latest with
        {
            TotalBytes = totalBytes,
            BytesReceived = totalBytes ?? handle.Latest.BytesReceived,
            InstallPath = request.InstallPath,
            ArtifactPath = finalPath,
            Progress = totalBytes is null ? handle.Latest.Progress : 1.0,
        });
    }

    private static async Task CopyStreamWithProgressAsync(
        Stream source,
        string partialPath,
        long? totalBytes,
        AiModelDownloadHandle handle,
        CancellationToken ct)
    {
        const int bufferSize = 64 * 1024;
        var buffer = new byte[bufferSize];

        long received = 0;

        await using var file = new FileStream(
            partialPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

            received += read;

            double? progress = totalBytes is null || totalBytes <= 0
                ? null
                : Math.Clamp(received / (double)totalBytes.Value, 0.0, 1.0);

            handle.Publish(handle.Latest with
            {
                Status = AiModelDownloadStatus.Downloading,
                BytesReceived = received,
                TotalBytes = totalBytes,
                Progress = progress,
                ArtifactPath = partialPath,
                ErrorMessage = null,
            });
        }

        // Ensure all bytes are flushed.
        await file.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string GetDefaultArtifactFileName(Uri uri)
    {
        try
        {
            var seg = uri.Segments.LastOrDefault();
            seg = seg?.Trim('/');
            if (!string.IsNullOrWhiteSpace(seg))
            {
                return seg;
            }
        }
        catch
        {
            // ignore
        }

        return "model.bin";
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static string NormalizeSha256Hex(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            t = t.Substring("sha256:".Length).Trim();
        }
        return t;
    }

    private sealed class AiModelDownloadVerificationException : Exception
    {
        public AiModelDownloadVerificationException(string message) : base(message)
        {
        }
    }
}
