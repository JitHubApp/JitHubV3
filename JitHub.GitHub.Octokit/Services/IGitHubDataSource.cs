using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal interface IGitHubDataSource
{
    Task<IReadOnlyList<OctokitRepositoryData>> GetMyRepositoriesAsync(CancellationToken ct);

    Task<OctokitRepositoryDetailData?> GetRepositoryAsync(RepoKey repo, CancellationToken ct);

    Task<IReadOnlyList<OctokitActivityEventData>> GetMyActivityAsync(PageRequest page, CancellationToken ct);

    Task<IReadOnlyList<OctokitActivityEventData>> GetRepoActivityAsync(RepoKey repo, PageRequest page, CancellationToken ct);

    Task<IReadOnlyList<OctokitNotificationData>> GetMyNotificationsAsync(bool unreadOnly, PageRequest page, CancellationToken ct);

    Task<IReadOnlyList<OctokitIssueData>> GetIssuesAsync(RepoKey repo, IssueQuery query, PageRequest page, CancellationToken ct);

    Task<IReadOnlyList<OctokitWorkItemData>> SearchIssuesAsync(IssueSearchQuery query, PageRequest page, CancellationToken ct);

    Task<IReadOnlyList<OctokitRepositoryData>> SearchRepositoriesAsync(RepoSearchQuery query, PageRequest page, CancellationToken ct);

    Task<IReadOnlyList<OctokitUserData>> SearchUsersAsync(UserSearchQuery query, PageRequest page, CancellationToken ct);

    Task<IReadOnlyList<OctokitCodeSearchItemData>> SearchCodeAsync(CodeSearchQuery query, PageRequest page, CancellationToken ct);

    Task<OctokitIssueDetailData?> GetIssueAsync(RepoKey repo, int issueNumber, CancellationToken ct);

    Task<IReadOnlyList<OctokitIssueCommentData>> GetIssueCommentsAsync(RepoKey repo, int issueNumber, PageRequest page, CancellationToken ct);
}
