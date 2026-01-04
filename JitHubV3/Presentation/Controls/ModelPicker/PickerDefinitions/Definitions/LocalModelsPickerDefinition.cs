using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class LocalModelsPickerDefinition : IPickerDefinition
{
    public string Id => "local-models";

    public string DisplayName => "Foundry local";

    public Uri? IconUri => new("ms-appx:///Assets/ModelIcons/local.svg");

    public Type PaneViewModelType => typeof(FoundryLocalPickerViewModel);

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct) => ValueTask.FromResult(true);

    public bool Supports(ModelPickerSlot slot)
    {
        return ModelPickerSlotMatching.Supports(
            slot,
            "local-models",
            "local",
            "foundry-local",
            "local-foundry");
    }
}
