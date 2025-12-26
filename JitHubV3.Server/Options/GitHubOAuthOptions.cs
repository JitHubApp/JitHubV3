namespace JitHubV3.Server.Options;

internal sealed class GitHubOAuthOptions
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
}
