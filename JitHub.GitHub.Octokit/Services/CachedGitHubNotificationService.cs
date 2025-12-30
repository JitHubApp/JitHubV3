using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubNotificationService : IGitHubNotificationService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;

    public CachedGitHubNotificationService(CacheRuntime cache, IGitHubDataSource dataSource)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<PagedResult<IReadOnlyList<NotificationSummary>>> GetMyNotificationsAsync(
        bool unreadOnly,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        var key = GitHubCacheKeys.MyNotifications(unreadOnly, page);

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<PagedResult<IReadOnlyList<NotificationSummary>>>(key);
            return cached.HasValue
                ? cached.Value!
                : new PagedResult<IReadOnlyList<NotificationSummary>>(Array.Empty<NotificationSummary>(), Next: null);
        }

        Func<CancellationToken, Task<PagedResult<IReadOnlyList<NotificationSummary>>>> fetchAsync = async token =>
        {
            var data = await _dataSource.GetMyNotificationsAsync(unreadOnly, page, token).ConfigureAwait(false);
            var items = data.Select(OctokitMappings.ToNotificationSummary).ToArray();

            PageRequest? next = null;
            if (page.Cursor is null && page.PageNumber is not null && items.Length == page.PageSize)
            {
                next = PageRequest.FromPageNumber(page.PageNumber.Value + 1, page.PageSize);
            }

            return new PagedResult<IReadOnlyList<NotificationSummary>>(items, next);
        };

        CacheSnapshot<PagedResult<IReadOnlyList<NotificationSummary>>> snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        return snapshot.HasValue
            ? snapshot.Value!
            : new PagedResult<IReadOnlyList<NotificationSummary>>(Array.Empty<NotificationSummary>(), Next: null);
    }
}
