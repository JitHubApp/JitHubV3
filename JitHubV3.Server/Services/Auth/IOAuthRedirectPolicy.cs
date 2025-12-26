namespace JitHubV3.Server.Services.Auth;

internal interface IOAuthRedirectPolicy
{
    /// <summary>
    /// Validates and normalizes an app-supplied redirect URL.
    /// Returns null when not allowed.
    /// </summary>
    Uri? TryGetAllowedRedirectUri(string? redirectUrl);
}
