using System.Collections.Concurrent;

namespace JitHub.Data.Caching;

public sealed class InMemoryCacheStore : ICacheStore
{
    private sealed class Entry(object value, Type valueType, CacheItemMetadata metadata)
    {
        public object Value { get; set; } = value;

        public Type ValueType { get; } = valueType;

        public CacheItemMetadata Metadata { get; set; } = metadata;
    }

    private readonly int _maxEntries;
    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, LinkedListNode<CacheKey>> _lruNodes = new();
    private readonly LinkedList<CacheKey> _lru = new();
    private readonly ConcurrentDictionary<CacheKey, Entry> _entries = new();

    public InMemoryCacheStore(InMemoryCacheStoreOptions? options = null)
    {
        options ??= new InMemoryCacheStoreOptions();
        if (options.MaxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxEntries, "MaxEntries must be greater than 0.");
        }

        _maxEntries = options.MaxEntries;
    }

    public bool TryGet<T>(CacheKey key, out T? value, out CacheItemMetadata? metadata)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.ValueType == typeof(T))
        {
            Touch(key);
            metadata = entry.Metadata;
            value = (T)entry.Value;
            return true;
        }

        metadata = null;
        value = default;
        return false;
    }

    public void Set<T>(CacheKey key, T value, CacheItemMetadata metadata)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        _entries[key] = new Entry(value!, typeof(T), metadata);
        Touch(key);
        EvictIfNeeded();
    }

    public bool Remove(CacheKey key)
    {
        var removed = _entries.TryRemove(key, out _);
        if (removed)
        {
            lock (_gate)
            {
                if (_lruNodes.Remove(key, out var node))
                {
                    _lru.Remove(node);
                }
            }
        }

        return removed;
    }

    private void Touch(CacheKey key)
    {
        lock (_gate)
        {
            if (_lruNodes.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
            }

            var node = _lru.AddFirst(key);
            _lruNodes[key] = node;
        }
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > _maxEntries)
        {
            CacheKey? toRemove = null;
            lock (_gate)
            {
                var last = _lru.Last;
                if (last is not null)
                {
                    toRemove = last.Value;
                    _lru.RemoveLast();
                    _lruNodes.Remove(toRemove.Value);
                }
            }

            if (toRemove is null)
            {
                return;
            }

            _entries.TryRemove(toRemove.Value, out _);
        }
    }
}
