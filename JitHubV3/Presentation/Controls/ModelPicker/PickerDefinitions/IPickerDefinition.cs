using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions;

public interface IPickerDefinition
{
    string Id { get; }
    string DisplayName { get; }
    Uri? IconUri { get; }

    // Phase 4.1: which picker pane to host when this definition is selected.
    // The host resolves this type via DI and uses existing DataTemplates to render it.
    Type PaneViewModelType { get; }

    // Gap report section 2.2: conditional availability (platform/runtime).
    ValueTask<bool> IsAvailableAsync(CancellationToken ct);

    // Gap report section 5.1: context-aware filtering by required model types.
    bool Supports(ModelPickerSlot slot);
}
