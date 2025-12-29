namespace JitHub.Data.Caching;

public sealed record CacheItemMetadata(
    DateTimeOffset FetchedAtUtc,
    DateTimeOffset? ExpiresAtUtc = null,
    string? ETag = null,
    DateTimeOffset? LastModifiedUtc = null,
    Exception? LastError = null);
