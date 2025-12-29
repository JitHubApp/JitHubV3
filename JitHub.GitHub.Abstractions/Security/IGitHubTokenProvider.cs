namespace JitHub.GitHub.Abstractions.Security;

/// <summary>
/// Provides the current GitHub access token for API calls.
/// Implementations are responsible for retrieving the token from the app's authentication flow.
/// </summary>
public interface IGitHubTokenProvider
{
    /// <summary>
    /// Raised when the access token may have changed (login/logout/token update).
    /// Consumers can recreate clients or invalidate cached auth state.
    /// </summary>
    event EventHandler? TokenChanged;

    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    void NotifyTokenChanged();
}
