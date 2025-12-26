using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Windows.Security.Authentication.Web;
#if WINDOWS || __WINDOWS__
using Windows.System;
#endif
#if __WASM__
using Uno.Foundation;
#endif

namespace JitHubV3.Authentication;

internal static class GitHubAuthFlow
{
    // Default callback URI for native broker-based platforms.
    // Windows does NOT use WebAuthenticationBroker in this app (see below); it uses browser + protocol activation.
    internal const string DefaultCallbackUri = "jithubv3://authentication-callback";

#if WINDOWS || __WINDOWS__
    private static TaskCompletionSource<string>? _protocolHandoffTcs;
    private static string? _pendingHandoffCode;

    internal static bool HasPendingProtocolHandoff() => !string.IsNullOrWhiteSpace(_pendingHandoffCode);

    internal static void TryHandleProtocolActivation(Uri uri)
    {
        // Expected: jithubv3://auth?handoffCode=...
        if (!string.Equals(uri.Scheme, "jithubv3", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var handoffCode = GetQueryParameter(uri, "handoffCode");
        if (string.IsNullOrWhiteSpace(handoffCode))
        {
            return;
        }

        // If a login is currently waiting, complete it.
        // Otherwise store it so startup/login can consume it and skip the login screen.
        if (!(_protocolHandoffTcs?.TrySetResult(handoffCode) ?? false))
        {
            _pendingHandoffCode = handoffCode;
        }
    }

    private static Task<string> BeginWindowsProtocolWait(TimeSpan timeout)
    {
        _protocolHandoffTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!string.IsNullOrWhiteSpace(_pendingHandoffCode))
        {
            var handoff = _pendingHandoffCode;
            _pendingHandoffCode = null;
            _protocolHandoffTcs.TrySetResult(handoff);
            return _protocolHandoffTcs.Task;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout);
                _protocolHandoffTcs.TrySetException(new TimeoutException("Timed out waiting for protocol callback."));
            }
            catch
            {
                // ignored
            }
        });

        return _protocolHandoffTcs.Task;
    }
#endif

    internal static async Task<IDictionary<string, string>> LoginAsync(IServiceProvider services, IDictionary<string, string>? credentials)
    {
        var config = services.GetRequiredService<IConfiguration>();

        var startUriRaw = config["WebAuthentication:LoginStartUri"];
        if (string.IsNullOrWhiteSpace(startUriRaw))
        {
            throw new InvalidOperationException("Missing configuration value WebAuthentication:LoginStartUri");
        }

        var logoutUriRaw = config["WebAuthentication:LogoutStartUri"];

    #if __WASM__
        // On WebAssembly the broker detects completion by polling the popup window URL from the opener.
        // That requires the popup to end on the SAME origin as the WASM app; otherwise browser cross-origin
        // restrictions prevent reading popup.location and the broker never completes.
        var uiOrigin = GetBrowserOrigin();
        var callbackUri = new Uri($"{uiOrigin}/authentication-callback.html");
    #else
        // Native platforms (Windows, iOS, Android, macOS) use registered schemes.
        var callbackUriRaw = config["WebAuthentication:CallbackUri"];
        var callbackUri = new Uri(string.IsNullOrWhiteSpace(callbackUriRaw) ? DefaultCallbackUri : callbackUriRaw);
    #endif

        var scope = credentials is not null && credentials.TryGetValue("scope", out var scopeFromCreds) && !string.IsNullOrWhiteSpace(scopeFromCreds)
            ? scopeFromCreds
            : "repo";

#if WINDOWS || __WINDOWS__
        // Windows: follow the old app's proven pattern.
        // - Open the server auth start URL in the system browser
        // - Server completes OAuth then redirects back into the app via protocol activation
        // This avoids WebAuthenticationBroker callback URI restrictions that caused COMException 0x8007007D.
        var startUri = new Uri(AppendQuery(startUriRaw, new Dictionary<string, string>
        {
            ["client"] = "windows",
            ["scope"] = scope,
        }));

        var handoffWait = BeginWindowsProtocolWait(TimeSpan.FromMinutes(2));
        var launched = await Launcher.LaunchUriAsync(startUri);
        if (!launched)
        {
            throw new InvalidOperationException("Failed to launch system browser for authentication.");
        }

        var handoffCode = await handoffWait;
        var exchangeUri = new Uri($"{startUri.GetLeftPart(UriPartial.Authority)}/auth/exchange");
#else
        // Other platforms: use WebAuthenticationBroker.
        var startUri = new Uri(AppendQuery(startUriRaw, new Dictionary<string, string>
        {
    #if __WASM__
            ["client"] = "wasm",
            ["redirect_uri"] = callbackUri.ToString(),
    #else
            ["client"] = "native",
            ["redirect_uri"] = callbackUri.ToString(),
    #endif
            ["scope"] = scope,
        }));

        var result = await WebAuthenticationBroker.AuthenticateAsync(WebAuthenticationOptions.None, startUri, callbackUri);
        if (result.ResponseStatus != WebAuthenticationStatus.Success || string.IsNullOrWhiteSpace(result.ResponseData))
        {
            throw new InvalidOperationException($"Authentication failed: {result.ResponseStatus}");
        }

        var responseUri = new Uri(result.ResponseData);
        var handoffCode = GetQueryParameter(responseUri, "handoffCode");
        if (string.IsNullOrWhiteSpace(handoffCode))
        {
            throw new InvalidOperationException("Missing handoffCode in authentication callback.");
        }

        var exchangeUri = new Uri($"{startUri.GetLeftPart(UriPartial.Authority)}/auth/exchange");
#endif

        using var http = new HttpClient();
        var exchangeResponse = await http.PostAsJsonAsync(exchangeUri, new { handoffCode });
        exchangeResponse.EnsureSuccessStatusCode();

        var payload = await exchangeResponse.Content.ReadFromJsonAsync<AuthExchangeResponseDto>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken) || string.IsNullOrWhiteSpace(payload.TokenType))
        {
            throw new InvalidOperationException("Invalid response from /auth/exchange.");
        }

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Standard keys used by typical OAuth token handlers
            ["access_token"] = payload.AccessToken,
            ["token_type"] = payload.TokenType,
        };

        if (payload.Scopes is { Length: > 0 })
        {
            tokens["scope"] = string.Join(' ', payload.Scopes);
        }

        if (payload.User is not null)
        {
            tokens["github_user_id"] = payload.User.Id.ToString();
            if (!string.IsNullOrWhiteSpace(payload.User.Login))
            {
                tokens["github_user_login"] = payload.User.Login;
            }
        }

        // Optional hint used by some logout flows.
        if (!string.IsNullOrWhiteSpace(logoutUriRaw))
        {
            tokens["logout_start_uri"] = logoutUriRaw;
        }

        return tokens;
    }

#if __WASM__
    private static string GetBrowserOrigin()
    {
        // location.origin includes scheme + host + port (e.g., http://localhost:5000)
        var origin = WebAssemblyRuntime.InvokeJS("globalThis.location && globalThis.location.origin ? globalThis.location.origin : ''");
        if (string.IsNullOrWhiteSpace(origin))
        {
            throw new InvalidOperationException("Unable to determine browser origin for WebAssembly redirect URI.");
        }

        return origin;
    }

    internal static async Task TryConsumeAndPersistWasmRedirectTokensAsync(IServiceProvider services)
    {
        try
        {
            // Prefer fragment because it's not sent to the server.
            var href = WebAssemblyRuntime.InvokeJS("globalThis.location && globalThis.location.href ? globalThis.location.href : ''");
            if (string.IsNullOrWhiteSpace(href))
            {
                return;
            }

            var accessToken = TryGetUriParam(href, "access_token");
            var tokenType = TryGetUriParam(href, "token_type");
            var scope = TryGetUriParam(href, "scope");

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(tokenType))
            {
                return;
            }

            var tokenCache = services.GetRequiredService<Uno.Extensions.Authentication.ITokenCache>();
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["access_token"] = accessToken,
                ["token_type"] = tokenType,
            };

            if (!string.IsNullOrWhiteSpace(scope))
            {
                tokens["scope"] = scope;
            }

            // Persist under the configured provider name used in App.xaml.cs ("GitHub").
            await tokenCache.SaveAsync("GitHub", tokens, CancellationToken.None);

            // Ensure the token does NOT remain visible in the address bar.
            // Some browsers/devservers can preserve fragments despite replaceState during early startup;
            // force a clean reload without the fragment.
            WebAssemblyRuntime.InvokeJS("globalThis.location && globalThis.location.replace ? globalThis.location.replace(globalThis.location.origin + '/') : void 0");
        }
        catch
        {
            // Non-fatal: worst-case the user stays logged out.
        }
    }

    private static string? TryGetUriParam(string href, string key)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // Check query first.
        var queryValue = GetQueryParameter(uri, key);
        if (!string.IsNullOrWhiteSpace(queryValue))
        {
            return queryValue;
        }

        // Then check fragment formatted as key=value&key2=value2.
        var fragment = uri.Fragment;
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return null;
        }

        var trimmed = fragment.TrimStart('#');
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(kv[0]);
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                continue;
            }

            return kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
        }

        return null;
    }
#endif

    private static string AppendQuery(string baseUri, IReadOnlyDictionary<string, string> query)
    {
        var separator = baseUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var sb = new System.Text.StringBuilder(baseUri);
        foreach (var (key, value) in query)
        {
            sb.Append(separator);
            separator = "&";
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }
        return sb.ToString();
    }

    private static string? GetQueryParameter(Uri uri, string key)
    {
        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(kv[0]);
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                continue;
            }

            return kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
        }

        return null;
    }

    private sealed class AuthExchangeResponseDto
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("tokenType")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scopes")]
        public string[]? Scopes { get; set; }

        [JsonPropertyName("user")]
        public AuthUserDto? User { get; set; }
    }

    private sealed class AuthUserDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("login")]
        public string? Login { get; set; }
    }
}
