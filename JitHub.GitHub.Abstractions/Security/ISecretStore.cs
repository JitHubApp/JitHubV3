namespace JitHub.GitHub.Abstractions.Security;

/// <summary>
/// Secure storage abstraction for secrets (e.g., OAuth tokens) when the app needs explicit control.
/// This is intentionally small so it can be backed by platform keychain/credential stores.
/// </summary>
public interface ISecretStore
{
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken);

    ValueTask SetAsync(string key, string value, CancellationToken cancellationToken);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);
}
