namespace JitHubV3.Services.Ai.ModelPicker;

public sealed record ModelPickerInvocation(
    PickerPrimaryAction PrimaryAction,
    IReadOnlyList<ModelPickerSlot> Slots,
    bool PersistSelection);
