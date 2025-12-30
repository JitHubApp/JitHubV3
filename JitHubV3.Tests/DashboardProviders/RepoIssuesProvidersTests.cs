using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHubV3.Presentation;

using IssueStateModel = JitHub.GitHub.Abstractions.Models.IssueState;

namespace JitHubV3.Tests.DashboardProviders;

public sealed class RepoIssuesProvidersTests
{
    [Test]
    public async Task RepoIssuesSummary_ReturnsEmpty_WhenNoSelectedRepo()
    {
        var provider = new RepoIssuesSummaryDashboardCardProvider(new FakeIssuesService([]));
        var ctx = new DashboardContext { SelectedRepo = null };

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().BeEmpty();
    }

    [Test]
    public async Task RepoRecentlyUpdated_ReturnsEmpty_WhenNoSelectedRepo()
    {
        var provider = new RepoRecentlyUpdatedIssuesDashboardCardProvider(new FakeIssuesService([]));
        var ctx = new DashboardContext { SelectedRepo = null };

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().BeEmpty();
    }

    [Test]
    public async Task RepoIssuesSummary_ProducesStableCardIds_AndDeterministicOrdering()
    {
        var now = DateTimeOffset.Now;
        var issues = new[]
        {
            new IssueSummary(1, 12, "B", IssueStateModel.Open, "a", 10, now.AddMinutes(-10)),
            new IssueSummary(2, 11, "A", IssueStateModel.Open, "a", 2, now.AddMinutes(-5)),
            new IssueSummary(3, 13, "C", IssueStateModel.Open, "a", 1, now.AddMinutes(-30)),
        };

        var provider = new RepoIssuesSummaryDashboardCardProvider(new FakeIssuesService(issues));
        var ctx = new DashboardContext { SelectedRepo = new RepoKey("octocat", "Hello-World") };

        var cards1 = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);
        var cards2 = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards1.Select(c => c.CardId).Should().Equal(20_000_001, 20_000_002);
        cards2.Select(c => c.CardId).Should().Equal(20_000_001, 20_000_002);

        cards1.Should().Equal(cards2);
    }

    [Test]
    public async Task RepoRecentlyUpdated_TakesTopFive_ByUpdatedAtDesc()
    {
        var baseTime = new DateTimeOffset(2025, 12, 29, 8, 0, 0, TimeSpan.Zero);

        var issues = Enumerable.Range(1, 8)
            .Select(i => new IssueSummary(
                Id: i,
                Number: 100 + i,
                Title: $"Issue {i}",
                State: IssueStateModel.Open,
                AuthorLogin: "me",
                CommentCount: 0,
                UpdatedAt: baseTime.AddMinutes(-i)))
            // Reverse so input isn't already sorted.
            .Reverse()
            .ToArray();

        var provider = new RepoRecentlyUpdatedIssuesDashboardCardProvider(new FakeIssuesService(issues));
        var ctx = new DashboardContext { SelectedRepo = new RepoKey("octocat", "Hello-World") };

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().HaveCount(1);
        cards[0].CardId.Should().Be(20_000_003);

        // Most recent should be #101 (i=1), then #102, ...
        cards[0].Summary.Should().Contain("#101");
        cards[0].Summary.Should().Contain("#102");
        cards[0].Summary.Should().Contain("#105");
        cards[0].Summary.Should().NotContain("#106");
        cards[0].Summary.Should().NotContain("#108");
    }

    [Test]
    public void Providers_HonorCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider1 = new RepoIssuesSummaryDashboardCardProvider(new FakeIssuesService([]));
        var provider2 = new RepoRecentlyUpdatedIssuesDashboardCardProvider(new FakeIssuesService([]));
        var ctx = new DashboardContext { SelectedRepo = new RepoKey("octocat", "Hello-World") };

        Func<Task> act1 = () => provider1.GetCardsAsync(ctx, RefreshMode.CacheOnly, cts.Token);
        Func<Task> act2 = () => provider2.GetCardsAsync(ctx, RefreshMode.CacheOnly, cts.Token);

        act1.Should().ThrowAsync<OperationCanceledException>();
        act2.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeIssuesService : IGitHubIssueService
    {
        private readonly IReadOnlyList<IssueSummary> _items;

        public FakeIssuesService(IReadOnlyList<IssueSummary> items)
        {
            _items = items;
        }

        public Task<PagedResult<IReadOnlyList<IssueSummary>>> GetIssuesAsync(
            RepoKey repo,
            IssueQuery query,
            PageRequest page,
            RefreshMode refresh,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<IssueSummary> pageItems = _items.Take(page.PageSize).ToArray();
            return Task.FromResult(new PagedResult<IReadOnlyList<IssueSummary>>(pageItems, Next: null));
        }
    }
}
