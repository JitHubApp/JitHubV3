using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;

namespace JitHub.GitHub.Octokit.Services;

internal static class GitHubCacheKeys
{
    public static CacheKey MyRepositories()
        => CacheKey.Create("github.repos.mine");

    public static CacheKey Issues(RepoKey repo, IssueQuery query, PageRequest page)
    {
        var state = query.State.ToString();
        var search = string.IsNullOrWhiteSpace(query.SearchText) ? string.Empty : query.SearchText.Trim();

        var pageNumber = page.PageNumber?.ToString() ?? string.Empty;
        var cursor = page.Cursor ?? string.Empty;

        return CacheKey.Create(
            "github.issues.list",
            userScope: null,
            ("owner", repo.Owner),
            ("repo", repo.Name),
            ("state", state),
            ("search", search),
            ("pageSize", page.PageSize.ToString()),
            ("pageNumber", pageNumber),
            ("cursor", cursor));
    }
}
