namespace JitHubV3.Services.Ai;

public sealed class LocalAiModelCatalog : IAiLocalModelCatalog
{
    private readonly IReadOnlyList<AiLocalModelDefinition> _definitions;
    private readonly IAiLocalModelInventoryStore _inventory;
    private readonly Func<string, string> _getDefaultInstallPathForFolderName;

    public LocalAiModelCatalog(
        IReadOnlyList<AiLocalModelDefinition> definitions,
        IAiLocalModelInventoryStore inventory,
        Func<string, string>? getDefaultInstallPathForFolderName = null)
    {
        _definitions = definitions ?? Array.Empty<AiLocalModelDefinition>();
        _inventory = inventory;
        _getDefaultInstallPathForFolderName = getDefaultInstallPathForFolderName ?? GetDefaultInstallPath;
    }

    public async ValueTask<IReadOnlyList<AiLocalModelCatalogItem>> GetCatalogAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var inventory = await _inventory.GetInventoryAsync(ct).ConfigureAwait(false);
        var inventoryByModelId = inventory
            .GroupBy(i => i.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return _definitions
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

    private static string GetDefaultInstallPath(string folderName)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "JitHubV3", "ai", "models", folderName);
    }
}
