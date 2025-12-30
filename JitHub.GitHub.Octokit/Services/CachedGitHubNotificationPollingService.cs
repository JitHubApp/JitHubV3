using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Polling;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;
using Microsoft.Extensions.Logging;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubNotificationPollingService : IGitHubNotificationPollingService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;
    private readonly ILogger<CachedGitHubNotificationPollingService> _logger;

    public CachedGitHubNotificationPollingService(
        CacheRuntime cache,
        IGitHubDataSource dataSource,
        ILogger<CachedGitHubNotificationPollingService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartNotificationsPollingAsync(bool unreadOnly, PageRequest page, PollingRequest polling, CancellationToken ct)
    {
        _logger.LogInformation(
            "Start notifications polling: unreadOnly={UnreadOnly} interval={IntervalMs}ms jitterMax={JitterMs}ms",
            unreadOnly,
            (long)polling.Interval.TotalMilliseconds,
            (long)polling.JitterMax.GetValueOrDefault(TimeSpan.Zero).TotalMilliseconds);

        var key = GitHubCacheKeys.MyNotifications(unreadOnly, page);
        var options = new PollingOptions(polling.Interval, polling.JitterMax);

        return _cache.StartPolling(
            key,
            options,
            fetchAsync: async token =>
            {
                var data = await _dataSource.GetMyNotificationsAsync(unreadOnly, page, token).ConfigureAwait(false);
                var items = data.Select(OctokitMappings.ToNotificationSummary).ToArray();

                PageRequest? next = null;
                if (page.Cursor is null && page.PageNumber is not null && items.Length == page.PageSize)
                {
                    next = PageRequest.FromPageNumber(page.PageNumber.Value + 1, page.PageSize);
                }

                return new PagedResult<IReadOnlyList<NotificationSummary>>(items, next);
            },
            ct);
    }
}
