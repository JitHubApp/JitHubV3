using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubRepositoryDetailsService
{
    Task<RepositorySnapshot?> GetRepositoryAsync(
        RepoKey repo,
        RefreshMode refresh,
        CancellationToken ct);
}
