using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubActivityService : IGitHubActivityService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;

    public CachedGitHubActivityService(CacheRuntime cache, IGitHubDataSource dataSource)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public Task<PagedResult<IReadOnlyList<ActivitySummary>>> GetMyActivityAsync(
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
        => GetAsync(
            key: GitHubCacheKeys.MyActivity(page),
            fetchAsync: token => FetchMyAsync(page, token),
            page,
            refresh,
            ct);

    public Task<PagedResult<IReadOnlyList<ActivitySummary>>> GetRepoActivityAsync(
        RepoKey repo,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
        => GetAsync(
            key: GitHubCacheKeys.RepoActivity(repo, page),
            fetchAsync: token => FetchRepoAsync(repo, page, token),
            page,
            refresh,
            ct);

    private async Task<PagedResult<IReadOnlyList<ActivitySummary>>> FetchMyAsync(PageRequest page, CancellationToken ct)
    {
        var data = await _dataSource.GetMyActivityAsync(page, ct).ConfigureAwait(false);
        var items = data.Select(OctokitMappings.ToActivitySummary).ToArray();
        return new PagedResult<IReadOnlyList<ActivitySummary>>(items, Next: ComputeNext(page, items.Length));
    }

    private async Task<PagedResult<IReadOnlyList<ActivitySummary>>> FetchRepoAsync(RepoKey repo, PageRequest page, CancellationToken ct)
    {
        var data = await _dataSource.GetRepoActivityAsync(repo, page, ct).ConfigureAwait(false);
        var items = data.Select(OctokitMappings.ToActivitySummary).ToArray();
        return new PagedResult<IReadOnlyList<ActivitySummary>>(items, Next: ComputeNext(page, items.Length));
    }

    private async Task<PagedResult<IReadOnlyList<ActivitySummary>>> GetAsync(
        CacheKey key,
        Func<CancellationToken, Task<PagedResult<IReadOnlyList<ActivitySummary>>>> fetchAsync,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<PagedResult<IReadOnlyList<ActivitySummary>>>(key);
            return cached.HasValue
                ? cached.Value!
                : new PagedResult<IReadOnlyList<ActivitySummary>>(Array.Empty<ActivitySummary>(), Next: null);
        }

        CacheSnapshot<PagedResult<IReadOnlyList<ActivitySummary>>> snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        return snapshot.HasValue
            ? snapshot.Value!
            : new PagedResult<IReadOnlyList<ActivitySummary>>(Array.Empty<ActivitySummary>(), Next: null);
    }

    private static PageRequest? ComputeNext(PageRequest page, int returnedCount)
    {
        if (page.Cursor is not null)
        {
            return null;
        }

        if (page.PageNumber is not null && returnedCount == page.PageSize)
        {
            return PageRequest.FromPageNumber(page.PageNumber.Value + 1, page.PageSize);
        }

        return null;
    }
}
