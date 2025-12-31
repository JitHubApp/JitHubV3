namespace JitHubV3.Services.Ai;

public interface IAiModelDownloadQueue
{
    event Action? DownloadsChanged;

    AiModelDownloadHandle Enqueue(AiModelDownloadRequest request);

    IReadOnlyList<AiModelDownloadHandle> GetActiveDownloads();

    AiModelDownloadHandle? TryGet(Guid downloadId);

    bool Cancel(Guid downloadId);
}

public sealed class AiModelDownloadHandle
{
    private readonly object _gate = new();
    private readonly List<Action<AiModelDownloadProgress>> _subscribers = new();

    internal AiModelDownloadHandle(Guid id, AiModelDownloadRequest request, CancellationTokenSource cts)
    {
        Id = id;
        Request = request;
        Cancellation = cts;
        Completion = new TaskCompletionSource<AiModelDownloadStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        Latest = new AiModelDownloadProgress(
            DownloadId: id,
            ModelId: request.ModelId,
            RuntimeId: request.RuntimeId,
            Status: AiModelDownloadStatus.Queued,
            BytesReceived: 0,
            TotalBytes: request.ExpectedBytes,
            Progress: null,
            InstallPath: request.InstallPath,
            ArtifactPath: null,
            ErrorMessage: null);
    }

    public Guid Id { get; }

    public AiModelDownloadRequest Request { get; }

    public AiModelDownloadProgress Latest { get; private set; }

    public Task<AiModelDownloadStatus> Task => Completion.Task;

    internal TaskCompletionSource<AiModelDownloadStatus> Completion { get; }

    internal CancellationTokenSource Cancellation { get; }

    public IDisposable Subscribe(Action<AiModelDownloadProgress> onProgress)
    {
        if (onProgress is null)
        {
            throw new ArgumentNullException(nameof(onProgress));
        }

        lock (_gate)
        {
            _subscribers.Add(onProgress);
        }

        onProgress(Latest);
        return new Subscription(this, onProgress);
    }

    public void Cancel() => Cancellation.Cancel();

    internal void Publish(AiModelDownloadProgress progress)
    {
        Latest = progress;

        Action<AiModelDownloadProgress>[] subs;
        lock (_gate)
        {
            subs = _subscribers.ToArray();
        }

        foreach (var s in subs)
        {
            s(progress);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly AiModelDownloadHandle _owner;
        private readonly Action<AiModelDownloadProgress> _handler;
        private bool _disposed;

        public Subscription(AiModelDownloadHandle owner, Action<AiModelDownloadProgress> handler)
        {
            _owner = owner;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_owner._gate)
            {
                _owner._subscribers.Remove(_handler);
            }
        }
    }
}
