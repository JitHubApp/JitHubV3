namespace JitHubV3.Services.Ai.ModelPicker;

public sealed record PickerSelectedModel(
    string SlotId,
    string RuntimeId,
    string ModelId,
    string? DisplayName)
{
    public string DisplayNameOrId => string.IsNullOrWhiteSpace(DisplayName) ? ModelId : DisplayName;
}
