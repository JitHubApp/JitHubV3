using FluentAssertions;
using JitHubV3.Presentation;
using NUnit.Framework;

namespace JitHubV3.Tests.Presentation.Infrastructure;

public class StatusBarComposerTests
{
    [Test]
    public void CoreExtension_ComposesFreshnessAndLastUpdatedSegments()
    {
        var statusBar = new StatusBarViewModel();
        var extension = new CoreStatusBarExtension(statusBar);
        _ = new StatusBarComposer(new[] { extension }, statusBar);

        statusBar.Set(
            freshness: DataFreshnessState.Fresh,
            lastUpdatedAt: new DateTimeOffset(2026, 1, 1, 1, 2, 3, TimeSpan.Zero));

        statusBar.Segments.Should().HaveCount(2);
        statusBar.Segments[0].Text.Should().Be("Fresh");
        statusBar.Segments[1].Text.Should().Be("Updated 01:02:03");
    }

    [Test]
    public void Composer_OrdersByPriorityDescendingThenId()
    {
        var statusBar = new StatusBarViewModel();

        var extension = new TestExtension(new StatusBarSegment[]
        {
            new StatusBarSegment("b", "B", IsVisible: true, Priority: 10),
            new StatusBarSegment("a", "A", IsVisible: true, Priority: 10),
            new StatusBarSegment("z", "Z", IsVisible: true, Priority: 100),
        });

        _ = new StatusBarComposer(new[] { extension }, statusBar);

        statusBar.Segments.Should().HaveCount(3);
        statusBar.Segments[0].Id.Should().Be("z");
        statusBar.Segments[1].Id.Should().Be("a");
        statusBar.Segments[2].Id.Should().Be("b");
    }

    private sealed class TestExtension : IStatusBarExtension
    {
        public TestExtension(IReadOnlyList<StatusBarSegment> segments) => Segments = segments;

        public event EventHandler? Changed;

        public IReadOnlyList<StatusBarSegment> Segments { get; }

        // ReSharper disable once UnusedMember.Local
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
