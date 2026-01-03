namespace JitHubV3.Services.Ai.ModelPicker;

public interface IModelPickerService
{
    Task<ModelPickerResult> ShowAsync(ModelPickerInvocation invocation, CancellationToken ct);
}
