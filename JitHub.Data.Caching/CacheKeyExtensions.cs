namespace JitHub.Data.Caching;

public static class CacheKeyExtensions
{
    public static bool TryGetParameterValue(this CacheKey key, string parameterKey, out string? value)
    {
        if (parameterKey is null)
        {
            throw new ArgumentNullException(nameof(parameterKey));
        }

        // CacheKey parameters are normalized and sorted, but lookups are low-volume.
        // Keep this simple and allocation-free.
        foreach (var p in key.Parameters)
        {
            if (string.Equals(p.Key, parameterKey, StringComparison.Ordinal))
            {
                value = p.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    public static string? GetParameterValue(this CacheKey key, string parameterKey)
        => key.TryGetParameterValue(parameterKey, out var value) ? value : null;

    public static string GetParameterValueOrEmpty(this CacheKey key, string parameterKey)
        => key.GetParameterValue(parameterKey) ?? string.Empty;
}
