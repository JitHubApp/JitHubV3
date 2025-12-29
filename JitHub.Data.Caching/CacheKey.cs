using System.Collections.Immutable;

namespace JitHub.Data.Caching;

public readonly record struct CacheKey
{
    private readonly ImmutableArray<KeyValuePair<string, string>> _parameters;

    public CacheKey(string operation, string? userScope, IEnumerable<KeyValuePair<string, string>>? parameters = null)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        operation = operation.Trim();
        if (operation.Length == 0)
        {
            throw new ArgumentException("Operation must not be empty.", nameof(operation));
        }

        Operation = operation;
        UserScope = string.IsNullOrWhiteSpace(userScope) ? null : userScope.Trim();

        _parameters = Normalize(parameters);
    }

    public string Operation { get; }

    public string? UserScope { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Parameters => _parameters;

    public static CacheKey Create(string operation, string? userScope = null, params (string Key, string Value)[] parameters)
        => new(operation, userScope, parameters.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));

    public bool Equals(CacheKey other)
    {
        if (!StringComparer.Ordinal.Equals(Operation, other.Operation))
        {
            return false;
        }

        if (!StringComparer.Ordinal.Equals(UserScope, other.UserScope))
        {
            return false;
        }

        if (_parameters.Length != other._parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < _parameters.Length; i++)
        {
            var a = _parameters[i];
            var b = other._parameters[i];

            if (!StringComparer.Ordinal.Equals(a.Key, b.Key) || !StringComparer.Ordinal.Equals(a.Value, b.Value))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Operation, StringComparer.Ordinal);
        hash.Add(UserScope, StringComparer.Ordinal);

        for (var i = 0; i < _parameters.Length; i++)
        {
            var p = _parameters[i];
            hash.Add(p.Key, StringComparer.Ordinal);
            hash.Add(p.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        if (_parameters.IsDefaultOrEmpty)
        {
            return UserScope is null ? Operation : $"{Operation}@{UserScope}";
        }

        var scope = UserScope is null ? string.Empty : $"@{UserScope}";
        return $"{Operation}{scope}?{string.Join("&", _parameters.Select(p => $"{p.Key}={p.Value}"))}";
    }

    private static ImmutableArray<KeyValuePair<string, string>> Normalize(IEnumerable<KeyValuePair<string, string>>? parameters)
    {
        if (parameters is null)
        {
            return ImmutableArray<KeyValuePair<string, string>>.Empty;
        }

        var list = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in parameters)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(parameters), "Parameter key must not be null.");
            }

            if (value is null)
            {
                throw new ArgumentNullException(nameof(parameters), "Parameter value must not be null.");
            }

            var normalizedKey = key.Trim();
            if (normalizedKey.Length == 0)
            {
                throw new ArgumentException("Parameter key must not be empty.", nameof(parameters));
            }

            list.Add(new KeyValuePair<string, string>(normalizedKey, value.Trim()));
        }

        list.Sort(static (a, b) =>
        {
            var cmp = StringComparer.Ordinal.Compare(a.Key, b.Key);
            return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.Value, b.Value);
        });

        return list.ToImmutableArray();
    }
}
