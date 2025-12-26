using System.Collections.Concurrent;

namespace JitHubV3.Server.Services;

internal sealed class OAuthStateStore
{
    private readonly ConcurrentDictionary<string, OAuthStateEntry> _entries = new(StringComparer.Ordinal);

    public void Put(string state, OAuthStateEntry entry) => _entries[state] = entry;

    public bool TryConsume(string state, out OAuthStateEntry? entry)
    {
        if (!_entries.TryRemove(state, out var existing))
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

    internal sealed record OAuthStateEntry(
        string Client,
        string RedirectUri,
        string Scope,
        DateTimeOffset ExpiresAt
    );
}
