namespace JitHubV3.Services.Ai;

public sealed record AiModelPickerOption(
    string RuntimeId,
    string ModelId,
    string DisplayName,
    bool IsLocal,
    string? InstallPath = null);

public interface IAiModelPickerOptionsProvider
{
    Task<IReadOnlyList<AiModelPickerOption>> GetOptionsAsync(CancellationToken ct);
}

public sealed class AiModelPickerOptionsProvider : IAiModelPickerOptionsProvider
{
    private readonly IAiRuntimeCatalog _runtimeCatalog;
    private readonly IAiModelStore _modelStore;
    private readonly IAiLocalModelInventoryStore _localInventory;
    private readonly OpenAiRuntimeConfig _openAi;
    private readonly AnthropicRuntimeConfig _anthropic;
    private readonly AzureAiFoundryRuntimeConfig _foundry;

    public AiModelPickerOptionsProvider(
        IAiRuntimeCatalog runtimeCatalog,
        IAiModelStore modelStore,
        IAiLocalModelInventoryStore localInventory,
        OpenAiRuntimeConfig openAi,
        AnthropicRuntimeConfig anthropic,
        AzureAiFoundryRuntimeConfig foundry)
    {
        _runtimeCatalog = runtimeCatalog;
        _modelStore = modelStore;
        _localInventory = localInventory;
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
                IsLocal: false));
        }

        var local = await _localInventory.GetInventoryAsync(ct);
        foreach (var entry in local)
        {
            options.Add(new AiModelPickerOption(
                RuntimeId: entry.RuntimeId,
                ModelId: entry.ModelId,
                DisplayName: $"Local · {entry.ModelId}",
                IsLocal: true,
                InstallPath: entry.InstallPath));
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
