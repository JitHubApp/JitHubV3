namespace JitHubV3.Services.Ai.ModelPicker;

public interface IModelPickerService
{
    event Action<ModelPickerSelectedModelsChanged>? SelectedModelsChanged;

    Task<ModelPickerResult> ShowAsync(ModelPickerInvocation invocation, CancellationToken ct);
}

public sealed record ModelPickerSelectedModelsChanged(
    ModelPickerInvocation Invocation,
    IReadOnlyList<PickerSelectedModel> SelectedModels);
