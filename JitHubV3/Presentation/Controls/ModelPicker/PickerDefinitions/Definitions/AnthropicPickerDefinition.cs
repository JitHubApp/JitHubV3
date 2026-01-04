using JitHubV3.Services.Ai.ModelPicker;
using JitHubV3.Services.Platform;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions.Definitions;

public sealed class AnthropicPickerDefinition : IPickerDefinition
{
    private readonly IPlatformCapabilities _capabilities;

    public AnthropicPickerDefinition(IPlatformCapabilities capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public string Id => "anthropic";

    public string DisplayName => "Anthropic";

    public Uri? IconUri => new("ms-appx:///Assets/ModelIcons/key.svg");

    public Type PaneViewModelType => typeof(AnthropicPickerViewModel);

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct)
        => ValueTask.FromResult(_capabilities.SupportsSecureSecretStore);

    public bool Supports(ModelPickerSlot slot)
    {
        return ModelPickerSlotMatching.Supports(slot, "anthropic");
    }
}
