namespace JitHub.GitHub.Abstractions.Refresh;

public enum RefreshMode
{
    PreferCacheThenRefresh = 0,
    ForceRefresh = 1,
    CacheOnly = 2,
}
