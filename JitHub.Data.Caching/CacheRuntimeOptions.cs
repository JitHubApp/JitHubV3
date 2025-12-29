namespace JitHub.Data.Caching;

public sealed record CacheRuntimeOptions(
    TimeSpan? DefaultTtl = null,
    int MaxConcurrentRefreshes = 8);
