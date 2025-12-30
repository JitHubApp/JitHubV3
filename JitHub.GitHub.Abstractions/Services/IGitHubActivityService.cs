using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubActivityService
{
    Task<PagedResult<IReadOnlyList<ActivitySummary>>> GetMyActivityAsync(
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct);

    Task<PagedResult<IReadOnlyList<ActivitySummary>>> GetRepoActivityAsync(
        RepoKey repo,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct);
}
