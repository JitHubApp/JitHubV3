using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class OnnxPickerDefinition : IPickerDefinition
{
    public string Id => "onnx";

    public string DisplayName => "Custom models";

    public Uri? IconUri => null;

    public Type PaneViewModelType => typeof(OnnxPickerViewModel);

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct) => ValueTask.FromResult(true);

    public bool Supports(ModelPickerSlot slot)
    {
        // Phase 1 scaffolding: required model types are not yet mapped.
        return true;
    }
}
