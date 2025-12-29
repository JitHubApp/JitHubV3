using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;
using Microsoft.Extensions.Logging;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubRepositoryService : IGitHubRepositoryService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;
    private readonly ILogger<CachedGitHubRepositoryService> _logger;

    public CachedGitHubRepositoryService(CacheRuntime cache, IGitHubDataSource dataSource, ILogger<CachedGitHubRepositoryService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _logger = logger;
    }

    public async Task<IReadOnlyList<RepositorySummary>> GetMyRepositoriesAsync(RefreshMode refresh, CancellationToken ct)
    {
        var key = GitHubCacheKeys.MyRepositories();

        _logger.LogInformation("GetMyRepositoriesAsync (RefreshMode={RefreshMode})", refresh);

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<IReadOnlyList<RepositorySummary>>(key);
            _logger.LogInformation("Repositories cache-only: HasValue={HasValue}", cached.HasValue);
            return cached.HasValue ? cached.Value! : Array.Empty<RepositorySummary>();
        }

        Func<CancellationToken, Task<IReadOnlyList<RepositorySummary>>> fetchAsync = async token =>
        {
            var data = await _dataSource.GetMyRepositoriesAsync(token).ConfigureAwait(false);
            return data.Select(OctokitMappings.ToRepositorySummary).ToArray();
        };

        CacheSnapshot<IReadOnlyList<RepositorySummary>> snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Repositories snapshot: HasValue={HasValue} FromCache={IsFromCache}",
            snapshot.HasValue,
            snapshot.IsFromCache);

        return snapshot.HasValue ? snapshot.Value! : Array.Empty<RepositorySummary>();
    }
}
