namespace JitHub.Data.Caching;

public interface ICacheStore
{
    bool TryGet<T>(CacheKey key, out T? value, out CacheItemMetadata? metadata);

    void Set<T>(CacheKey key, T value, CacheItemMetadata metadata);

    bool Remove(CacheKey key);
}
