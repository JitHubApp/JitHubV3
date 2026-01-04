using JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions;
using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Tests;

public sealed class ModelPickerRegistryTests
{
    private sealed class FakePickerDefinition : IPickerDefinition
    {
        private readonly bool _available;
        private readonly string[] _supportedTypeKeys;

        public FakePickerDefinition(string id, bool available, params string[] supportedTypeKeys)
        {
            Id = id;
            DisplayName = id;
            _available = available;
            _supportedTypeKeys = supportedTypeKeys ?? Array.Empty<string>();
        }

        public string Id { get; }

        public string DisplayName { get; }

        public Uri? IconUri => null;

        public Type PaneViewModelType => typeof(object);

        public ValueTask<bool> IsAvailableAsync(CancellationToken ct) => ValueTask.FromResult(_available);

        public bool Supports(ModelPickerSlot slot)
            => ModelPickerSlotMatching.Supports(slot, _supportedTypeKeys);
    }

    [Test]
    public async Task GetAvailableAsync_WhenSlotHasRequiredType_FiltersToSupportedAndAvailable()
    {
        var registry = new PickerDefinitionRegistry(
        [
            new FakePickerDefinition("openai", available: true, "openai"),
            new FakePickerDefinition("anthropic", available: true, "anthropic"),
            new FakePickerDefinition("openai-disabled", available: false, "openai"),
        ]);

        var invocation = new ModelPickerInvocation(
            PrimaryAction: PickerPrimaryAction.Apply,
            Slots:
            [
                new ModelPickerSlot(
                    SlotId: "slot-1",
                    RequiredModelTypes: ["openai"],
                    InitialModelId: null)
            ],
            PersistSelection: false);

        var results = await registry.GetAvailableAsync(invocation, CancellationToken.None);

        results.Select(r => r.Id).Should().BeEquivalentTo(["openai"]);
    }

    [Test]
    public async Task GetAvailableAsync_WhenNoSlots_DoesNotInvokeSupportFiltering()
    {
        var registry = new PickerDefinitionRegistry(
        [
            new FakePickerDefinition("openai", available: true, "openai"),
            new FakePickerDefinition("anthropic", available: true, "anthropic"),
            new FakePickerDefinition("disabled", available: false, "openai", "anthropic"),
        ]);

        var invocation = new ModelPickerInvocation(
            PrimaryAction: PickerPrimaryAction.Apply,
            Slots: Array.Empty<ModelPickerSlot>(),
            PersistSelection: false);

        var results = await registry.GetAvailableAsync(invocation, CancellationToken.None);

        results.Select(r => r.Id).Should().BeEquivalentTo(["openai", "anthropic"]);
    }
}
