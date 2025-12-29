using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class CachedGitHubIssueConversationService : IGitHubIssueConversationService
{
    private readonly CacheRuntime _cache;
    private readonly IGitHubDataSource _dataSource;

    public CachedGitHubIssueConversationService(CacheRuntime cache, IGitHubDataSource dataSource)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<IssueDetail?> GetIssueAsync(RepoKey repo, int issueNumber, RefreshMode refresh, CancellationToken ct)
    {
        var key = GitHubCacheKeys.Issue(repo, issueNumber);

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<IssueDetail?>(key);
            return cached.HasValue ? cached.Value : null;
        }

        Func<CancellationToken, Task<IssueDetail?>> fetchAsync = async token =>
        {
            var data = await _dataSource.GetIssueAsync(repo, issueNumber, token).ConfigureAwait(false);
            return data is null ? null : OctokitMappings.ToIssueDetail(data);
        };

        var snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        return snapshot.HasValue ? snapshot.Value : null;
    }

    public async Task<IReadOnlyList<IssueComment>> GetCommentsAsync(RepoKey repo, int issueNumber, RefreshMode refresh, CancellationToken ct)
    {
        // POC: first page only.
        var page = PageRequest.FirstPage(pageSize: 50);
        var key = GitHubCacheKeys.IssueComments(repo, issueNumber, page);

        if (refresh == RefreshMode.CacheOnly)
        {
            var cached = _cache.GetCached<IReadOnlyList<IssueComment>>(key);
            return cached.HasValue ? (cached.Value ?? Array.Empty<IssueComment>()) : Array.Empty<IssueComment>();
        }

        Func<CancellationToken, Task<IReadOnlyList<IssueComment>>> fetchAsync = async token =>
        {
            var data = await _dataSource.GetIssueCommentsAsync(repo, issueNumber, page, token).ConfigureAwait(false);
            return data.Select(OctokitMappings.ToIssueComment).ToArray();
        };

        var snapshot = refresh == RefreshMode.ForceRefresh
            ? await _cache.RefreshAsync(key, fetchAsync, ct).ConfigureAwait(false)
            : await _cache.GetOrRefreshAsync(key, preferCacheThenRefresh: true, fetchAsync, ct).ConfigureAwait(false);

        return snapshot.HasValue ? (snapshot.Value ?? Array.Empty<IssueComment>()) : Array.Empty<IssueComment>();
    }
}
