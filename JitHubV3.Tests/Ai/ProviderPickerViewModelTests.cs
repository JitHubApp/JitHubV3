using System.Collections.Concurrent;
using JitHub.GitHub.Abstractions.Security;
using JitHubV3.Presentation.Controls.ModelPicker;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class ProviderPickerViewModelTests
{
    [Test]
    public async Task OpenAi_RefreshAsync_loads_effective_model_and_detects_stored_key_without_populating_api_key()
    {
        var settingsStore = new FakeRuntimeSettingsStore(new AiRuntimeSettings(OpenAi: new OpenAiRuntimeSettings(ModelId: "gpt-x")));
        var secrets = new FakeSecretStore(new Dictionary<string, string>
        {
            [AiSecretKeys.OpenAiApiKey] = "stored",
        });
        var modelStore = new FakeModelStore();

        var vm = new OpenAiPickerViewModel(
            settingsStore,
            secrets,
            modelStore,
            baseConfig: new OpenAiRuntimeConfig { ModelId = "base-model" });

        await vm.RefreshAsync(CancellationToken.None);

        vm.ModelId.Should().Be("gpt-x");
        vm.HasStoredApiKey.Should().BeTrue();
        vm.ApiKey.Should().BeNull();
        vm.CanApply.Should().BeTrue();
    }

    [Test]
    public async Task OpenAi_ApplyAsync_persists_model_override_and_selection_and_stores_key_if_provided()
    {
        var settingsStore = new FakeRuntimeSettingsStore(new AiRuntimeSettings());
        var secrets = new FakeSecretStore();
        var modelStore = new FakeModelStore();

        var vm = new OpenAiPickerViewModel(
            settingsStore,
            secrets,
            modelStore,
            baseConfig: new OpenAiRuntimeConfig());

        vm.ModelId = "gpt-4o-mini";
        vm.ApiKey = " key ";

        vm.CanApply.Should().BeTrue();

        await vm.ApplyAsync(CancellationToken.None);

        var updated = settingsStore.Latest;
        updated.OpenAi.Should().NotBeNull();
        updated.OpenAi!.ModelId.Should().Be("gpt-4o-mini");

        secrets.GetValue(AiSecretKeys.OpenAiApiKey).Should().Be("key");

        modelStore.LastSetSelection.Should().Be(new AiModelSelection(RuntimeId: "openai", ModelId: "gpt-4o-mini"));

        vm.HasStoredApiKey.Should().BeTrue();
        vm.ApiKey.Should().BeNull();
    }

    [Test]
    public async Task Anthropic_ApplyAsync_requires_model_and_api_key_or_stored_key()
    {
        var settingsStore = new FakeRuntimeSettingsStore(new AiRuntimeSettings());
        var secrets = new FakeSecretStore();
        var modelStore = new FakeModelStore();

        var vm = new AnthropicPickerViewModel(
            settingsStore,
            secrets,
            modelStore,
            baseConfig: new AnthropicRuntimeConfig());

        vm.ModelId = "";
        vm.ApiKey = "";
        vm.CanApply.Should().BeFalse();

        vm.ModelId = "claude-x";
        vm.CanApply.Should().BeFalse();

        vm.ApiKey = "abc";
        vm.CanApply.Should().BeTrue();

        await vm.ApplyAsync(CancellationToken.None);

        settingsStore.Latest.Anthropic!.ModelId.Should().Be("claude-x");
        secrets.GetValue(AiSecretKeys.AnthropicApiKey).Should().Be("abc");
        modelStore.LastSetSelection.Should().Be(new AiModelSelection(RuntimeId: "anthropic", ModelId: "claude-x"));
    }

    [Test]
    public async Task Foundry_CanApply_false_for_invalid_endpoint_and_true_for_valid_with_key()
    {
        var settingsStore = new FakeRuntimeSettingsStore(new AiRuntimeSettings());
        var secrets = new FakeSecretStore();
        var modelStore = new FakeModelStore();

        var vm = new AzureAiFoundryPickerViewModel(
            settingsStore,
            secrets,
            modelStore,
            baseConfig: new AzureAiFoundryRuntimeConfig { ApiKeyHeaderName = "api-key" });

        // The picker calls RefreshAsync when the category becomes active.
        await vm.RefreshAsync(CancellationToken.None);

        vm.Endpoint = "not a uri";
        vm.ModelId = "dep";
        vm.ApiKey = "x";
        vm.CanApply.Should().BeFalse();

        vm.Endpoint = "https://example.azure.com";
        vm.CanApply.Should().BeTrue();

        await vm.ApplyAsync(CancellationToken.None);

        settingsStore.Latest.AzureAiFoundry!.Endpoint.Should().Be("https://example.azure.com");
        settingsStore.Latest.AzureAiFoundry!.ModelId.Should().Be("dep");
        settingsStore.Latest.AzureAiFoundry!.ApiKeyHeaderName.Should().Be("api-key");

        secrets.GetValue(AiSecretKeys.AzureAiFoundryApiKey).Should().Be("x");
        modelStore.LastSetSelection.Should().Be(new AiModelSelection(RuntimeId: "azure-ai-foundry", ModelId: "dep"));

        vm.ApiKey.Should().BeNull();
        vm.HasStoredApiKey.Should().BeTrue();
    }

    [Test]
    public async Task Foundry_RefreshAsync_uses_effective_config_and_does_not_fill_api_key()
    {
        var settingsStore = new FakeRuntimeSettingsStore(new AiRuntimeSettings(
            AzureAiFoundry: new AzureAiFoundryRuntimeSettings(
                Endpoint: "https://e/",
                ModelId: "m",
                ApiKeyHeaderName: "x-key")));

        var secrets = new FakeSecretStore(new Dictionary<string, string>
        {
            [AiSecretKeys.AzureAiFoundryApiKey] = "stored",
        });

        var modelStore = new FakeModelStore();

        var vm = new AzureAiFoundryPickerViewModel(
            settingsStore,
            secrets,
            modelStore,
            baseConfig: new AzureAiFoundryRuntimeConfig { ApiKeyHeaderName = "api-key" });

        await vm.RefreshAsync(CancellationToken.None);

        vm.Endpoint.Should().Be("https://e/");
        vm.ModelId.Should().Be("m");
        vm.ApiKeyHeaderName.Should().Be("x-key");
        vm.HasStoredApiKey.Should().BeTrue();
        vm.ApiKey.Should().BeNull();
    }

    private sealed class FakeRuntimeSettingsStore : IAiRuntimeSettingsStore
    {
        private AiRuntimeSettings _settings;

        public FakeRuntimeSettingsStore(AiRuntimeSettings initial) => _settings = initial;

        public AiRuntimeSettings Latest => _settings;

        public ValueTask<AiRuntimeSettings> GetAsync(CancellationToken ct)
            => ValueTask.FromResult(_settings);

        public ValueTask SetAsync(AiRuntimeSettings settings, CancellationToken ct)
        {
            _settings = settings;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly ConcurrentDictionary<string, string> _values;

        public FakeSecretStore()
            : this(new Dictionary<string, string>())
        {
        }

        public FakeSecretStore(IDictionary<string, string> initial)
            => _values = new ConcurrentDictionary<string, string>(initial);

        public string? GetValue(string key)
            => _values.TryGetValue(key, out var v) ? v : null;

        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetValue(key));

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            _values[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
        {
            _values.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeModelStore : IAiModelStore
    {
        public AiModelSelection? LastSetSelection { get; private set; }

        public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
            => ValueTask.FromResult<AiModelSelection?>(null);

        public ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
        {
            LastSetSelection = selection;
            return ValueTask.CompletedTask;
        }
    }
}
