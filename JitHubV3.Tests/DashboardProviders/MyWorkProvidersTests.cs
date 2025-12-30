using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHubV3.Presentation;

namespace JitHubV3.Tests.DashboardProviders;

public sealed class MyWorkProvidersTests
{
    [Test]
    public async Task MyAssignedWork_ReturnsCard_EvenWhenEmpty()
    {
        var provider = new MyAssignedWorkDashboardCardProvider(new FakeSearchService([]));
        var ctx = new DashboardContext();

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().HaveCount(1);
        cards[0].CardId.Should().Be(30_000_001);
        cards[0].Title.Should().Be("My assigned issues");
    }

    [Test]
    public async Task MyReviewRequests_ReturnsCard_EvenWhenEmpty()
    {
        var provider = new MyReviewRequestsDashboardCardProvider(new FakeSearchService([]));
        var ctx = new DashboardContext();

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().HaveCount(1);
        cards[0].CardId.Should().Be(30_000_002);
        cards[0].Title.Should().Be("Review requests");
    }

    [Test]
    public async Task Providers_HonorCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider1 = new MyAssignedWorkDashboardCardProvider(new FakeSearchService([]));
        var provider2 = new MyReviewRequestsDashboardCardProvider(new FakeSearchService([]));
        var ctx = new DashboardContext();

        Func<Task> act1 = () => provider1.GetCardsAsync(ctx, RefreshMode.CacheOnly, cts.Token);
        Func<Task> act2 = () => provider2.GetCardsAsync(ctx, RefreshMode.CacheOnly, cts.Token);

        await act1.Should().ThrowAsync<OperationCanceledException>();
        await act2.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeSearchService : IGitHubIssueSearchService
    {
        private readonly IReadOnlyList<WorkItemSummary> _items;

        public FakeSearchService(IReadOnlyList<WorkItemSummary> items)
        {
            _items = items;
        }

        public Task<PagedResult<IReadOnlyList<WorkItemSummary>>> SearchAsync(
            IssueSearchQuery query,
            PageRequest page,
            RefreshMode refresh,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<WorkItemSummary> pageItems = _items.Take(page.PageSize).ToArray();
            return Task.FromResult(new PagedResult<IReadOnlyList<WorkItemSummary>>(pageItems, Next: null));
        }
    }
}
