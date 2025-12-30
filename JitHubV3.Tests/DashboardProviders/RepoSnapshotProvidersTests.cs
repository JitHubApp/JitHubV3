using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHubV3.Presentation;

namespace JitHubV3.Tests.DashboardProviders;

public sealed class RepoSnapshotProvidersTests
{
    [Test]
    public async Task RepoSnapshot_ReturnsEmpty_WhenNoSelectedRepo()
    {
        var provider = new RepoSnapshotDashboardCardProvider(new FakeRepoDetailsService(null));
        var ctx = new DashboardContext { SelectedRepo = null };

        var cards = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards.Should().BeEmpty();
    }

    [Test]
    public async Task RepoSnapshot_ReturnsStableCardId_WhenSelectedRepoPresent()
    {
        var snapshot = new RepositorySnapshot(
            Repo: new RepoKey("octocat", "Hello-World"),
            IsPrivate: false,
            DefaultBranch: "main",
            Description: "Demo",
            UpdatedAt: new DateTimeOffset(2025, 12, 30, 10, 0, 0, TimeSpan.Zero),
            StargazersCount: 12,
            ForksCount: 3,
            WatchersCount: 4);

        var provider = new RepoSnapshotDashboardCardProvider(new FakeRepoDetailsService(snapshot));
        var ctx = new DashboardContext { SelectedRepo = new RepoKey("octocat", "Hello-World") };

        var cards1 = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);
        var cards2 = await provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, CancellationToken.None);

        cards1.Should().HaveCount(1);
        cards1[0].CardId.Should().Be(20_000_003);
        cards1[0].Kind.Should().Be(DashboardCardKind.RepoSnapshot);

        cards1.Should().Equal(cards2);
    }

    [Test]
    public void Provider_HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new RepoSnapshotDashboardCardProvider(new FakeRepoDetailsService(null));
        var ctx = new DashboardContext { SelectedRepo = new RepoKey("octocat", "Hello-World") };

        Func<Task> act = () => provider.GetCardsAsync(ctx, RefreshMode.CacheOnly, cts.Token);
        act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeRepoDetailsService : IGitHubRepositoryDetailsService
    {
        private readonly RepositorySnapshot? _snapshot;

        public FakeRepoDetailsService(RepositorySnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<RepositorySnapshot?> GetRepositoryAsync(RepoKey repo, RefreshMode refresh, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshot);
        }
    }
}
