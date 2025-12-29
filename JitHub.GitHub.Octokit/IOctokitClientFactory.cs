using Octokit;

namespace JitHub.GitHub.Octokit;

public interface IOctokitClientFactory
{
    ValueTask<GitHubClient> CreateAsync(CancellationToken cancellationToken);
}
