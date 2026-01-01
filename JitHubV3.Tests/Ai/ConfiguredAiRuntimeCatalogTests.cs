using FluentAssertions;
using JitHub.GitHub.Abstractions.Security;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class ConfiguredAiRuntimeCatalogTests
{
    [Test]
    public async Task GetAvailableRuntimesAsync_ReturnsEmpty_WhenNotConfigured()
    {
        var config = new InMemoryConfiguration();
        var secrets = new TestSecretStore();
        var settingsStore = new TestAiRuntimeSettingsStore();

        var catalog = new ConfiguredAiRuntimeCatalog(config, secrets, settingsStore);

        var available = await catalog.GetAvailableRuntimesAsync(CancellationToken.None);

        available.Should().BeEmpty();
    }

    [Test]
    public async Task GetAvailableRuntimesAsync_ReturnsOpenAi_WhenModelConfiguredAndKeyPresent()
    {
        var config = new InMemoryConfiguration();
        config["Ai:OpenAI:ModelId"] = "gpt-4o-mini";

        var secrets = new TestSecretStore();
        await secrets.SetAsync(AiSecretKeys.OpenAiApiKey, "secret", CancellationToken.None);

        var settingsStore = new TestAiRuntimeSettingsStore();

        var catalog = new ConfiguredAiRuntimeCatalog(config, secrets, settingsStore);

        var available = await catalog.GetAvailableRuntimesAsync(CancellationToken.None);

        available.Should().ContainSingle(r => r.RuntimeId == "openai");
    }

    [Test]
    public async Task GetAvailableRuntimesAsync_UsesOverrides_WhenBaseConfigMissing()
    {
        var config = new InMemoryConfiguration();
        var secrets = new TestSecretStore();
        await secrets.SetAsync(AiSecretKeys.OpenAiApiKey, "secret", CancellationToken.None);

        var settingsStore = new TestAiRuntimeSettingsStore
        {
            Settings = new AiRuntimeSettings(OpenAi: new OpenAiRuntimeSettings(ModelId: "gpt-override"))
        };

        var catalog = new ConfiguredAiRuntimeCatalog(config, secrets, settingsStore);

        var available = await catalog.GetAvailableRuntimesAsync(CancellationToken.None);

        available.Should().ContainSingle(r => r.RuntimeId == "openai");
    }

    private sealed class InMemoryConfiguration : Microsoft.Extensions.Configuration.IConfiguration
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : null;
            set => _values[key] = value;
        }

        public IEnumerable<KeyValuePair<string, string?>> AsEnumerable(bool makePathsRelative = false)
            => _values;

        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key)
            => new InMemorySection(this, key);

        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren()
            => Array.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
            => new NoChangeToken();

        private sealed class InMemorySection : Microsoft.Extensions.Configuration.IConfigurationSection
        {
            private readonly InMemoryConfiguration _root;
            private readonly string _path;

            public InMemorySection(InMemoryConfiguration root, string path)
            {
                _root = root;
                _path = path;
            }

            public string? this[string key]
            {
                get => _root[$"{_path}:{key}"] ?? string.Empty;
                set => _root[$"{_path}:{key}"] = value;
            }

            public string Key => _path.Split(':').Last();

            public string Path => _path;

            public string? Value
            {
                get => _root[_path];
                set => _root[_path] = value;
            }

            public IEnumerable<KeyValuePair<string, string?>> AsEnumerable(bool makePathsRelative = false)
                => _root.AsEnumerable(makePathsRelative);

            public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key)
                => new InMemorySection(_root, string.IsNullOrWhiteSpace(_path) ? key : $"{_path}:{key}");

            public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren()
                => Array.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();

            public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
                => _root.GetReloadToken();
        }

        private sealed class NoChangeToken : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool ActiveChangeCallbacks => false;
            public bool HasChanged => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
                => new NoopDisposable();

            private sealed class NoopDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}
