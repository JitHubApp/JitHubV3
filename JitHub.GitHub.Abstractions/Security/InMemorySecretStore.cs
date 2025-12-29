using System.Collections.Concurrent;

namespace JitHub.GitHub.Abstractions.Security;

/// <summary>
/// In-memory secret store for unit tests. Not secure.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        _values.TryGetValue(key, out var value);
        return ValueTask.FromResult<string?>(value);
    }

    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        _values[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        _values.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }
}
