using FluentAssertions;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class JsonFileAiRuntimeSettingsStoreTests
{
    [Test]
    public async Task GetAsync_DefaultsToEmpty_WhenFileMissing()
    {
        var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "runtime-settings.json");

        var store = new JsonFileAiRuntimeSettingsStore(filePath);

        var settings = await store.GetAsync(CancellationToken.None);

        settings.Should().NotBeNull();
        settings.OpenAi.Should().BeNull();
        settings.Anthropic.Should().BeNull();
        settings.AzureAiFoundry.Should().BeNull();
    }

    [Test]
    public async Task SetAsync_Persists_AndGetReadsBack()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "runtime-settings.json");

        var store = new JsonFileAiRuntimeSettingsStore(filePath);

        var input = new AiRuntimeSettings(
            OpenAi: new OpenAiRuntimeSettings(ModelId: "gpt-test"),
            Anthropic: new AnthropicRuntimeSettings(ModelId: "claude-test"),
            AzureAiFoundry: new AzureAiFoundryRuntimeSettings(
                Endpoint: "https://example.test",
                ModelId: "foundry-model",
                ApiKeyHeaderName: "x-api-key"));

        await store.SetAsync(input, CancellationToken.None);

        var roundTrip = await store.GetAsync(CancellationToken.None);

        roundTrip.OpenAi!.ModelId.Should().Be("gpt-test");
        roundTrip.Anthropic!.ModelId.Should().Be("claude-test");
        roundTrip.AzureAiFoundry!.Endpoint.Should().Be("https://example.test");
        roundTrip.AzureAiFoundry!.ModelId.Should().Be("foundry-model");
        roundTrip.AzureAiFoundry!.ApiKeyHeaderName.Should().Be("x-api-key");
    }
}
