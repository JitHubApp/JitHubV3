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
        var sort = query.Sort?.ToString() ?? string.Empty;
        var direction = query.Direction?.ToString() ?? string.Empty;

        var pageNumber = page.PageNumber?.ToString() ?? string.Empty;
        var cursor = page.Cursor ?? string.Empty;

        return CacheKey.Create(
            "github.issues.list",
            userScope: null,
            ("owner", repo.Owner),
            ("repo", repo.Name),
            ("state", state),
            ("search", search),
            ("sort", sort),
            ("direction", direction),
            ("pageSize", page.PageSize.ToString()),
            ("pageNumber", pageNumber),
            ("cursor", cursor));
    }

    public static CacheKey Issue(RepoKey repo, int issueNumber)
        => CacheKey.Create(
            "github.issue.get",
            userScope: null,
            ("owner", repo.Owner),
            ("repo", repo.Name),
            ("issueNumber", issueNumber.ToString()));

    public static CacheKey IssueComments(RepoKey repo, int issueNumber, PageRequest page)
    {
        var pageNumber = page.PageNumber?.ToString() ?? string.Empty;
        var cursor = page.Cursor ?? string.Empty;

        return CacheKey.Create(
            "github.issue.comments.list",
            userScope: null,
            ("owner", repo.Owner),
            ("repo", repo.Name),
            ("issueNumber", issueNumber.ToString()),
            ("pageSize", page.PageSize.ToString()),
            ("pageNumber", pageNumber),
            ("cursor", cursor));
    }
}
