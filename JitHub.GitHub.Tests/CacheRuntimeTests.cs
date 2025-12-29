using FluentAssertions;
using JitHub.Data.Caching;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class CacheRuntimeTests
{
    [Test]
    public async Task PreferCacheThenRefresh_returns_cached_and_refreshes_in_background()
    {
        var store = new InMemoryCacheStore(new InMemoryCacheStoreOptions(MaxEntries: 10));
        var bus = new CacheEventBus();
        var runtime = new CacheRuntime(store, bus);

        var key = CacheKey.Create("repos.list", userScope: "me");

        store.Set(key, 1, new CacheItemMetadata(DateTimeOffset.UtcNow));

        var updated = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = bus.Subscribe(e =>
        {
            if (e.Kind == CacheEventKind.Updated && e.Key.Equals(key) && e.Value is int v)
            {
                updated.TrySetResult(v);
            }
        });

        var fetchCalls = 0;
        Task<int> Fetch(CancellationToken ct)
        {
            Interlocked.Increment(ref fetchCalls);
            return Task.FromResult(2);
        }

        var snapshot = await runtime.GetOrRefreshAsync(key, preferCacheThenRefresh: true, Fetch, CancellationToken.None);
        snapshot.IsFromCache.Should().BeTrue();
        snapshot.Value.Should().Be(1);

        var published = await updated.Task.WaitAsync(TimeSpan.FromSeconds(2));
        published.Should().Be(2);

        store.TryGet<int>(key, out var stored, out _).Should().BeTrue();
        stored.Should().Be(2);
        fetchCalls.Should().Be(1);
    }

    [Test]
    public async Task Concurrent_refresh_deduplicates_inflight_fetch()
    {
        var store = new InMemoryCacheStore(new InMemoryCacheStoreOptions(MaxEntries: 10));
        var runtime = new CacheRuntime(store, new CacheEventBus());
        var key = CacheKey.Create("issues.list", userScope: "me", ("owner", "dotnet"), ("repo", "runtime"));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<int> Fetch(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return 123;
        }

        var t1 = runtime.RefreshAsync(key, Fetch, CancellationToken.None);
        var t2 = runtime.RefreshAsync(key, Fetch, CancellationToken.None);

        gate.SetResult();
        await Task.WhenAll(t1, t2);

        calls.Should().Be(1);
        store.TryGet<int>(key, out var stored, out _).Should().BeTrue();
        stored.Should().Be(123);
    }

    [Test]
    public async Task Polling_stops_on_cancellation()
    {
        var store = new InMemoryCacheStore(new InMemoryCacheStoreOptions(MaxEntries: 10));
        var runtime = new CacheRuntime(store, new CacheEventBus());
        var key = CacheKey.Create("repos.list", userScope: "me");

        var calls = 0;
        Task<int> Fetch(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(1);
        }

        using var cts = new CancellationTokenSource();
        var pollTask = runtime.StartPolling(key, new PollingOptions(TimeSpan.FromMilliseconds(20), JitterMax: TimeSpan.Zero), Fetch, cts.Token);

        await Task.Delay(60);
        cts.Cancel();

        await pollTask.ContinueWith(_ => { }, TaskScheduler.Default);
        calls.Should().BeGreaterThan(0);
    }
}
