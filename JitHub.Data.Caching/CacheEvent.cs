namespace JitHub.Data.Caching;

public sealed record CacheEvent(
    CacheEventKind Kind,
    CacheKey Key,
    Type? ValueType,
    object? Value,
    Exception? Error,
    DateTimeOffset TimestampUtc);
