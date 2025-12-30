using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubRepositoryDetailsService : IGitHubRepositoryDetailsService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;

    public CachedGitHubRepositoryDetailsService(CacheRuntime cache, IGitHubDataSource dataSource)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<RepositorySnapshot?> GetRepositoryAsync(RepoKey repo, RefreshMode refresh, CancellationToken ct)
    {
        var key = GitHubCacheKeys.Repository(repo);

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<RepositorySnapshot?>(key);
            return cached.HasValue ? cached.Value : null;
        }

        Func<CancellationToken, Task<RepositorySnapshot?>> fetchAsync = async token =>
        {
            var data = await _dataSource.GetRepositoryAsync(repo, token).ConfigureAwait(false);
            return data is null ? null : OctokitMappings.ToRepositorySnapshot(data);
        };

        var snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        return snapshot.HasValue ? snapshot.Value : null;
    }
}
