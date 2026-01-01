namespace JitHubV3.Services.Ai;

public sealed record AiModelPickerOption(
    string RuntimeId,
    string ModelId,
    string DisplayName,
    bool IsLocal,
    bool IsDownloaded,
    string? InstallPath = null,
    Uri? DownloadUri = null,
    string? ArtifactFileName = null,
    long? ExpectedBytes = null,
    string? ExpectedSha256 = null);

public interface IAiModelPickerOptionsProvider
{
    Task<IReadOnlyList<AiModelPickerOption>> GetOptionsAsync(CancellationToken ct);
}

public sealed class AiModelPickerOptionsProvider : IAiModelPickerOptionsProvider
{
    private readonly IAiRuntimeCatalog _runtimeCatalog;
    private readonly IAiModelStore _modelStore;
    private readonly IAiLocalModelCatalog _localCatalog;
    private readonly OpenAiRuntimeConfig _openAi;
    private readonly AnthropicRuntimeConfig _anthropic;
    private readonly AzureAiFoundryRuntimeConfig _foundry;
    private readonly IReadOnlyList<AiLocalModelDefinition> _localDefinitions;

    public AiModelPickerOptionsProvider(
        IAiRuntimeCatalog runtimeCatalog,
        IAiModelStore modelStore,
        IAiLocalModelCatalog localCatalog,
        IReadOnlyList<AiLocalModelDefinition> localDefinitions,
        OpenAiRuntimeConfig openAi,
        AnthropicRuntimeConfig anthropic,
        AzureAiFoundryRuntimeConfig foundry)
    {
        _runtimeCatalog = runtimeCatalog;
        _modelStore = modelStore;
        _localCatalog = localCatalog;
        _localDefinitions = localDefinitions ?? Array.Empty<AiLocalModelDefinition>();
        _openAi = openAi;
        _anthropic = anthropic;
        _foundry = foundry;
    }

    public async Task<IReadOnlyList<AiModelPickerOption>> GetOptionsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var selection = await _modelStore.GetSelectionAsync(ct);

        var runtimes = await _runtimeCatalog.GetAvailableRuntimesAsync(ct);
        var options = new List<AiModelPickerOption>(capacity: runtimes.Count + 8);

        foreach (var r in runtimes)
        {
            var modelId = GetConfiguredModelIdOrSelection(r.RuntimeId, selection);
            if (string.IsNullOrWhiteSpace(modelId))
            {
                continue;
            }

            options.Add(new AiModelPickerOption(
                RuntimeId: r.RuntimeId,
                ModelId: modelId,
                DisplayName: $"API · {r.DisplayName} · {modelId}",
                IsLocal: false,
                IsDownloaded: true));
        }

        var catalog = await _localCatalog.GetCatalogAsync(ct);
        foreach (var item in catalog)
        {
            var def = _localDefinitions.FirstOrDefault(d =>
                string.Equals(d.ModelId, item.ModelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.RuntimeId, item.RuntimeId, StringComparison.OrdinalIgnoreCase));

            Uri? downloadUri = null;
            if (def?.DownloadUri is not null && Uri.TryCreate(def.DownloadUri, UriKind.Absolute, out var parsed))
            {
                downloadUri = parsed;
            }

            options.Add(new AiModelPickerOption(
                RuntimeId: item.RuntimeId,
                ModelId: item.ModelId,
                DisplayName: item.IsDownloaded
                    ? $"Local · {item.DisplayName ?? item.ModelId}"
                    : $"Local · {item.DisplayName ?? item.ModelId} (not downloaded)",
                IsLocal: true,
                IsDownloaded: item.IsDownloaded,
                InstallPath: item.InstallPath,
                DownloadUri: downloadUri,
                ArtifactFileName: def?.ArtifactFileName,
                ExpectedBytes: def?.ExpectedBytes,
                ExpectedSha256: def?.ExpectedSha256));
        }

        return options
            .OrderBy(o => o.IsLocal ? 0 : 1)
            .ThenBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? GetConfiguredModelIdOrSelection(string runtimeId, AiModelSelection? selection)
    {
        if (selection is not null && string.Equals(selection.RuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase))
        {
            return selection.ModelId;
        }

        return runtimeId switch
        {
            "openai" => _openAi.ModelId,
            "anthropic" => _anthropic.ModelId,
            "azure-ai-foundry" => _foundry.ModelId,
            _ => null,
        };
    }
}
