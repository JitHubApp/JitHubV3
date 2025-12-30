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
    public async Task RepoRecentActivity_ReturnsNoCards_WhenNoSelectedRepo()
    {
        var provider = new RepoRecentActivityDashboardCardProvider(new FakeActivityService([]));
        var ctx = new DashboardContext();

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().BeEmpty();
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
