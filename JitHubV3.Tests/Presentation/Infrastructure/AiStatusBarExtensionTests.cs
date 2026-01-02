using FluentAssertions;
using JitHubV3.Presentation;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Presentation.Infrastructure;

public sealed class AiStatusBarExtensionTests
{
    [Test]
    public void Segments_update_when_events_are_published()
    {
        var bus = new AiStatusEventBus();
        var enablement = new FakeEnablementStore();
        var modelStore = new FakeModelStore();

        var ext = new AiStatusBarExtension(enablement, modelStore, bus);

        bus.Publish(new AiEnablementChanged(IsEnabled: false));
        bus.Publish(new AiSelectionChanged(new AiModelSelection(RuntimeId: "local-foundry", ModelId: "phi3")));

        ext.Segments.Should().Contain(s => s.Id == "ai-enabled" && s.Text == "AI: Off");
        ext.Segments.Should().Contain(s => s.Id == "ai-runtime" && s.Text == "Runtime: local-foundry");
        ext.Segments.Should().Contain(s => s.Id == "ai-model" && s.Text == "Model: phi3");
    }

    [Test]
    public async Task Segments_initialize_from_stores_best_effort()
    {
        var bus = new AiStatusEventBus();
        var enablement = new FakeEnablementStore(initial: true);
        var modelStore = new FakeModelStore(new AiModelSelection(RuntimeId: "openai", ModelId: "gpt"));

        var ext = new AiStatusBarExtension(enablement, modelStore, bus);

        var ok = await WaitUntilAsync(() =>
            ext.Segments.Any(s => s.Id == "ai-enabled" && s.Text == "AI: On")
            && ext.Segments.Any(s => s.Id == "ai-runtime" && s.Text == "Runtime: openai")
            && ext.Segments.Any(s => s.Id == "ai-model" && s.Text == "Model: gpt"));

        ok.Should().BeTrue();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeoutMs = 250)
    {
        var start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(5);
        }

        return predicate();
    }

    private sealed class FakeEnablementStore : IAiEnablementStore
    {
        private bool _enabled;

        public FakeEnablementStore(bool initial = true) => _enabled = initial;

        public ValueTask<bool> GetIsEnabledAsync(CancellationToken ct) => ValueTask.FromResult(_enabled);

        public ValueTask SetIsEnabledAsync(bool isEnabled, CancellationToken ct)
        {
            _enabled = isEnabled;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeModelStore : IAiModelStore
    {
        private AiModelSelection? _selection;

        public FakeModelStore(AiModelSelection? selection = null) => _selection = selection;

        public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
            => ValueTask.FromResult(_selection);

        public ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
        {
            _selection = selection;
            return ValueTask.CompletedTask;
        }
    }
}
