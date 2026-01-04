using JitHubV3.Services.Ai.ModelPicker;
using System.Collections;
using System.Reflection;

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

        var results = new List<(int Order, string DisplayName, PickerDefinitionDescriptor Descriptor)>();
        foreach (var def in supportedByAnySlot)
        {
            if (!await def.IsAvailableAsync(ct).ConfigureAwait(false))
            {
                continue;
            }

            results.Add((
                Order: TryGetOrderFromGeneratedModelDefinitions(def.Id),
                DisplayName: def.DisplayName,
                Descriptor: new PickerDefinitionDescriptor(def.Id, def.DisplayName, def.IconUri)));
        }

        return results
            .OrderBy(r => r.Order)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Descriptor)
            .ToArray();
    }

    private static int TryGetOrderFromGeneratedModelDefinitions(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return int.MaxValue;
        }

        try
        {
            // NOTE: This registry file is linked into the unit test project, which does not run the source generator.
            // We use reflection so the production app can use the real generated types, while tests can provide a stub.
            var asm = typeof(PickerDefinitionRegistry).Assembly;

            var helpersType = asm.GetType("JitHubV3.Services.Ai.ModelDefinitions.ModelTypeHelpers", throwOnError: false);
            if (helpersType is null)
            {
                return int.MaxValue;
            }

            var modelGroupDetailsProp = helpersType.GetProperty(
                "ModelGroupDetails",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            var modelGroupDetails = modelGroupDetailsProp?.GetValue(null) as IDictionary;
            if (modelGroupDetails is null)
            {
                return int.MaxValue;
            }

            object? matchedModelType = null;
            foreach (DictionaryEntry entry in modelGroupDetails)
            {
                var details = entry.Value;
                if (details is null)
                {
                    continue;
                }

                var idProp = details.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var id = idProp?.GetValue(details) as string;
                if (string.Equals(id, definitionId, StringComparison.OrdinalIgnoreCase))
                {
                    matchedModelType = entry.Key;
                    break;
                }
            }

            if (matchedModelType is null)
            {
                return int.MaxValue;
            }

            var getOrderMethod = helpersType.GetMethod(
                "GetModelOrder",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: [matchedModelType.GetType()],
                modifiers: null);

            var orderObj = getOrderMethod?.Invoke(null, [matchedModelType]);
            return orderObj is int i ? i : int.MaxValue;
        }
        catch
        {
            return int.MaxValue;
        }
    }
}
