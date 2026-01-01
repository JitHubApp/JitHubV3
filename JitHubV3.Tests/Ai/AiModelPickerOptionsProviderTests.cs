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

    private sealed class FakeLocalCatalog : IAiLocalModelCatalog
    {
        private readonly IReadOnlyList<AiLocalModelCatalogItem> _items;

        public FakeLocalCatalog(params AiLocalModelCatalogItem[] items) => _items = items;

        public ValueTask<IReadOnlyList<AiLocalModelCatalogItem>> GetCatalogAsync(CancellationToken ct)
            => ValueTask.FromResult(_items);
    }

    [Test]
    public async Task Options_IncludeApiRuntimesWithConfiguredModelIds()
    {
        var catalog = new FakeCatalog(
            new AiRuntimeDescriptor("openai", "OpenAI", RequiresApiKey: true),
            new AiRuntimeDescriptor("anthropic", "Anthropic", RequiresApiKey: true));

        var store = new TestAiModelStore();
        var settingsStore = new TestAiRuntimeSettingsStore();
        var localCatalog = new FakeLocalCatalog();
        var localDefs = Array.Empty<AiLocalModelDefinition>();

        var provider = new AiModelPickerOptionsProvider(
            catalog,
            store,
            localCatalog,
            settingsStore,
            localDefs,
            openAi: new OpenAiRuntimeConfig { ModelId = "gpt-test" },
            anthropic: new AnthropicRuntimeConfig { ModelId = "claude-test" },
            foundry: new AzureAiFoundryRuntimeConfig { ModelId = null });

        var options = await provider.GetOptionsAsync(CancellationToken.None);

        options.Should().Contain(o => o.RuntimeId == "openai" && o.ModelId == "gpt-test" && !o.IsLocal && o.IsDownloaded);
        options.Should().Contain(o => o.RuntimeId == "anthropic" && o.ModelId == "claude-test" && !o.IsLocal && o.IsDownloaded);
    }

    [Test]
    public async Task Options_IncludeLocalInventoryEntries()
    {
        var catalog = new FakeCatalog();
        var store = new TestAiModelStore();
        var settingsStore = new TestAiRuntimeSettingsStore();

        var localCatalog = new FakeLocalCatalog(
            new AiLocalModelCatalogItem(
                ModelId: "phi3-mini",
                DisplayName: "Phi-3 Mini",
                RuntimeId: "local-foundry",
                IsDownloaded: true,
                InstallPath: "C:/models/phi3"));

        var localDefs = new[]
        {
            new AiLocalModelDefinition(
                ModelId: "phi3-mini",
                DisplayName: "Phi-3 Mini",
                RuntimeId: "local-foundry",
                DefaultInstallFolderName: "phi3-mini",
                DownloadUri: "https://example.com/phi3-mini.bin",
                ArtifactFileName: "phi3-mini.bin",
                ExpectedBytes: 123)
        };

        var provider = new AiModelPickerOptionsProvider(
            catalog,
            store,
            localCatalog,
            settingsStore,
            localDefs,
            openAi: new OpenAiRuntimeConfig { ModelId = null },
            anthropic: new AnthropicRuntimeConfig { ModelId = null },
            foundry: new AzureAiFoundryRuntimeConfig { ModelId = null });

        var options = await provider.GetOptionsAsync(CancellationToken.None);

        options.Should().ContainSingle(o =>
            o.IsLocal
            && o.IsDownloaded
            && o.ModelId == "phi3-mini"
            && o.RuntimeId == "local-foundry"
            && o.InstallPath == "C:/models/phi3");
    }

    [Test]
    public async Task Options_IncludeNotDownloadedLocalCatalogItems_WithDownloadMetadata()
    {
        var catalog = new FakeCatalog();
        var store = new TestAiModelStore();
        var settingsStore = new TestAiRuntimeSettingsStore();

        var localCatalog = new FakeLocalCatalog(
            new AiLocalModelCatalogItem(
                ModelId: "m1",
                DisplayName: "Model One",
                RuntimeId: "local-foundry",
                IsDownloaded: false,
                InstallPath: "C:/models/m1"));

        var localDefs = new[]
        {
            new AiLocalModelDefinition(
                ModelId: "m1",
                DisplayName: "Model One",
                RuntimeId: "local-foundry",
                DefaultInstallFolderName: "m1",
                DownloadUri: "https://example.com/m1.bin",
                ArtifactFileName: "m1.bin",
                ExpectedBytes: 456)
        };

        var provider = new AiModelPickerOptionsProvider(
            catalog,
            store,
            localCatalog,
            settingsStore,
            localDefs,
            openAi: new OpenAiRuntimeConfig { ModelId = null },
            anthropic: new AnthropicRuntimeConfig { ModelId = null },
            foundry: new AzureAiFoundryRuntimeConfig { ModelId = null });

        var options = await provider.GetOptionsAsync(CancellationToken.None);

        options.Should().ContainSingle(o =>
            o.IsLocal
            && !o.IsDownloaded
            && o.ModelId == "m1"
            && o.DownloadUri!.ToString() == "https://example.com/m1.bin"
            && o.ArtifactFileName == "m1.bin"
            && o.ExpectedBytes == 456);
    }

    [Test]
    public async Task Options_UseStoreSelectionModelIdForMatchingRuntime()
    {
        var catalog = new FakeCatalog(new AiRuntimeDescriptor("openai", "OpenAI", RequiresApiKey: true));
        var store = new TestAiModelStore
        {
            Selection = new AiModelSelection(RuntimeId: "openai", ModelId: "gpt-selected")
        };

        var settingsStore = new TestAiRuntimeSettingsStore
        {
            Settings = new AiRuntimeSettings(OpenAi: new OpenAiRuntimeSettings(ModelId: "gpt-override"))
        };

        var localCatalog = new FakeLocalCatalog();
        var localDefs = Array.Empty<AiLocalModelDefinition>();

        var provider = new AiModelPickerOptionsProvider(
            catalog,
            store,
            localCatalog,
            settingsStore,
            localDefs,
            openAi: new OpenAiRuntimeConfig { ModelId = "gpt-config" },
            anthropic: new AnthropicRuntimeConfig { ModelId = null },
            foundry: new AzureAiFoundryRuntimeConfig { ModelId = null });

        var options = await provider.GetOptionsAsync(CancellationToken.None);

        options.Should().ContainSingle(o => o.RuntimeId == "openai" && o.ModelId == "gpt-selected");
    }

    [Test]
    public async Task Options_UseOverrideModelId_WhenNoSelection()
    {
        var catalog = new FakeCatalog(new AiRuntimeDescriptor("openai", "OpenAI", RequiresApiKey: true));

        var store = new TestAiModelStore();
        var settingsStore = new TestAiRuntimeSettingsStore
        {
            Settings = new AiRuntimeSettings(OpenAi: new OpenAiRuntimeSettings(ModelId: "gpt-override"))
        };

        var provider = new AiModelPickerOptionsProvider(
            catalog,
            store,
            new FakeLocalCatalog(),
            settingsStore,
            Array.Empty<AiLocalModelDefinition>(),
            openAi: new OpenAiRuntimeConfig { ModelId = null },
            anthropic: new AnthropicRuntimeConfig { ModelId = null },
            foundry: new AzureAiFoundryRuntimeConfig { ModelId = null });

        var options = await provider.GetOptionsAsync(CancellationToken.None);

        options.Should().ContainSingle(o => o.RuntimeId == "openai" && o.ModelId == "gpt-override");
    }
}
