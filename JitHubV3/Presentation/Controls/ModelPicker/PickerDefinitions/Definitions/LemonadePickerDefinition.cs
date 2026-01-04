using JitHubV3.Services.Ai.ExternalProviders;
using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class LemonadePickerDefinition : IPickerDefinition
{
    private readonly ILemonadeProbe _probe;

    public LemonadePickerDefinition(ILemonadeProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public string Id => "lemonade";

    public string DisplayName => "Lemonade";

    public Uri? IconUri => new("ms-appx:///Assets/ModelIcons/api.svg");

    public Type PaneViewModelType => typeof(LemonadePickerViewModel);

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct)
        => await _probe.IsAvailableAsync(ct).ConfigureAwait(false);

    public bool Supports(ModelPickerSlot slot)
    {
        // Phase 1 scaffolding: no concrete type mapping yet.
        return true;
    }
}
