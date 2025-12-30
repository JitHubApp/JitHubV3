using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubIssueSearchService : IGitHubIssueSearchService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;

    public CachedGitHubIssueSearchService(CacheRuntime cache, IGitHubDataSource dataSource)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<PagedResult<IReadOnlyList<WorkItemSummary>>> SearchAsync(
        IssueSearchQuery query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        var key = GitHubCacheKeys.SearchIssues(query, page);

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<PagedResult<IReadOnlyList<WorkItemSummary>>>(key);
            return cached.HasValue ? cached.Value! : new PagedResult<IReadOnlyList<WorkItemSummary>>(Array.Empty<WorkItemSummary>(), Next: null);
        }

        Func<CancellationToken, Task<PagedResult<IReadOnlyList<WorkItemSummary>>>> fetchAsync = async token =>
        {
            var data = await _dataSource.SearchIssuesAsync(query, page, token).ConfigureAwait(false);
            var items = data.Select(OctokitMappings.ToWorkItemSummary).ToArray();

            PageRequest? next = null;
            if (page.Cursor is null && page.PageNumber is not null && items.Length == page.PageSize)
            {
                next = PageRequest.FromPageNumber(page.PageNumber.Value + 1, page.PageSize);
            }

            return new PagedResult<IReadOnlyList<WorkItemSummary>>(items, next);
        };

        CacheSnapshot<PagedResult<IReadOnlyList<WorkItemSummary>>> snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        return snapshot.HasValue
            ? snapshot.Value!
            : new PagedResult<IReadOnlyList<WorkItemSummary>>(Array.Empty<WorkItemSummary>(), Next: null);
    }
}
