namespace JitHubV3.Services.Ai;

public sealed class AiModelDownloadQueueEventingDecorator : IAiModelDownloadQueue
{
    private readonly IAiModelDownloadQueue _inner;
    private readonly IAiStatusEventPublisher _events;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, IDisposable> _subscriptionsByDownloadId = new();

    public AiModelDownloadQueueEventingDecorator(IAiModelDownloadQueue inner, IAiStatusEventPublisher events)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        _inner.DownloadsChanged += OnInnerDownloadsChanged;
        AttachToActiveDownloads();
    }

    public event Action? DownloadsChanged;

    public AiModelDownloadHandle Enqueue(AiModelDownloadRequest request)
    {
        var handle = _inner.Enqueue(request);
        Attach(handle);
        return handle;
    }

    public IReadOnlyList<AiModelDownloadHandle> GetActiveDownloads() => _inner.GetActiveDownloads();

    public AiModelDownloadHandle? TryGet(Guid downloadId) => _inner.TryGet(downloadId);

    public bool Cancel(Guid downloadId) => _inner.Cancel(downloadId);

    private void OnInnerDownloadsChanged()
    {
        AttachToActiveDownloads();
        DownloadsChanged?.Invoke();
    }

    private void AttachToActiveDownloads()
    {
        var active = _inner.GetActiveDownloads();

        foreach (var handle in active)
        {
            Attach(handle);
        }

        var activeIds = active.Select(h => h.Id).ToHashSet();
        lock (_gate)
        {
            foreach (var kvp in _subscriptionsByDownloadId.ToArray())
            {
                if (activeIds.Contains(kvp.Key))
                {
                    continue;
                }

                kvp.Value.Dispose();
                _subscriptionsByDownloadId.Remove(kvp.Key);
            }
        }
    }

    private void Attach(AiModelDownloadHandle handle)
    {
        lock (_gate)
        {
            if (_subscriptionsByDownloadId.ContainsKey(handle.Id))
            {
                return;
            }

            _subscriptionsByDownloadId[handle.Id] = handle.Subscribe(progress =>
            {
                _events.Publish(new AiDownloadProgressChanged(handle.Id, handle.Request, progress));
            });
        }
    }
}
