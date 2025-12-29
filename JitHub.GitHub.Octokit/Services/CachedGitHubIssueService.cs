using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubIssueService : IGitHubIssueService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;

    public CachedGitHubIssueService(CacheRuntime cache, IGitHubDataSource dataSource)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<PagedResult<IReadOnlyList<IssueSummary>>> GetIssuesAsync(
        RepoKey repo,
        IssueQuery query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        var key = GitHubCacheKeys.Issues(repo, query, page);

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<PagedResult<IReadOnlyList<IssueSummary>>>(key);
            return cached.HasValue ? cached.Value! : new PagedResult<IReadOnlyList<IssueSummary>>(Array.Empty<IssueSummary>(), Next: null);
        }

        Func<CancellationToken, Task<PagedResult<IReadOnlyList<IssueSummary>>>> fetchAsync = async token =>
        {
            var data = await _dataSource.GetIssuesAsync(repo, query, page, token).ConfigureAwait(false);
            var items = data.Select(OctokitMappings.ToIssueSummary).ToArray();

            PageRequest? next = null;
            if (page.Cursor is null && page.PageNumber is not null && items.Length == page.PageSize)
            {
                next = PageRequest.FromPageNumber(page.PageNumber.Value + 1, page.PageSize);
            }

            return new PagedResult<IReadOnlyList<IssueSummary>>(items, next);
        };

        CacheSnapshot<PagedResult<IReadOnlyList<IssueSummary>>> snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        return snapshot.HasValue
            ? snapshot.Value!
            : new PagedResult<IReadOnlyList<IssueSummary>>(Array.Empty<IssueSummary>(), Next: null);
    }
}
