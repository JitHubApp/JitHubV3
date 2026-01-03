using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions;

public sealed class PickerDefinitionRegistry : IPickerDefinitionRegistry
{
    private readonly IReadOnlyList<IPickerDefinition> _definitions;

    public PickerDefinitionRegistry(IEnumerable<IPickerDefinition> definitions)
    {
        _definitions = (definitions ?? Enumerable.Empty<IPickerDefinition>()).ToArray();
    }

    public IReadOnlyList<IPickerDefinition> GetAll() => _definitions;

    public async Task<IReadOnlyList<PickerDefinitionDescriptor>> GetAvailableAsync(ModelPickerInvocation invocation, CancellationToken ct)
    {
        if (invocation is null) throw new ArgumentNullException(nameof(invocation));

        var supportedByAnySlot = invocation.Slots.Count == 0
            ? _definitions
            : _definitions.Where(d => invocation.Slots.Any(d.Supports));

        var results = new List<PickerDefinitionDescriptor>();
        foreach (var def in supportedByAnySlot)
        {
            if (!await def.IsAvailableAsync(ct).ConfigureAwait(false))
            {
                continue;
            }

            results.Add(new PickerDefinitionDescriptor(def.Id, def.DisplayName, def.IconUri));
        }

        return results;
    }
}
