namespace JitHub.Data.Caching;

public sealed record PollingOptions(
    TimeSpan Interval,
    TimeSpan? JitterMax = null);
