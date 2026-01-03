using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class LocalModelsPickerDefinition : IPickerDefinition
{
    public string Id => "local-models";

    public string DisplayName => "Local models";

    public Uri? IconUri => null;

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct) => ValueTask.FromResult(true);

    public bool Supports(ModelPickerSlot slot)
    {
        // Phase 1 scaffolding: required model types are not yet mapped.
        // Treat local models as a general-purpose provider.
        return true;
    }
}
