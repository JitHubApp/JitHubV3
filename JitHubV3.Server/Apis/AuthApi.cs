using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using JitHubV3.Server.Options;
using JitHubV3.Server.Services;
using JitHubV3.Server.Services.Auth;

namespace JitHubV3.Server.Apis;

internal static class AuthApi
{
    private const string Tag = "Auth";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static WebApplication MapAuthApi(this WebApplication app)
    {
        var group = app.MapGroup("/auth")
            .WithTags(Tag);

        group.MapGet("/start", Start)
            .WithName("AuthStart");

        group.MapGet("/callback", Callback)
            .WithName("AuthCallback");

        group.MapPost("/exchange", Exchange)
            .WithName("AuthExchange");

        group.MapGet("/logout", Logout)
            .WithName("AuthLogout");

        return app;
    }

    private static IResult Logout()
    {
        // Minimal logout endpoint (GitHub OAuth tokens are not revoked here).
        // This exists to support a basic “logout flow” through the broker.
        const string html = "<!doctype html><html><head><meta charset=\"utf-8\"/><title>Logged out</title></head>" +
                            "<body><h3>Logged out</h3><p>You can close this window.</p></body></html>";
        return Results.Text(html, "text/html", Encoding.UTF8);
    }

    private static IResult Start(
        HttpContext http,
        IOptions<GitHubOAuthOptions> options,
        IOptions<OAuthRedirectOptions> redirectOptions,
        OAuthStateStore stateStore,
        IOAuthRedirectPolicy redirectPolicy)
    {
        var clientId = options.Value.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Results.Problem("GitHub ClientId is not configured (GitHub:ClientId).", statusCode: 500);
        }

        // Scope input: accept comma, semicolon, or space separated.
        var scopeRaw = (string?)http.Request.Query["scope"];
        var scope = OAuthScopes.NormalizeScope(scopeRaw);

        // The app tells us which client initiated the login.
        // We then choose the appropriate post-auth redirect target.
        // This mirrors the old JitHub pattern: native app starts auth via web endpoint, web redirects back via protocol.
        var client = ((string?)http.Request.Query["client"])?.Trim().ToLowerInvariant();
        var redirectUriFromQuery = (string?)http.Request.Query["redirect_uri"];
        var redirectUri = ResolveRedirectUri(client, redirectUriFromQuery, redirectOptions.Value, redirectPolicy);
        if (redirectUri is null)
        {
            return Results.BadRequest("Invalid or unsupported redirect_uri.");
        }

        var callbackUri = $"{http.Request.Scheme}://{http.Request.Host}/auth/callback";

        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        stateStore.Put(state, new OAuthStateStore.OAuthStateEntry(
            Client: string.IsNullOrWhiteSpace(client) ? AuthClientKinds.Wasm : client,
            RedirectUri: redirectUri,
            Scope: scope,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10)
        ));

        var authorizeUrl = QueryHelpers.AddQueryString(
            "https://github.com/login/oauth/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = callbackUri,
                ["scope"] = scope,
                ["state"] = state,
            });

        return Results.Redirect(authorizeUrl);
    }

    private static string? ResolveRedirectUri(
        string? client,
        string? redirectUriFromQuery,
        OAuthRedirectOptions redirectOptions,
        IOAuthRedirectPolicy redirectPolicy)
    {
        // Default client if not specified.
        client = string.IsNullOrWhiteSpace(client) ? AuthClientKinds.Wasm : client;

        // For WASM we redirect back to the UI host origin. We only accept allowlisted HTTP(S) origins.
        if (string.Equals(client, AuthClientKinds.Wasm, StringComparison.Ordinal)
            || string.Equals(client, AuthClientKinds.WasmFullPage, StringComparison.Ordinal))
        {
            var candidate = !string.IsNullOrWhiteSpace(redirectUriFromQuery)
                ? redirectUriFromQuery
                : redirectOptions.DefaultWasmRedirectUri;

            var allowed = redirectPolicy.TryGetAllowedRedirectUri(candidate);
            if (allowed is null)
            {
                return null;
            }

            return (string.Equals(allowed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(allowed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                ? candidate
                : null;
        }

        // For Windows we redirect into the app via its registered protocol (old-app style).
        if (string.Equals(client, AuthClientKinds.Windows, StringComparison.Ordinal))
        {
            return "/auth/complete?client=windows";
        }

        // Desktop loopback flow: accept an explicit loopback redirect_uri (http://127.0.0.1:{port}/oauth2/callback).
        if (string.Equals(client, AuthClientKinds.Desktop, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(redirectUriFromQuery))
            {
                return null;
            }

            var allowed = redirectPolicy.TryGetAllowedRedirectUri(redirectUriFromQuery);
            if (allowed is null || !allowed.IsLoopback)
            {
                return null;
            }

            return allowed.ToString();
        }

        // For native broker-based clients (iOS/Android/macOS/etc), accept an explicit redirect_uri
        // but only when it matches our allowlist rules.
        if (!string.IsNullOrWhiteSpace(redirectUriFromQuery))
        {
            var allowed = redirectPolicy.TryGetAllowedRedirectUri(redirectUriFromQuery);
            if (allowed is not null && string.Equals(allowed.Scheme, "jithubv3", StringComparison.OrdinalIgnoreCase))
            {
                return redirectUriFromQuery;
            }
        }

        // No valid redirect target.
        return null;
    }

    private static async Task<IResult> Callback(
        HttpContext http,
        IOptions<GitHubOAuthOptions> options,
        OAuthStateStore stateStore,
        IGitHubOAuthClient oauthClient,
        OAuthCallbackRedirectBuilder redirectBuilder)
    {
        var clientId = options.Value.ClientId;
        var clientSecret = options.Value.ClientSecret;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return Results.Problem(
                "GitHub OAuth is not fully configured. Ensure GitHub:ClientId and GitHub:ClientSecret are set for JitHubV3.Server. " +
                "For local dev you can use dotnet user-secrets (recommended) or set environment variables GitHub__ClientId and GitHub__ClientSecret.",
                statusCode: 500);
        }

        var code = (string?)http.Request.Query["code"];
        var state = (string?)http.Request.Query["state"];
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return Results.BadRequest("Missing required query parameters: code/state.");
        }

        if (!stateStore.TryConsume(state, out var stateEntry) || stateEntry is null)
        {
            return Results.BadRequest("Invalid or expired OAuth state.");
        }

        var callbackUri = $"{http.Request.Scheme}://{http.Request.Host}/auth/callback";

        OAuthTokenResult token;
        try
        {
            token = await oauthClient.ExchangeCodeForTokenAsync(
                code,
                state,
                new Uri(callbackUri),
                clientId,
                clientSecret,
                http.RequestAborted);
        }
        catch (GitHubOAuthException ex)
        {
            return Results.Problem(
                title: ex.Message,
                detail: ex.ResponseBody,
                statusCode: ex.StatusCode);
        }

        // Build redirect (handoffCode default / token-in-fragment wasm-fullpage).
        var redirect = redirectBuilder.BuildRedirect(stateEntry, token);
        return Results.Redirect(redirect);
    }

    private static async Task<IResult> Exchange(
        ExchangeRequest request,
        AuthHandoffStore handoffStore,
        IHttpClientFactory httpClientFactory,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(request.HandoffCode))
        {
            return Results.BadRequest("Missing handoffCode.");
        }

        if (!handoffStore.TryConsume(request.HandoffCode, out var entry) || entry is null)
        {
            return Results.BadRequest("Invalid or expired handoffCode.");
        }

        GitHubUserResponse? user = null;
        try
        {
            var apiClient = httpClientFactory.CreateClient("GitHubApi");
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", entry.AccessToken);

            using var userResponse = await apiClient.SendAsync(userRequest, http.RequestAborted);
            if (userResponse.IsSuccessStatusCode)
            {
                var userJson = await userResponse.Content.ReadAsStringAsync(http.RequestAborted);
                user = JsonSerializer.Deserialize<GitHubUserResponse>(userJson, JsonOptions);
            }
        }
        catch
        {
            // Non-fatal: token exchange succeeded; user lookup is best-effort.
        }

        var scopes = OAuthScopes.SplitScopes(entry.Scope);

        return Results.Json(new
        {
            accessToken = entry.AccessToken,
            tokenType = entry.TokenType,
            scopes,
            user = user is null ? null : new { id = user.Id, login = user.Login },
        }, JsonOptions);
    }

    private sealed record ExchangeRequest(string HandoffCode);

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

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }

        [JsonPropertyName("error_uri")]
        public string? ErrorUri { get; init; }
    }

    private sealed record GitHubUserResponse(long Id, string Login);
}
