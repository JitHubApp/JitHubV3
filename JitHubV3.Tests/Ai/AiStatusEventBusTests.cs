using FluentAssertions;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class AiStatusEventBusTests
{
    [Test]
    public void Publish_is_resilient_to_subscriber_exceptions()
    {
        var bus = new AiStatusEventBus();

        var received = new List<AiStatusEvent>();

        using var _ = bus.Subscribe(_ => throw new InvalidOperationException("boom"));
        using var __ = bus.Subscribe(e => received.Add(e));

        bus.Publish(new AiEnablementChanged(IsEnabled: true));

        received.Should().ContainSingle()
            .Which.Should().BeOfType<AiEnablementChanged>();
    }

    [Test]
    public void Subscribe_returns_disposable_that_unsubscribes()
    {
        var bus = new AiStatusEventBus();

        var calls = 0;
        var sub = bus.Subscribe(_ => calls++);

        bus.Publish(new AiEnablementChanged(IsEnabled: true));
        calls.Should().Be(1);

        sub.Dispose();

        bus.Publish(new AiEnablementChanged(IsEnabled: false));
        calls.Should().Be(1);
    }
}
