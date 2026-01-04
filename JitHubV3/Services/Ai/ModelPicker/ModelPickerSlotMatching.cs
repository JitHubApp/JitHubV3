namespace JitHubV3.Services.Ai.ModelPicker;

internal static class ModelPickerSlotMatching
{
    public static bool Supports(ModelPickerSlot slot, params string[] supportedTypeKeys)
    {
        if (slot is null)
        {
            return false;
        }

        if (slot.RequiredModelTypes is null || slot.RequiredModelTypes.Count == 0)
        {
            // No constraints means any picker can satisfy the slot.
            return true;
        }

        if (supportedTypeKeys is null || supportedTypeKeys.Length == 0)
        {
            return false;
        }

        foreach (var required in slot.RequiredModelTypes)
        {
            if (string.IsNullOrWhiteSpace(required))
            {
                continue;
            }

            foreach (var supported in supportedTypeKeys)
            {
                if (string.IsNullOrWhiteSpace(supported))
                {
                    continue;
                }

                if (string.Equals(required, supported, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
