namespace JitHubV3.Server.Options;

internal sealed class OAuthRedirectOptions
{
    public string DefaultWasmRedirectUri { get; init; } = string.Empty;

    public string[] AllowedRedirectOrigins { get; init; } = [];

    // For browser-based redirects we also restrict the path to avoid open-redirect abuse
    // (e.g. redirecting to an allowlisted origin but attacker-controlled path).
    public string[] AllowedRedirectPaths { get; init; } = [];
}
