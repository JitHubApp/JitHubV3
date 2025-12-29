using System.Collections.Concurrent;

namespace JitHub.Data.Caching;

public sealed class CacheEventBus : ICacheEventBus
{
    private readonly ConcurrentDictionary<int, Action<CacheEvent>> _subscribers = new();
    private int _nextId;

    public IDisposable Subscribe(Action<CacheEvent> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var id = Interlocked.Increment(ref _nextId);
        _subscribers[id] = handler;

        return new Subscription(() => _subscribers.TryRemove(id, out _));
    }

    public void Publish(CacheEvent cacheEvent)
    {
        foreach (var subscriber in _subscribers.Values)
        {
            try
            {
                subscriber(cacheEvent);
            }
            catch
            {
                // Intentionally isolate subscribers. Cache publication should never fail because a consumer misbehaves.
            }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
