using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubIssueSearchService
{
    Task<PagedResult<IReadOnlyList<WorkItemSummary>>> SearchAsync(
        IssueSearchQuery query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct);
}
