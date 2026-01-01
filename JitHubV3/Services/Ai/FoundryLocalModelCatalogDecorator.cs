namespace JitHubV3.Services.Ai;

/// <summary>
/// Augments an existing local model catalog with models discovered from a local Foundry runtime.
///
/// This is how Phase 4.1 surfaces Foundry models in the existing Model Picker UX:
/// the picker already renders entries returned by <see cref="IAiLocalModelCatalog"/>.
/// </summary>
public sealed class FoundryLocalModelCatalogDecorator : IAiLocalModelCatalog
{
    private readonly IAiLocalModelCatalog _inner;
    private readonly ILocalFoundryClient _foundry;

    public FoundryLocalModelCatalogDecorator(IAiLocalModelCatalog inner, ILocalFoundryClient foundry)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _foundry = foundry ?? throw new ArgumentNullException(nameof(foundry));
    }

    public async ValueTask<IReadOnlyList<AiLocalModelCatalogItem>> GetCatalogAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var baseItems = await _inner.GetCatalogAsync(ct).ConfigureAwait(false);

        if (!_foundry.IsAvailable())
        {
            return baseItems;
        }

        var foundryModels = await _foundry.ListModelsAsync(ct).ConfigureAwait(false);
        if (foundryModels.Count == 0)
        {
            return baseItems;
        }

        var baseKeys = new HashSet<(string RuntimeId, string ModelId)>(
            baseItems.Select(i => (i.RuntimeId, i.ModelId)),
            new RuntimeModelKeyComparer());

        var extra = foundryModels
            .Where(m => !string.IsNullOrWhiteSpace(m.ModelId))
            .Select(m => new AiLocalModelCatalogItem(
                ModelId: m.ModelId,
                DisplayName: m.DisplayName,
                RuntimeId: "local-foundry",
                IsDownloaded: true,
                InstallPath: string.Empty))
            .Where(item => !baseKeys.Contains((item.RuntimeId, item.ModelId)))
            .ToArray();

        if (extra.Length == 0)
        {
            return baseItems;
        }

        return baseItems
            .Concat(extra)
            .OrderBy(i => i.DisplayName ?? i.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class RuntimeModelKeyComparer : IEqualityComparer<(string RuntimeId, string ModelId)>
    {
        public bool Equals((string RuntimeId, string ModelId) x, (string RuntimeId, string ModelId) y)
            => string.Equals(x.RuntimeId, y.RuntimeId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.ModelId, y.ModelId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string RuntimeId, string ModelId) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RuntimeId ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ModelId ?? string.Empty));
    }
}
