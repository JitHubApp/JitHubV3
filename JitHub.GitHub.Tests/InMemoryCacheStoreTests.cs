using FluentAssertions;
using JitHub.Data.Caching;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class InMemoryCacheStoreTests
{
    [Test]
    public void Evicts_least_recently_used_entries()
    {
        var store = new InMemoryCacheStore(new InMemoryCacheStoreOptions(MaxEntries: 2));
        var meta = new CacheItemMetadata(DateTimeOffset.UtcNow);

        var k1 = CacheKey.Create("op", userScope: null, ("k", "1"));
        var k2 = CacheKey.Create("op", userScope: null, ("k", "2"));
        var k3 = CacheKey.Create("op", userScope: null, ("k", "3"));

        store.Set(k1, 1, meta);
        store.Set(k2, 2, meta);

        store.TryGet<int>(k1, out _, out _).Should().BeTrue();

        store.Set(k3, 3, meta);

        store.TryGet<int>(k2, out _, out _).Should().BeFalse();
        store.TryGet<int>(k1, out var v1, out _).Should().BeTrue();
        v1.Should().Be(1);
        store.TryGet<int>(k3, out var v3, out _).Should().BeTrue();
        v3.Should().Be(3);
    }
}
