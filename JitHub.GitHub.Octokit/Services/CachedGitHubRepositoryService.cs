using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubRepositoryService : IGitHubRepositoryService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;

    public CachedGitHubRepositoryService(CacheRuntime cache, IGitHubDataSource dataSource)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<IReadOnlyList<RepositorySummary>> GetMyRepositoriesAsync(RefreshMode refresh, CancellationToken ct)
    {
        var key = GitHubCacheKeys.MyRepositories();

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<IReadOnlyList<RepositorySummary>>(key);
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

        return snapshot.HasValue ? snapshot.Value! : Array.Empty<RepositorySummary>();
    }
}
