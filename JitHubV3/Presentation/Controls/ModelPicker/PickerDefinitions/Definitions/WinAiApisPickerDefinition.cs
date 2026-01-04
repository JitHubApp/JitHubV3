using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class WinAiApisPickerDefinition : IPickerDefinition
{
    public string Id => "winai";

    public string DisplayName => "Windows AI APIs";

    public Uri? IconUri => new("ms-appx:///Assets/ModelIcons/api.svg");

    public Type PaneViewModelType => typeof(WinAiApisPickerViewModel);

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct)
    {
        // Gap report 2.4: this picker is Windows-only.
        return ValueTask.FromResult(OperatingSystem.IsWindows());
    }

    public bool Supports(ModelPickerSlot slot)
    {
        // Phase 1 scaffolding: no concrete type mapping yet.
        return true;
    }
}
