namespace JitHubV3.Services.Ai;

public sealed class LocalAiModelCatalog : IAiLocalModelCatalog
{
    private readonly IReadOnlyList<AiLocalModelDefinition> _builtInDefinitions;
    private readonly IAiLocalModelDefinitionStore? _definitionStore;
    private readonly IAiLocalModelInventoryStore _inventory;
    private readonly Func<string, string> _getDefaultInstallPathForFolderName;

    public LocalAiModelCatalog(
        IReadOnlyList<AiLocalModelDefinition> definitions,
        IAiLocalModelDefinitionStore? definitionStore,
        IAiLocalModelInventoryStore inventory,
        Func<string, string>? getDefaultInstallPathForFolderName = null)
    {
        _builtInDefinitions = definitions ?? Array.Empty<AiLocalModelDefinition>();
        _definitionStore = definitionStore;
        _inventory = inventory;
        _getDefaultInstallPathForFolderName = getDefaultInstallPathForFolderName ?? GetDefaultInstallPath;
    }

    public async ValueTask<IReadOnlyList<AiLocalModelCatalogItem>> GetCatalogAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var definitions = await GetDefinitionsAsync(ct).ConfigureAwait(false);

        var inventory = await _inventory.GetInventoryAsync(ct).ConfigureAwait(false);
        var inventoryByModelId = inventory
            .GroupBy(i => i.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return definitions
            .Select(d =>
            {
                if (inventoryByModelId.TryGetValue(d.ModelId, out var entry)
                    && string.Equals(entry.RuntimeId, d.RuntimeId, StringComparison.OrdinalIgnoreCase))
                {
                    return new AiLocalModelCatalogItem(
                        ModelId: d.ModelId,
                        DisplayName: d.DisplayName,
                        RuntimeId: d.RuntimeId,
                        IsDownloaded: true,
                        InstallPath: entry.InstallPath);
                }

                var defaultPath = string.IsNullOrWhiteSpace(d.DefaultInstallFolderName)
                    ? string.Empty
                    : _getDefaultInstallPathForFolderName(d.DefaultInstallFolderName!);

                return new AiLocalModelCatalogItem(
                    ModelId: d.ModelId,
                    DisplayName: d.DisplayName,
                    RuntimeId: d.RuntimeId,
                    IsDownloaded: false,
                    InstallPath: defaultPath);
            })
            .OrderBy(i => i.DisplayName ?? i.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async ValueTask<IReadOnlyList<AiLocalModelDefinition>> GetDefinitionsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_definitionStore is null)
        {
            return _builtInDefinitions;
        }

        var custom = await _definitionStore.GetDefinitionsAsync(ct).ConfigureAwait(false);
        if (custom.Count == 0)
        {
            return _builtInDefinitions;
        }

        var merged = new List<AiLocalModelDefinition>(_builtInDefinitions.Count + custom.Count);
        merged.AddRange(_builtInDefinitions);

        foreach (var d in custom)
        {
            if (merged.Any(x => string.Equals(x.ModelId, d.ModelId, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.RuntimeId, d.RuntimeId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            merged.Add(d);
        }

        return merged;
    }

    private static string GetDefaultInstallPath(string folderName)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "JitHubV3", "ai", "models", folderName);
    }
}
