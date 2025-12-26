namespace JitHubV3.Server.Services.Auth;

internal sealed record OAuthTokenResult(
    string AccessToken,
    string TokenType,
    string? Scope
);
