using Microsoft.Extensions.Options;
using JitHubV3.Server.Options;

namespace JitHubV3.Server.Services.Auth;

internal sealed class OAuthRedirectPolicy : IOAuthRedirectPolicy
{
    private readonly IOptions<OAuthRedirectOptions> _options;

    public OAuthRedirectPolicy(IOptions<OAuthRedirectOptions> options)
    {
        _options = options;
    }

    public Uri? TryGetAllowedRedirectUri(string? redirectUrl)
    {
        if (string.IsNullOrWhiteSpace(redirectUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(redirectUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // Allow only our custom scheme redirects for native apps.
        if (string.Equals(uri.Scheme, "jithubv3", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Desktop loopback redirects (used by the app for local callback capture).
        // Allow only loopback and only a fixed callback path to avoid open-redirect issues.
        if (uri.IsLoopback)
        {
            if (string.IsNullOrWhiteSpace(uri.Fragment)
                && (string.Equals(uri.AbsolutePath, "/oauth2/callback", StringComparison.Ordinal)
                    || string.Equals(uri.AbsolutePath, "/oauth2/callback/", StringComparison.Ordinal)))
            {
                return uri;
            }

            return null;
        }

        // Disallow fragments for HTTP(S) redirects; caller may append query params (handoffCode).
        if (!string.IsNullOrWhiteSpace(uri.Fragment))
        {
            return null;
        }

        var options = _options.Value;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (!options.AllowedRedirectOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        // If paths are specified, require an exact match.
        // Defense-in-depth to prevent open redirect within an allowlisted origin.
        if (options.AllowedRedirectPaths is { Length: > 0 })
        {
            return options.AllowedRedirectPaths.Any(p => string.Equals(p, uri.AbsolutePath, StringComparison.Ordinal)) ? uri : null;
        }

        return uri;
    }
}
