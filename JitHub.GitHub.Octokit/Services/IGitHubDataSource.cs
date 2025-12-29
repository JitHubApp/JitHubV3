using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Octokit.Mapping;

namespace JitHub.GitHub.Octokit.Services;

internal interface IGitHubDataSource
{
    Task<IReadOnlyList<OctokitRepositoryData>> GetMyRepositoriesAsync(CancellationToken ct);

    Task<IReadOnlyList<OctokitIssueData>> GetIssuesAsync(RepoKey repo, IssueQuery query, PageRequest page, CancellationToken ct);
}
