using JitHubV3.Services.Ai.ModelPicker;
using JitHubV3.Services.Platform;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class AzureAiFoundryPickerDefinition : IPickerDefinition
{
    private readonly IPlatformCapabilities _capabilities;

    public AzureAiFoundryPickerDefinition(IPlatformCapabilities capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public string Id => "azure-ai-foundry";

    public string DisplayName => "Azure AI Foundry";

    public Uri? IconUri => null;

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct)
        => ValueTask.FromResult(_capabilities.SupportsSecureSecretStore);

    public bool Supports(ModelPickerSlot slot)
    {
        // Phase 1 scaffolding: no concrete type mapping yet.
        return true;
    }
}
