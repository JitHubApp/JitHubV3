using System.Threading;

namespace JitHub.GitHub.Abstractions.Security;

/// <summary>
/// Simple token provider for unit tests and local harnesses.
/// </summary>
public sealed class InMemoryGitHubTokenProvider : IGitHubTokenProvider
{
    private readonly object _gate = new();
    private string? _token;

    public event EventHandler? TokenChanged;

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_token);
        }
    }

    public void NotifyTokenChanged() => TokenChanged?.Invoke(this, EventArgs.Empty);

    public void SetToken(string? token)
    {
        lock (_gate)
        {
            _token = token;
        }

        NotifyTokenChanged();
    }
}
