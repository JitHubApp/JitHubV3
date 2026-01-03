namespace JitHubV3.Services.Ai.ModelPicker;

public sealed record ModelPickerResult(
    bool WasConfirmed,
    IReadOnlyList<PickerSelectedModel> SelectedModels);
