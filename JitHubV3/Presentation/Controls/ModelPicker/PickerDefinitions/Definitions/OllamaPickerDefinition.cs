using JitHubV3.Services.Ai.ExternalProviders;
using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class OllamaPickerDefinition : IPickerDefinition
{
    private readonly IOllamaProbe _probe;

    public OllamaPickerDefinition(IOllamaProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public string Id => "ollama";

    public string DisplayName => "Ollama";

    public Uri? IconUri => new("ms-appx:///Assets/ModelIcons/local.svg");

    public Type PaneViewModelType => typeof(OllamaPickerViewModel);

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct)
        => await _probe.IsAvailableAsync(ct).ConfigureAwait(false);

    public bool Supports(ModelPickerSlot slot)
    {
        // Phase 1 scaffolding: no concrete type mapping yet.
        return true;
    }
}
