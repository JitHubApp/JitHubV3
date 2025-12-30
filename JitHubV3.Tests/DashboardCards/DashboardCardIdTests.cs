using FluentAssertions;
using JitHubV3.Presentation;

namespace JitHubV3.Tests.DashboardCards;

public sealed class DashboardCardIdTests
{
    private const long NotificationsItemBase = 30_000_003_000_000;
    private const long MyRecentActivityItemBase = 30_000_004_000_000;
    private const long RepoRecentActivityItemBase = 20_000_004_000_000;
    private const long FeedModulo = 1_000_000;

    [Test]
    public void FixedCardIds_AreUnique()
    {
        var fixedIds = new[]
        {
            DashboardCardId.RepoRecentlyUpdatedIssues,
            DashboardCardId.RepoSnapshot,
            DashboardCardId.RepoRecentActivityEmpty,
            DashboardCardId.MyRecentActivityEmpty,
            DashboardCardId.NotificationsEmpty,
        };

        fixedIds.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void RepoSnapshot_And_RepoRecentlyUpdatedIssues_AreDistinct_RegressionGuard()
    {
        DashboardCardId.RepoSnapshot.Should().NotBe(DashboardCardId.RepoRecentlyUpdatedIssues);
    }

    [Test]
    public void NotificationsItem_IsStable_AndInExpectedRange()
    {
        var id1 = DashboardCardId.NotificationsItem("notification_123");
        var id2 = DashboardCardId.NotificationsItem("notification_123");

        id1.Should().Be(id2);
        id1.Should().NotBe(DashboardCardId.NotificationsEmpty);
        id1.Should().BeGreaterThanOrEqualTo(NotificationsItemBase);
        id1.Should().BeLessThan(NotificationsItemBase + FeedModulo);
    }

    [Test]
    public void MyRecentActivityItem_IsStable_AndInExpectedRange()
    {
        var id1 = DashboardCardId.MyRecentActivityItem("evt_123");
        var id2 = DashboardCardId.MyRecentActivityItem("evt_123");

        id1.Should().Be(id2);
        id1.Should().NotBe(DashboardCardId.MyRecentActivityEmpty);
        id1.Should().BeGreaterThanOrEqualTo(MyRecentActivityItemBase);
        id1.Should().BeLessThan(MyRecentActivityItemBase + FeedModulo);
    }

    [Test]
    public void RepoRecentActivityItem_IsStable_AndInExpectedRange()
    {
        var id1 = DashboardCardId.RepoRecentActivityItem("evt_123");
        var id2 = DashboardCardId.RepoRecentActivityItem("evt_123");

        id1.Should().Be(id2);
        id1.Should().NotBe(DashboardCardId.RepoRecentActivityEmpty);
        id1.Should().BeGreaterThanOrEqualTo(RepoRecentActivityItemBase);
        id1.Should().BeLessThan(RepoRecentActivityItemBase + FeedModulo);
    }

    [Test]
    public void FeedItem_ThrowsArgumentNull_WhenIdIsNull()
    {
        // Explicitly validate runtime null-guards (even though inputs are non-nullable).
        var act1 = () => DashboardCardId.NotificationsItem(null!);
        var act2 = () => DashboardCardId.MyRecentActivityItem(null!);
        var act3 = () => DashboardCardId.RepoRecentActivityItem(null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }
}
