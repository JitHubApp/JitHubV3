namespace JitHubV3.Server.Services.Auth;

internal static class OAuthScopes
{
    public static string NormalizeScope(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Minimal scope for listing private repos.
            return "repo";
        }

        // Accept space, comma, or semicolon separated lists.
        var tokens = raw
            .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return tokens.Length == 0 ? "repo" : string.Join(' ', tokens);
    }

    public static string[] SplitScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return Array.Empty<string>();
        }

        // GitHub returns scopes as comma-separated string.
        return scope
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
