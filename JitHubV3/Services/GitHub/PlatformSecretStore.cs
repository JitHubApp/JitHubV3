using JitHub.GitHub.Abstractions.Security;

namespace JitHubV3.Services.GitHub;

public sealed class PlatformSecretStore : ISecretStore
{
    private readonly ISecretStore _inner;

    public PlatformSecretStore()
    {
#if WINDOWS
        _inner = new PasswordVaultSecretStore();
#else
        _inner = new UnsupportedSecretStore();
#endif
    }

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken) => _inner.GetAsync(key, cancellationToken);

    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken) => _inner.SetAsync(key, value, cancellationToken);

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken) => _inner.RemoveAsync(key, cancellationToken);

    private sealed class UnsupportedSecretStore : ISecretStore
    {
        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken)
            => throw new PlatformNotSupportedException("No platform secret store configured for this target.");

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken)
            => throw new PlatformNotSupportedException("No platform secret store configured for this target.");

        public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
            => throw new PlatformNotSupportedException("No platform secret store configured for this target.");
    }
}
