using System.Collections.Concurrent;
using JitHub.GitHub.Abstractions.Security;

namespace JitHubV3.Tests.Ai;

public sealed class TestSecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_secrets.TryGetValue(key, out var value) ? value : null);
    }

    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }
}
