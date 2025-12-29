using FluentAssertions;
using JitHub.Data.Caching;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class CacheRuntimeHardeningTests
{
    [Test]
    public async Task Canceling_one_waiter_does_not_cancel_shared_inflight_refresh()
    {
        var events = new CacheEventBus();
        var cache = new CacheRuntime(new InMemoryCacheStore(), events);
        var key = CacheKey.Create("op.shared");

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var callCount = 0;

        async Task<int> FetchAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref callCount);
            started.TrySetResult();
            await allowComplete.Task.WaitAsync(ct);
            return 123;
        }

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var t1 = cache.RefreshAsync(key, FetchAsync, cts1.Token);
        var t2 = cache.RefreshAsync(key, FetchAsync, cts2.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // One waiter cancels while another remains active.
        cts1.Cancel();

        // Allow the shared fetch to finish.
        allowComplete.TrySetResult();

        var r2 = await t2;
        r2.HasValue.Should().BeTrue();

        var cached = cache.GetCached<int>(key);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be(123);

        callCount.Should().Be(1);

        // Ensure the canceled waiter does not hang.
        await t1.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
