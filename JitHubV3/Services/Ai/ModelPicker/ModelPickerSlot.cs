namespace JitHubV3.Services.Ai.ModelPicker;

public sealed record ModelPickerSlot(
    string SlotId,
    IReadOnlyList<string> RequiredModelTypes,
    string? InitialModelId);
