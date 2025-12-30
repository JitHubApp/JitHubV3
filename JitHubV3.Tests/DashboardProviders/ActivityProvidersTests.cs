using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHubV3.Presentation;

namespace JitHubV3.Tests.DashboardProviders;

public sealed class ActivityProvidersTests
{
    [Test]
    public async Task MyRecentActivity_ReturnsCard_EvenWhenEmpty()
    {
        var provider = new MyRecentActivityDashboardCardProvider(new FakeActivityService([]));
        var ctx = new DashboardContext();

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().HaveCount(1);
        cards[0].CardId.Should().Be(30_000_004);
        cards[0].Title.Should().Be("Recent activity");
    }

    [Test]
    public async Task MyRecentActivity_ReturnsOneCardPerItem_WhenNotEmpty()
    {
        var items = new[]
        {
            new ActivitySummary(
                Id: "evt_1",
                Repo: new RepoKey("octo", "one"),
                Type: "PushEvent",
                ActorLogin: "alice",
                Description: "pushed commits",
                CreatedAt: new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            new ActivitySummary(
                Id: "evt_2",
                Repo: new RepoKey("octo", "two"),
                Type: "IssuesEvent",
                ActorLogin: "bob",
                Description: null,
                CreatedAt: new DateTimeOffset(2025, 1, 2, 3, 4, 6, TimeSpan.Zero)),
        };

        var provider = new MyRecentActivityDashboardCardProvider(new FakeActivityService(items));
        var ctx = new DashboardContext();

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().HaveCount(2);
        cards.Select(c => c.CardId).Should().OnlyHaveUniqueItems();
        cards.All(c => c.Kind == DashboardCardKind.MyRecentActivity).Should().BeTrue();
    }

    [Test]
    public async Task RepoRecentActivity_ReturnsNoCards_WhenNoSelectedRepo()
    {
        var provider = new RepoRecentActivityDashboardCardProvider(new FakeActivityService([]));
        var ctx = new DashboardContext();

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().BeEmpty();
    }

    [Test]
    public async Task RepoRecentActivity_ReturnsOneCardPerItem_WhenSelectedRepo()
    {
        var items = new[]
        {
            new ActivitySummary(
                Id: "evt_3",
                Repo: new RepoKey("octo", "repo"),
                Type: "WatchEvent",
                ActorLogin: "carol",
                Description: "starred",
                CreatedAt: new DateTimeOffset(2025, 1, 2, 3, 4, 7, TimeSpan.Zero)),
            new ActivitySummary(
                Id: "evt_4",
                Repo: new RepoKey("octo", "repo"),
                Type: "ForkEvent",
                ActorLogin: "dave",
                Description: null,
                CreatedAt: new DateTimeOffset(2025, 1, 2, 3, 4, 8, TimeSpan.Zero)),
            new ActivitySummary(
                Id: "evt_5",
                Repo: new RepoKey("octo", "repo"),
                Type: "IssueCommentEvent",
                ActorLogin: null,
                Description: "commented",
                CreatedAt: new DateTimeOffset(2025, 1, 2, 3, 4, 9, TimeSpan.Zero)),
        };

        var provider = new RepoRecentActivityDashboardCardProvider(new FakeActivityService(items));
        var ctx = new DashboardContext
        {
            SelectedRepo = new RepoKey("octo", "repo"),
        };

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().HaveCount(3);
        cards.Select(c => c.CardId).Should().OnlyHaveUniqueItems();
        cards.All(c => c.Kind == DashboardCardKind.RepoRecentActivity).Should().BeTrue();
    }

    [Test]
    public async Task Providers_HonorCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider1 = new MyRecentActivityDashboardCardProvider(new FakeActivityService([]));
        var provider2 = new RepoRecentActivityDashboardCardProvider(new FakeActivityService([]));
        var ctx = new DashboardContext();

        Func<Task> act1 = () => provider1.GetCardsAsync(ctx, RefreshMode.CacheOnly, cts.Token);
        Func<Task> act2 = () => provider2.GetCardsAsync(ctx, RefreshMode.CacheOnly, cts.Token);

        await act1.Should().ThrowAsync<OperationCanceledException>();
        await act2.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeActivityService : IGitHubActivityService
    {
        private readonly IReadOnlyList<ActivitySummary> _items;

        public FakeActivityService(IReadOnlyList<ActivitySummary> items)
        {
            _items = items;
        }

        public Task<PagedResult<IReadOnlyList<ActivitySummary>>> GetMyActivityAsync(PageRequest page, RefreshMode refresh, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<ActivitySummary> pageItems = _items.Take(page.PageSize).ToArray();
            return Task.FromResult(new PagedResult<IReadOnlyList<ActivitySummary>>(pageItems, Next: null));
        }

        public Task<PagedResult<IReadOnlyList<ActivitySummary>>> GetRepoActivityAsync(RepoKey repo, PageRequest page, RefreshMode refresh, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<ActivitySummary> pageItems = _items.Take(page.PageSize).ToArray();
            return Task.FromResult(new PagedResult<IReadOnlyList<ActivitySummary>>(pageItems, Next: null));
        }
    }
}
