using System.Collections.Concurrent;

namespace JitHubV3.Server.Services;

internal sealed class AuthHandoffStore
{
    private readonly ConcurrentDictionary<string, HandoffEntry> _entries = new(StringComparer.Ordinal);

    public void Put(string handoffCode, HandoffEntry entry) => _entries[handoffCode] = entry;

    public bool TryConsume(string handoffCode, out HandoffEntry? entry)
    {
        if (!_entries.TryRemove(handoffCode, out var existing))
        {
            entry = null;
            return false;
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            entry = null;
            return false;
        }

        entry = existing;
        return true;
    }

    internal sealed record HandoffEntry(
        string AccessToken,
        string TokenType,
        string Scope,
        DateTimeOffset ExpiresAt
    );
}
