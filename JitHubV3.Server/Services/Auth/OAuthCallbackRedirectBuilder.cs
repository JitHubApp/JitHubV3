using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace JitHubV3.Server.Services.Auth;

internal sealed class OAuthCallbackRedirectBuilder
{
    private readonly AuthHandoffStore _handoffStore;

    public OAuthCallbackRedirectBuilder(AuthHandoffStore handoffStore)
    {
        _handoffStore = handoffStore;
    }

    public string BuildRedirect(OAuthStateStore.OAuthStateEntry stateEntry, OAuthTokenResult token)
    {
        // WASM E2E full-page mode: return the access token directly to the WASM origin.
        // Place sensitive values in the URL fragment to avoid them being sent to the UI server.
        if (string.Equals(stateEntry.Client, AuthClientKinds.WasmFullPage, StringComparison.Ordinal))
        {
            var scopeValue = token.Scope ?? stateEntry.Scope;

            var fragment = $"access_token={Uri.EscapeDataString(token.AccessToken)}&token_type={Uri.EscapeDataString(token.TokenType)}";
            if (!string.IsNullOrWhiteSpace(scopeValue))
            {
                fragment += $"&scope={Uri.EscapeDataString(scopeValue)}";
            }

            // stateEntry.RedirectUri is allowlisted (origin + path) and must not contain a fragment.
            return stateEntry.RedirectUri + "#" + fragment;
        }

        // Default mode: store token server-side and return a short-lived handoffCode.
        var handoffCode = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _handoffStore.Put(handoffCode, new AuthHandoffStore.HandoffEntry(
            AccessToken: token.AccessToken,
            TokenType: token.TokenType,
            Scope: token.Scope ?? stateEntry.Scope,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(2)
        ));

        return QueryHelpers.AddQueryString(stateEntry.RedirectUri, "handoffCode", handoffCode);
    }
}
