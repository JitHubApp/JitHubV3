namespace JitHub.Data.Caching;

public sealed record CacheSnapshot<T>(
    CacheKey Key,
    bool HasValue,
    T? Value,
    CacheItemMetadata? Metadata,
    bool IsFromCache);
