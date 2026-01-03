using FluentAssertions;
using JitHubV3.Services.Ai;
using NUnit.Framework;

namespace JitHubV3.Tests.Ai;

public sealed class LocalAiModelCatalogTests
{
    private sealed class InMemoryInventoryStore : IAiLocalModelInventoryStore
    {
        public IReadOnlyList<AiLocalModelInventoryEntry> Inventory { get; set; } = Array.Empty<AiLocalModelInventoryEntry>();

        public ValueTask<IReadOnlyList<AiLocalModelInventoryEntry>> GetInventoryAsync(CancellationToken ct)
            => ValueTask.FromResult(Inventory);

        public ValueTask SetInventoryAsync(IReadOnlyList<AiLocalModelInventoryEntry> inventory, CancellationToken ct)
        {
            Inventory = inventory;
            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task Catalog_MarksDownloaded_WhenPresentInInventory()
    {
        var defs = new[]
        {
            new AiLocalModelDefinition(ModelId: "m1", DisplayName: "Model 1", RuntimeId: "local-foundry", DefaultInstallFolderName: "m1"),
            new AiLocalModelDefinition(ModelId: "m2", DisplayName: "Model 2", RuntimeId: "local-foundry", DefaultInstallFolderName: "m2"),
        };

        var inv = new InMemoryInventoryStore
        {
            Inventory = new[]
            {
                new AiLocalModelInventoryEntry(ModelId: "m2", RuntimeId: "local-foundry", InstallPath: "C:/models/m2")
            }
        };

        var catalog = new LocalAiModelCatalog(
            defs,
            definitionStore: null,
            inv,
            folderName => $"C:/base/{folderName}");

        var items = await catalog.GetCatalogAsync(CancellationToken.None);

        items.Should().HaveCount(2);

        items.Single(i => i.ModelId == "m2").IsDownloaded.Should().BeTrue();
        items.Single(i => i.ModelId == "m2").InstallPath.Should().Be("C:/models/m2");

        items.Single(i => i.ModelId == "m1").IsDownloaded.Should().BeFalse();
        items.Single(i => i.ModelId == "m1").InstallPath.Should().Be("C:/base/m1");
    }
}
