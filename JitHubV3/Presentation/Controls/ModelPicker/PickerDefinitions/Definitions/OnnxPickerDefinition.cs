using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class OnnxPickerDefinition : IPickerDefinition
{
    public string Id => "onnx";

    public string DisplayName => "Custom models";

    public Uri? IconUri => new("ms-appx:///Assets/ModelIcons/chip.svg");

    public Type PaneViewModelType => typeof(OnnxPickerViewModel);

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct) => ValueTask.FromResult(true);

    public bool Supports(ModelPickerSlot slot)
    {
        return ModelPickerSlotMatching.Supports(
            slot,
            "onnx",
            "custom-models",
            "custom-model",
            "local-onnx");
    }
}
