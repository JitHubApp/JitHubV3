using JitHub.GitHub.Abstractions.Security;
using Uno.Extensions.Authentication;

namespace JitHubV3.Services.GitHub;

public sealed class UnoTokenCacheGitHubTokenProvider : IGitHubTokenProvider
{
    private readonly ITokenCache _tokenCache;
    private readonly object _gate = new();
    private string? _lastKnownToken;

    public UnoTokenCacheGitHubTokenProvider(ITokenCache tokenCache)
    {
        _tokenCache = tokenCache;
    }

    public event EventHandler? TokenChanged;

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_lastKnownToken))
            {
                return _lastKnownToken;
            }
        }

        var tokens = await _tokenCache.GetAsync(cancellationToken).ConfigureAwait(false);
        if (tokens is null || !tokens.TryGetValue("access_token", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        lock (_gate)
        {
            _lastKnownToken = accessToken;
            return _lastKnownToken;
        }
    }

    public void NotifyTokenChanged() => TokenChanged?.Invoke(this, EventArgs.Empty);

    public void UpdateFromTokens(IDictionary<string, string>? tokens)
    {
        var token = (tokens is not null && tokens.TryGetValue("access_token", out var accessToken) && !string.IsNullOrWhiteSpace(accessToken))
            ? accessToken
            : null;

        lock (_gate)
        {
            _lastKnownToken = token;
        }

        NotifyTokenChanged();
    }
}
