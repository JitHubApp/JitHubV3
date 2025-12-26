using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JitHubV3.Server.Services.Auth;

internal sealed class GitHubOAuthClient : IGitHubOAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubOAuthClient> _logger;

    public GitHubOAuthClient(IHttpClientFactory httpClientFactory, ILogger<GitHubOAuthClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<OAuthTokenResult> ExchangeCodeForTokenAsync(
        string code,
        string state,
        Uri callbackUri,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var oauthClient = _httpClientFactory.CreateClient("GitHubOAuth");

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["redirect_uri"] = callbackUri.ToString(),
                ["state"] = state,
            })
        };
        tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var tokenResponse = await oauthClient.SendAsync(tokenRequest, cancellationToken);
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub token exchange failed: {StatusCode} {Body}", (int)tokenResponse.StatusCode, tokenJson);
            throw new GitHubOAuthException(
                message: $"GitHub token exchange failed ({(int)tokenResponse.StatusCode}).",
                statusCode: (int)tokenResponse.StatusCode,
                responseBody: tokenJson);
        }

        var payload = JsonSerializer.Deserialize<GitHubTokenResponse>(tokenJson, JsonOptions);
        if (payload is null || !string.IsNullOrWhiteSpace(payload.Error))
        {
            _logger.LogWarning("GitHub token exchange returned error payload: {Body}", tokenJson);
            throw new GitHubOAuthException(
                message: "GitHub token exchange returned an error.",
                statusCode: 400,
                responseBody: tokenJson);
        }

        if (string.IsNullOrWhiteSpace(payload.AccessToken) || string.IsNullOrWhiteSpace(payload.TokenType))
        {
            _logger.LogWarning("GitHub token exchange returned an invalid payload: {Body}", tokenJson);
            throw new GitHubOAuthException(
                message: "GitHub token exchange returned an invalid payload.",
                statusCode: 400,
                responseBody: tokenJson);
        }

        return new OAuthTokenResult(payload.AccessToken, payload.TokenType, payload.Scope);
    }

    private sealed record GitHubTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }
}
