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

    [Test]
    public async Task Enqueue_DownloadsArtifact_AndUpdatesInventory()
    {
        var payload = Enumerable.Range(0, 1024 * 64).Select(i => (byte)(i % 251)).ToArray();
        var http = new HttpClient(new FixedBytesHandler(payload));
        var inventory = new InMemoryInventoryStore();
        var queue = new AiModelDownloadQueue(http, inventory);

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
        var queue = new AiModelDownloadQueue(http, inventory);

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
}
