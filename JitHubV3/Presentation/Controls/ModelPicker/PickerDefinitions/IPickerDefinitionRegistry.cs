using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions;

public interface IPickerDefinitionRegistry
{
    IReadOnlyList<IPickerDefinition> GetAll();

    Task<IReadOnlyList<PickerDefinitionDescriptor>> GetAvailableAsync(ModelPickerInvocation invocation, CancellationToken ct);
}
