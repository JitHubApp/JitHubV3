using FluentAssertions;
using JitHubV3.Services.Ai;
using NUnit.Framework;

namespace JitHubV3.Tests.Ai;

public sealed class AiModelPickerOptionsProviderTests
{
    private sealed class FakeCatalog : IAiRuntimeCatalog
    {
        private readonly IReadOnlyList<AiRuntimeDescriptor> _items;

        public FakeCatalog(params AiRuntimeDescriptor[] items) => _items = items;

        public Task<IReadOnlyList<AiRuntimeDescriptor>> GetAvailableRuntimesAsync(CancellationToken ct)
            => Task.FromResult(_items);
    }

    private sealed class InMemoryInventory : IAiLocalModelInventoryStore
    {
        public IReadOnlyList<AiLocalModelInventoryEntry> Items { get; set; } = Array.Empty<AiLocalModelInventoryEntry>();

        public ValueTask<IReadOnlyList<AiLocalModelInventoryEntry>> GetInventoryAsync(CancellationToken ct)
            => ValueTask.FromResult(Items);

        public ValueTask SetInventoryAsync(IReadOnlyList<AiLocalModelInventoryEntry> inventory, CancellationToken ct)
        {
            Items = inventory;
            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task Options_IncludeApiRuntimesWithConfiguredModelIds()
    {
        var catalog = new FakeCatalog(
            new AiRuntimeDescriptor("openai", "OpenAI", RequiresApiKey: true),
            new AiRuntimeDescriptor("anthropic", "Anthropic", RequiresApiKey: true));

        var store = new TestAiModelStore();
        var local = new InMemoryInventory();

        var provider = new AiModelPickerOptionsProvider(
            catalog,
            store,
            local,
            openAi: new OpenAiRuntimeConfig { ModelId = "gpt-test" },
            anthropic: new AnthropicRuntimeConfig { ModelId = "claude-test" },
            foundry: new AzureAiFoundryRuntimeConfig { ModelId = null });

        var options = await provider.GetOptionsAsync(CancellationToken.None);

        options.Should().Contain(o => o.RuntimeId == "openai" && o.ModelId == "gpt-test" && !o.IsLocal);
        options.Should().Contain(o => o.RuntimeId == "anthropic" && o.ModelId == "claude-test" && !o.IsLocal);
    }

    [Test]
    public async Task Options_IncludeLocalInventoryEntries()
    {
        var catalog = new FakeCatalog();
        var store = new TestAiModelStore();
        var local = new InMemoryInventory
        {
            Items = new[]
            {
                new AiLocalModelInventoryEntry(ModelId: "phi3-mini", RuntimeId: "local-foundry", InstallPath: "C:/models/phi3")
            }
        };

        var provider = new AiModelPickerOptionsProvider(
            catalog,
            store,
            local,
            openAi: new OpenAiRuntimeConfig { ModelId = null },
            anthropic: new AnthropicRuntimeConfig { ModelId = null },
            foundry: new AzureAiFoundryRuntimeConfig { ModelId = null });

        var options = await provider.GetOptionsAsync(CancellationToken.None);

        options.Should().ContainSingle(o => o.IsLocal && o.ModelId == "phi3-mini" && o.RuntimeId == "local-foundry" && o.InstallPath == "C:/models/phi3");
    }

    [Test]
    public async Task Options_UseStoreSelectionModelIdForMatchingRuntime()
    {
        var catalog = new FakeCatalog(new AiRuntimeDescriptor("openai", "OpenAI", RequiresApiKey: true));
        var store = new TestAiModelStore
        {
            Selection = new AiModelSelection(RuntimeId: "openai", ModelId: "gpt-selected")
        };

        var local = new InMemoryInventory();

        var provider = new AiModelPickerOptionsProvider(
            catalog,
            store,
            local,
            openAi: new OpenAiRuntimeConfig { ModelId = "gpt-config" },
            anthropic: new AnthropicRuntimeConfig { ModelId = null },
            foundry: new AzureAiFoundryRuntimeConfig { ModelId = null });

        var options = await provider.GetOptionsAsync(CancellationToken.None);

        options.Should().ContainSingle(o => o.RuntimeId == "openai" && o.ModelId == "gpt-selected");
    }
}
