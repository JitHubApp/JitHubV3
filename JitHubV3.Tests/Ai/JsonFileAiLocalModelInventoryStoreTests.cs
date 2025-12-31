using FluentAssertions;
using JitHubV3.Services.Ai;
using NUnit.Framework;

namespace JitHubV3.Tests.Ai;

public sealed class JsonFileAiLocalModelInventoryStoreTests
{
    [Test]
    public async Task Inventory_SurvivesRestart_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}", "local-models.json");

        var store1 = new JsonFileAiLocalModelInventoryStore(path);
        var inventory = new[]
        {
            new AiLocalModelInventoryEntry(ModelId: "phi3-mini", RuntimeId: "local-foundry", InstallPath: "C:/models/phi3"),
            new AiLocalModelInventoryEntry(ModelId: "llama3", RuntimeId: "local-foundry", InstallPath: "C:/models/llama3"),
        };

        await store1.SetInventoryAsync(inventory, CancellationToken.None);

        var store2 = new JsonFileAiLocalModelInventoryStore(path);
        var loaded = await store2.GetInventoryAsync(CancellationToken.None);

        loaded.Should().BeEquivalentTo(inventory);
    }

    [Test]
    public async Task Inventory_ReturnsEmpty_OnCorruptedJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}", "local-models.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{not-json", CancellationToken.None);

        var store = new JsonFileAiLocalModelInventoryStore(path);
        var loaded = await store.GetInventoryAsync(CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded.Should().BeEmpty();
    }
}
