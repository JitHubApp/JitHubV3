using FluentAssertions;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class DefaultAiRuntimeDescriptorCatalogTests
{
    [Test]
    public async Task GetDeclaredRuntimesAsync_ReturnsAllKnownRuntimes()
    {
        var catalog = new DefaultAiRuntimeDescriptorCatalog();

        var runtimes = await catalog.GetDeclaredRuntimesAsync(CancellationToken.None);

        runtimes.Select(r => r.RuntimeId)
            .Should()
            .BeEquivalentTo(new[] { "openai", "anthropic", "azure-ai-foundry", "local-foundry" });

        runtimes.Single(r => r.RuntimeId == "azure-ai-foundry").RequiresEndpoint.Should().BeTrue();
        runtimes.Single(r => r.RuntimeId == "local-foundry").SupportsLocalDownloads.Should().BeTrue();
    }
}
