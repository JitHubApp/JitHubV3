using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubIssueService
{
    Task<PagedResult<IReadOnlyList<IssueSummary>>> GetIssuesAsync(
        RepoKey repo,
        IssueQuery query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct);
}
