namespace JitHubV3.Services.GitHub;

public interface IGitHubApi
{
    Task<IReadOnlyList<GitHubRepo>> GetMyPrivateReposAsync(CancellationToken cancellationToken);
}

public sealed record GitHubRepo(string Name, string FullName, string HtmlUrl, bool Private);
