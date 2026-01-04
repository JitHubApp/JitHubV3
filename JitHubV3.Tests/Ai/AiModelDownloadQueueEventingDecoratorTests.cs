using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class AiModelDownloadQueueEventingDecoratorTests
{
    [Test]
    public void Enqueue_PublishesDownloadProgressEvents()
    {
        var inner = new FakeDownloadQueue();
        var events = new RecordingPublisher();

        var sut = new AiModelDownloadQueueEventingDecorator(inner, events);

        var handle = sut.Enqueue(new AiModelDownloadRequest(
            ModelId: "m1",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("file:///C:/tmp/m1.bin"),
            InstallPath: "C:\\models\\m1",
            ArtifactFileName: "m1.bin"));

        inner.Publish(handle, AiModelDownloadStatus.Downloading, progress: 0.5);

        events.Events.OfType<AiDownloadProgressChanged>()
            .Should()
            .Contain(e => e.DownloadId == handle.Id && e.Progress.Status == AiModelDownloadStatus.Downloading);
    }

    [Test]
    public void DownloadsChanged_IsForwarded()
    {
        var inner = new FakeDownloadQueue();
        var events = new RecordingPublisher();

        var sut = new AiModelDownloadQueueEventingDecorator(inner, events);

        var raised = 0;
        sut.DownloadsChanged += () => raised++;

        inner.RaiseChanged();

        raised.Should().Be(1);
    }

    private sealed class RecordingPublisher : IAiStatusEventPublisher
    {
        public List<AiStatusEvent> Events { get; } = new();

        public void Publish(AiStatusEvent evt) => Events.Add(evt);
    }

    private sealed class FakeDownloadQueue : IAiModelDownloadQueue
    {
        private readonly List<AiModelDownloadHandle> _active = new();

        public event Action? DownloadsChanged;

        public AiModelDownloadHandle Enqueue(AiModelDownloadRequest request)
        {
            var handle = new AiModelDownloadHandle(Guid.NewGuid(), request, new CancellationTokenSource());
            _active.Add(handle);
            DownloadsChanged?.Invoke();
            return handle;
        }

        public IReadOnlyList<AiModelDownloadHandle> GetActiveDownloads() => _active.ToArray();

        public AiModelDownloadHandle? TryGet(Guid downloadId)
            => _active.FirstOrDefault(h => h.Id == downloadId);

        public bool Cancel(Guid downloadId) => false;

        public void Publish(AiModelDownloadHandle handle, AiModelDownloadStatus status, double? progress)
        {
            var p = new AiModelDownloadProgress(
                DownloadId: handle.Id,
                ModelId: handle.Request.ModelId,
                RuntimeId: handle.Request.RuntimeId,
                Status: status,
                BytesReceived: 0,
                TotalBytes: handle.Request.ExpectedBytes,
                Progress: progress,
                InstallPath: handle.Request.InstallPath,
                ArtifactPath: null,
                ErrorMessage: null);

            handle.Publish(p);
        }

        public void RaiseChanged() => DownloadsChanged?.Invoke();
    }
}
