using FluentAssertions;
using JitHubV3.Presentation;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Presentation;

public sealed class FoundrySetupDashboardCardProviderTests
{
    [Test]
    public async Task ReturnsEmpty_WhenSelectionIsNotLocalFoundry()
    {
        var modelStore = new FakeAiModelStore(new AiModelSelection("openai", "gpt"));
        var foundry = new FakeFoundryClient(isAvailable: false, models: Array.Empty<LocalFoundryModel>());
        var provider = new FoundrySetupDashboardCardProvider(modelStore, foundry);

        var cards = await provider.GetCardsAsync(new DashboardContext(), refresh: default, CancellationToken.None);

        cards.Should().BeEmpty();
    }

    [Test]
    public async Task ReturnsEmpty_WhenFoundryIsAvailable()
    {
        var modelStore = new FakeAiModelStore(new AiModelSelection("local-foundry", "any"));
        var foundry = new FakeFoundryClient(isAvailable: true, models: new[] { new LocalFoundryModel("qwen2.5-0.5b") });
        var provider = new FoundrySetupDashboardCardProvider(modelStore, foundry);

        var cards = await provider.GetCardsAsync(new DashboardContext(), refresh: default, CancellationToken.None);

        cards.Should().BeEmpty();
    }

    [Test]
    public async Task ReturnsGuidanceCard_WhenFoundryIsAvailableButNoModels()
    {
        var modelStore = new FakeAiModelStore(new AiModelSelection("local-foundry", "any"));
        var foundry = new FakeFoundryClient(isAvailable: true, models: Array.Empty<LocalFoundryModel>());
        var provider = new FoundrySetupDashboardCardProvider(modelStore, foundry);

        var cards = await provider.GetCardsAsync(new DashboardContext(), refresh: default, CancellationToken.None);

        cards.Should().HaveCount(1);
        cards[0].CardId.Should().Be(DashboardCardId.FoundrySetup);
        cards[0].Kind.Should().Be(DashboardCardKind.FoundrySetup);
        cards[0].Actions.Should().NotBeNull();
        cards[0].Actions!.Should().HaveCount(1);
        cards[0].Actions![0].Label.Should().Be("Open setup guide");
    }

    [Test]
    public async Task ReturnsGuidanceCard_WhenLocalFoundrySelectedButUnavailable()
    {
        var modelStore = new FakeAiModelStore(new AiModelSelection("local-foundry", "any"));
        var foundry = new FakeFoundryClient(isAvailable: false, models: Array.Empty<LocalFoundryModel>());
        var provider = new FoundrySetupDashboardCardProvider(modelStore, foundry);

        var cards = await provider.GetCardsAsync(new DashboardContext(), refresh: default, CancellationToken.None);

        cards.Should().HaveCount(1);
        cards[0].CardId.Should().Be(DashboardCardId.FoundrySetup);
        cards[0].Kind.Should().Be(DashboardCardKind.FoundrySetup);
        cards[0].Actions.Should().NotBeNull();
        cards[0].Actions!.Should().HaveCount(1);
        cards[0].Actions![0].Label.Should().Be("Open setup guide");
    }

    private sealed class FakeAiModelStore : IAiModelStore
    {
        private readonly AiModelSelection? _selection;

        public FakeAiModelStore(AiModelSelection? selection) => _selection = selection;

        public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct) => ValueTask.FromResult(_selection);

        public ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class FakeFoundryClient : ILocalFoundryClient
    {
        private readonly bool _isAvailable;
        private readonly IReadOnlyList<LocalFoundryModel> _models;

        public FakeFoundryClient(bool isAvailable, IReadOnlyList<LocalFoundryModel> models)
        {
            _isAvailable = isAvailable;
            _models = models;
        }

        public bool IsAvailable() => _isAvailable;

        public ValueTask<IReadOnlyList<LocalFoundryModel>> ListModelsAsync(CancellationToken ct)
            => ValueTask.FromResult(_models);

        public ValueTask<string?> TryBuildQueryPlanJsonAsync(string modelId, string input, CancellationToken ct)
            => ValueTask.FromResult<string?>(null);
    }
}
