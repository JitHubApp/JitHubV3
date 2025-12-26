namespace JitHubV3.Server.Services.Auth;

internal interface IGitHubOAuthClient
{
    Task<OAuthTokenResult> ExchangeCodeForTokenAsync(
        string code,
        string state,
        Uri callbackUri,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken);
}
