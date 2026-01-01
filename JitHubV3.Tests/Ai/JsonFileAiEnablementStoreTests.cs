using FluentAssertions;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class JsonFileAiEnablementStoreTests
{
    [Test]
    public async Task GetIsEnabledAsync_DefaultsToTrue_WhenFileMissing()
    {
        var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "enablement.json");

        var store = new JsonFileAiEnablementStore(filePath);

        var isEnabled = await store.GetIsEnabledAsync(CancellationToken.None);

        isEnabled.Should().BeTrue();
    }

    [Test]
    public async Task SetIsEnabledAsync_PersistsValue_AndGetReadsItBack()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "enablement.json");

        var store = new JsonFileAiEnablementStore(filePath);

        await store.SetIsEnabledAsync(false, CancellationToken.None);
        (await store.GetIsEnabledAsync(CancellationToken.None)).Should().BeFalse();

        await store.SetIsEnabledAsync(true, CancellationToken.None);
        (await store.GetIsEnabledAsync(CancellationToken.None)).Should().BeTrue();
    }
}
