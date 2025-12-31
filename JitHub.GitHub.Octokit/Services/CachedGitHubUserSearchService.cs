using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubUserSearchService : IGitHubUserSearchService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;

    public CachedGitHubUserSearchService(CacheRuntime cache, IGitHubDataSource dataSource)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<PagedResult<IReadOnlyList<UserSummary>>> SearchAsync(
        UserSearchQuery query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        var key = GitHubCacheKeys.SearchUsers(query, page);

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<PagedResult<IReadOnlyList<UserSummary>>>(key);
            return cached.HasValue ? cached.Value! : new PagedResult<IReadOnlyList<UserSummary>>(Array.Empty<UserSummary>(), Next: null);
        }

        Func<CancellationToken, Task<PagedResult<IReadOnlyList<UserSummary>>>> fetchAsync = async token =>
        {
            var data = await _dataSource.SearchUsersAsync(query, page, token).ConfigureAwait(false);
            var items = data.Select(OctokitMappings.ToUserSummary).ToArray();

            PageRequest? next = null;
            if (page.Cursor is null && page.PageNumber is not null && items.Length == page.PageSize)
            {
                next = PageRequest.FromPageNumber(page.PageNumber.Value + 1, page.PageSize);
            }

            return new PagedResult<IReadOnlyList<UserSummary>>(items, next);
        };

        CacheSnapshot<PagedResult<IReadOnlyList<UserSummary>>> snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        return snapshot.HasValue
            ? snapshot.Value!
            : new PagedResult<IReadOnlyList<UserSummary>>(Array.Empty<UserSummary>(), Next: null);
    }
}
