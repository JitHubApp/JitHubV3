namespace JitHub.GitHub.Abstractions.Polling;

public sealed record PollingRequest(
    TimeSpan Interval,
    TimeSpan? JitterMax = null);
