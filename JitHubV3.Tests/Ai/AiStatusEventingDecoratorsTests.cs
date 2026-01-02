using FluentAssertions;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class AiStatusEventingDecoratorsTests
{
    [Test]
    public async Task Model_store_decorator_publishes_selection_changed_after_set()
    {
        var bus = new AiStatusEventBus();
        var inner = new FakeModelStore();
        var store = new AiModelStoreEventingDecorator(inner, bus);

        var observed = new List<AiStatusEvent>();
        using var _ = bus.Subscribe(observed.Add);

        var selection = new AiModelSelection(RuntimeId: "openai", ModelId: "gpt-4.1-mini");
        await store.SetSelectionAsync(selection, CancellationToken.None);

        inner.Selection.Should().Be(selection);
        observed.OfType<AiSelectionChanged>().Should().ContainSingle()
            .Which.Selection.Should().Be(selection);
    }

    [Test]
    public async Task Enablement_store_decorator_publishes_enablement_changed_after_set()
    {
        var bus = new AiStatusEventBus();
        var inner = new FakeEnablementStore();
        var store = new AiEnablementStoreEventingDecorator(inner, bus);

        var observed = new List<AiStatusEvent>();
        using var _ = bus.Subscribe(observed.Add);

        await store.SetIsEnabledAsync(false, CancellationToken.None);

        inner.IsEnabled.Should().BeFalse();
        observed.OfType<AiEnablementChanged>().Should().ContainSingle()
            .Which.IsEnabled.Should().BeFalse();
    }

    private sealed class FakeModelStore : IAiModelStore
    {
        public AiModelSelection? Selection { get; private set; }

        public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
            => ValueTask.FromResult(Selection);

        public ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
        {
            Selection = selection;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeEnablementStore : IAiEnablementStore
    {
        public bool IsEnabled { get; private set; } = true;

        public ValueTask<bool> GetIsEnabledAsync(CancellationToken ct)
            => ValueTask.FromResult(IsEnabled);

        public ValueTask SetIsEnabledAsync(bool isEnabled, CancellationToken ct)
        {
            IsEnabled = isEnabled;
            return ValueTask.CompletedTask;
        }
    }
}
