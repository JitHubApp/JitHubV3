using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FluentAssertions;
using JitHubV3.Services.Ai;
using NUnit.Framework;

namespace JitHubV3.Tests.Ai;

public sealed class AiModelDownloadQueueTests
{
    private sealed class InMemoryInventoryStore : IAiLocalModelInventoryStore
    {
        public IReadOnlyList<AiLocalModelInventoryEntry> Inventory { get; private set; } = Array.Empty<AiLocalModelInventoryEntry>();

        public ValueTask<IReadOnlyList<AiLocalModelInventoryEntry>> GetInventoryAsync(CancellationToken ct)
            => ValueTask.FromResult(Inventory);

        public ValueTask SetInventoryAsync(IReadOnlyList<AiLocalModelInventoryEntry> inventory, CancellationToken ct)
        {
            Inventory = inventory;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedBytesHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public FixedBytesHandler(byte[] payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new ByteArrayContent(_payload);
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            resp.Content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(resp);
        }
    }

    private sealed class GateStream : Stream
    {
        private readonly SemaphoreSlim _gate = new(0);
        private readonly byte[] _chunk;

        public GateStream(int chunkSize = 4096)
        {
            _chunk = new byte[chunkSize];
            for (var i = 0; i < _chunk.Length; i++)
            {
                _chunk[i] = (byte)(i % 251);
            }
        }

        public void AllowOneRead() => _gate.Release();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ReadInternalAsync(buffer, cancellationToken);

        private async ValueTask<int> ReadInternalAsync(Memory<byte> buffer, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            var toCopy = Math.Min(buffer.Length, _chunk.Length);
            _chunk.AsSpan(0, toCopy).CopyTo(buffer.Span);
            return toCopy;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }

    private sealed class FiniteGateStream : Stream
    {
        private readonly SemaphoreSlim _gate = new(0);
        private readonly byte[] _chunk;
        private long _remaining;

        public FiniteGateStream(long totalBytes, int chunkSize = 4096)
        {
            _remaining = totalBytes;
            _chunk = new byte[chunkSize];
            for (var i = 0; i < _chunk.Length; i++)
            {
                _chunk[i] = (byte)(i % 251);
            }
        }

        public void AllowOneRead() => _gate.Release();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ReadInternalAsync(buffer, cancellationToken);

        private async ValueTask<int> ReadInternalAsync(Memory<byte> buffer, CancellationToken ct)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            await _gate.WaitAsync(ct);

            if (_remaining <= 0)
            {
                return 0;
            }

            var toCopy = (int)Math.Min(Math.Min(buffer.Length, _chunk.Length), _remaining);
            _chunk.AsSpan(0, toCopy).CopyTo(buffer.Span);
            _remaining -= toCopy;
            return toCopy;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }

    private sealed class GateStreamHandler : HttpMessageHandler
    {
        public GateStream Stream { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(Stream),
            };

            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return Task.FromResult(resp);
        }
    }

    private sealed class FiniteGateStreamHandler : HttpMessageHandler
    {
        private readonly long _totalBytes;

        public FiniteGateStreamHandler(long totalBytes)
        {
            _totalBytes = totalBytes;
            Stream = new FiniteGateStream(totalBytes);
        }

        public FiniteGateStream Stream { get; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(Stream),
            };

            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            resp.Content.Headers.ContentLength = _totalBytes;
            return Task.FromResult(resp);
        }
    }

    [Test]
    public async Task Enqueue_DownloadsArtifact_AndUpdatesInventory()
    {
        var payload = Enumerable.Range(0, 1024 * 64).Select(i => (byte)(i % 251)).ToArray();
        var http = new HttpClient(new FixedBytesHandler(payload));
        var inventory = new InMemoryInventoryStore();
        var queue = new AiModelDownloadQueue(http, inventory, new NullAiModelDownloadNotificationService());

        var installDir = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}");

        var handle = queue.Enqueue(new AiModelDownloadRequest(
            ModelId: "m1",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.com/models/m1.bin"),
            InstallPath: installDir,
            ArtifactFileName: "m1.bin"));

        var updates = new List<AiModelDownloadProgress>();
        using var sub = handle.Subscribe(p => updates.Add(p));

        var status = await handle.Task;
        status.Should().Be(AiModelDownloadStatus.Completed);

        var finalPath = Path.Combine(installDir, "m1.bin");
        File.Exists(finalPath).Should().BeTrue();
        File.ReadAllBytes(finalPath).Should().Equal(payload);

        inventory.Inventory.Should().ContainSingle(i => i.ModelId == "m1" && i.RuntimeId == "local-foundry" && i.InstallPath == installDir);

        updates.Should().Contain(u => u.Status == AiModelDownloadStatus.Downloading);
        updates.Last().Status.Should().Be(AiModelDownloadStatus.Completed);
    }

    [Test]
    public async Task Cancel_StopsDownload_AndDoesNotUpdateInventory()
    {
        var handler = new GateStreamHandler();
        var http = new HttpClient(handler);
        var inventory = new InMemoryInventoryStore();
        var queue = new AiModelDownloadQueue(http, inventory, new NullAiModelDownloadNotificationService());

        var installDir = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}");

        var handle = queue.Enqueue(new AiModelDownloadRequest(
            ModelId: "m2",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.com/models/m2.bin"),
            InstallPath: installDir,
            ArtifactFileName: "m2.bin"));

        var sawDownloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = handle.Subscribe(p =>
        {
            if (p.Status == AiModelDownloadStatus.Downloading && p.BytesReceived > 0)
            {
                sawDownloading.TrySetResult();
            }
        });

        // Allow one chunk so progress begins.
        handler.Stream.AllowOneRead();
        await sawDownloading.Task;

        queue.Cancel(handle.Id).Should().BeTrue();

        var status = await handle.Task;
        status.Should().Be(AiModelDownloadStatus.Canceled);

        inventory.Inventory.Should().BeEmpty();

        // Partial may exist; final should not.
        var finalPath = Path.Combine(installDir, "m2.bin");
        File.Exists(finalPath).Should().BeFalse();
    }

    [Test]
    public async Task Enqueue_WithExpectedSha256_TransitionsToVerifying()
    {
        var payload = Enumerable.Range(0, 1024 * 8).Select(i => (byte)(i % 251)).ToArray();

        // Precompute expected SHA256 to enable verification.
        var tmp = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(tmp, payload);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
        File.Delete(tmp);

        var http = new HttpClient(new FixedBytesHandler(payload));
        var inventory = new InMemoryInventoryStore();
        var queue = new AiModelDownloadQueue(http, inventory, new NullAiModelDownloadNotificationService());

        var installDir = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}");

        var handle = queue.Enqueue(new AiModelDownloadRequest(
            ModelId: "m3",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.com/models/m3.bin"),
            InstallPath: installDir,
            ArtifactFileName: "m3.bin",
            ExpectedSha256: expected));

        var updates = new List<AiModelDownloadProgress>();
        using var sub = handle.Subscribe(p => updates.Add(p));

        var status = await handle.Task;
        status.Should().Be(AiModelDownloadStatus.Completed);

        updates.Should().Contain(u => u.Status == AiModelDownloadStatus.Verifying);
    }

    [Test]
    public async Task Enqueue_WithMismatchedSha256_YieldsVerificationFailed_AndDoesNotUpdateInventory()
    {
        var payload = Enumerable.Range(0, 1024 * 8).Select(i => (byte)(i % 251)).ToArray();
        var http = new HttpClient(new FixedBytesHandler(payload));
        var inventory = new InMemoryInventoryStore();
        var queue = new AiModelDownloadQueue(http, inventory, new NullAiModelDownloadNotificationService());

        var installDir = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}");

        var handle = queue.Enqueue(new AiModelDownloadRequest(
            ModelId: "m4",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.com/models/m4.bin"),
            InstallPath: installDir,
            ArtifactFileName: "m4.bin",
            ExpectedSha256: "DEADBEEF"));

        var updates = new List<AiModelDownloadProgress>();
        using var sub = handle.Subscribe(p => updates.Add(p));

        var status = await handle.Task;
        status.Should().Be(AiModelDownloadStatus.VerificationFailed);

        updates.Last().Status.Should().Be(AiModelDownloadStatus.VerificationFailed);
        updates.Last().ErrorMessage.Should().NotBeNullOrWhiteSpace();

        inventory.Inventory.Should().BeEmpty();
    }

    [Test]
    public async Task Enqueue_DedupesByModelIdAndRuntimeId_ReturnsExistingHandle()
    {
        var handler = new FiniteGateStreamHandler(totalBytes: 8 * 1024);
        var http = new HttpClient(handler);
        var inventory = new InMemoryInventoryStore();
        var queue = new AiModelDownloadQueue(http, inventory, new NullAiModelDownloadNotificationService());

        var installDir = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}");

        var first = queue.Enqueue(new AiModelDownloadRequest(
            ModelId: "m5",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.com/models/m5.bin"),
            InstallPath: installDir,
            ArtifactFileName: "m5.bin"));

        var second = queue.Enqueue(new AiModelDownloadRequest(
            ModelId: "m5",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.com/models/m5.bin"),
            InstallPath: installDir,
            ArtifactFileName: "m5.bin"));

        second.Id.Should().Be(first.Id);

        // Let the finite download complete.
        for (var i = 0; i < 4; i++)
        {
            handler.Stream.AllowOneRead();
        }

        var status = await first.Task;
        status.Should().Be(AiModelDownloadStatus.Completed);
    }

    [Test]
    public async Task Enqueue_WhenAlreadyInstalled_CompletesImmediately_WithoutDownloading()
    {
        var handler = new GateStreamHandler();
        var http = new HttpClient(handler);
        var inventory = new InMemoryInventoryStore();
        var queue = new AiModelDownloadQueue(http, inventory, new NullAiModelDownloadNotificationService());

        var installDir = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "m6.bin"), "already here");

        await inventory.SetInventoryAsync(new[]
        {
            new AiLocalModelInventoryEntry("m6", "local-foundry", installDir),
        }, CancellationToken.None);

        var handle = queue.Enqueue(new AiModelDownloadRequest(
            ModelId: "m6",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.com/models/m6.bin"),
            InstallPath: installDir,
            ArtifactFileName: "m6.bin"));

        var status = await handle.Task;
        status.Should().Be(AiModelDownloadStatus.Completed);
    }
}
