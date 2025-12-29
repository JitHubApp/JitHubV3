using FluentAssertions;
using JitHub.Data.Caching;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class CacheEventBusHardeningTests
{
    [Test]
    public void Publish_isolates_subscriber_exceptions()
    {
        var bus = new CacheEventBus();

        var called = 0;
        using var sub1 = bus.Subscribe(_ => throw new InvalidOperationException("boom"));
        using var sub2 = bus.Subscribe(_ => called++);

        var evt = new CacheEvent(
            Kind: CacheEventKind.Updated,
            Key: CacheKey.Create("op"),
            ValueType: typeof(string),
            Value: "v",
            Error: null,
            TimestampUtc: DateTimeOffset.UtcNow);

        bus.Invoking(b => b.Publish(evt)).Should().NotThrow();
        called.Should().Be(1);
    }
}
