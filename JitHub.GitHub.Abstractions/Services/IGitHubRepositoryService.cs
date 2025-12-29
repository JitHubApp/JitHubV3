using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubRepositoryService
{
    Task<IReadOnlyList<RepositorySummary>> GetMyRepositoriesAsync(RefreshMode refresh, CancellationToken ct);
}
