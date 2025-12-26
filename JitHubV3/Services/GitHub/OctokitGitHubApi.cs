using Octokit;
using Uno.Extensions.Authentication;

namespace JitHubV3.Services.GitHub;

public sealed class OctokitGitHubApi : IGitHubApi
{
    private readonly ITokenCache _tokenCache;

    public OctokitGitHubApi(ITokenCache tokenCache)
    {
        _tokenCache = tokenCache;
    }

    public async Task<IReadOnlyList<GitHubRepo>> GetMyPrivateReposAsync(CancellationToken cancellationToken)
    {
        var tokens = await _tokenCache.GetAsync(cancellationToken);
        if (tokens is null || !tokens.TryGetValue("access_token", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Missing access_token. Please login first.");
        }

        var client = new GitHubClient(new ProductHeaderValue("JitHubV3"))
        {
            Credentials = new Credentials(accessToken)
        };

        // Request only private repos; GitHub may still return other repos depending on token scopes.
        var request = new RepositoryRequest
        {
            Visibility = RepositoryRequestVisibility.Private,
            Affiliation = RepositoryAffiliation.Owner
        };

        var repos = await client.Repository.GetAllForCurrent(request);

        return repos
            .Where(r => r.Private)
            .Select(r => new GitHubRepo(
                Name: r.Name,
                FullName: r.FullName,
                HtmlUrl: r.HtmlUrl,
                Private: r.Private))
            .ToArray();
    }
}
