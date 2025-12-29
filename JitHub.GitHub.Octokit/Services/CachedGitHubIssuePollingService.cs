using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Polling;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;
using Microsoft.Extensions.Logging;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubIssuePollingService : IGitHubIssuePollingService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;
    private readonly ILogger<CachedGitHubIssuePollingService> _logger;

    public CachedGitHubIssuePollingService(CacheRuntime cache, IGitHubDataSource dataSource, ILogger<CachedGitHubIssuePollingService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartIssuesPollingAsync(
        RepoKey repo,
        IssueQuery query,
        PageRequest page,
        PollingRequest polling,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Start issues polling: {Repo} state={State} interval={IntervalMs}ms jitterMax={JitterMs}ms",
            $"{repo.Owner}/{repo.Name}",
            query.State,
            (long)polling.Interval.TotalMilliseconds,
            (long)polling.JitterMax.GetValueOrDefault(TimeSpan.Zero).TotalMilliseconds);

        var key = GitHubCacheKeys.Issues(repo, query, page);
        var options = new PollingOptions(polling.Interval, polling.JitterMax);

        return _cache.StartPolling(
            key,
            options,
            fetchAsync: async token =>
            {
                _logger.LogDebug("Polling fetch: {Repo} state={State}", $"{repo.Owner}/{repo.Name}", query.State);
                var data = await _dataSource.GetIssuesAsync(repo, query, page, token).ConfigureAwait(false);
                var items = data.Select(OctokitMappings.ToIssueSummary).ToArray();

                PageRequest? next = null;
                if (page.Cursor is null && page.PageNumber is not null && items.Length == page.PageSize)
                {
                    next = PageRequest.FromPageNumber(page.PageNumber.Value + 1, page.PageSize);
                }

                return new PagedResult<IReadOnlyList<IssueSummary>>(items, next);
            },
            ct);
    }
}
