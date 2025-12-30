using System.Collections.Concurrent;
using FluentAssertions;
using JitHub.Data.Caching;

namespace JitHub.GitHub.Tests;

public sealed class StagedWorkSchedulerTests
{
    [Test]
    public async Task RunAsync_FiltersAndOrdersByTierThenOriginalOrder_WhenConcurrencyIsOne()
    {
        var items = new[]
        {
            (id: 0, tier: 1),
            (id: 1, tier: 0),
            (id: 2, tier: 2),
            (id: 3, tier: 1),
        };

        var executed = new List<int>();

        await StagedWorkScheduler.RunAsync(
            items,
            tierSelector: x => x.tier,
            maxTierInclusive: 1,
            maxConcurrency: 1,
            workAsync: (x, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                executed.Add(x.id);
                return Task.CompletedTask;
            },
            ct: CancellationToken.None);

        executed.Should().Equal(1, 0, 3);
    }

    [Test]
    public async Task RunAsync_StopsWorkOnCancellation()
    {
        var items = new[]
        {
            (id: 0, tier: 0),
            (id: 1, tier: 0),
            (id: 2, tier: 0),
        };

        using var cts = new CancellationTokenSource();
        var executed = new ConcurrentQueue<int>();

        Func<(int id, int tier), CancellationToken, Task> work = async (x, ct) =>
        {
            executed.Enqueue(x.id);

            // Cancel while holding the semaphore so queued work doesn't start.
            cts.Cancel();

            // Observe cancellation inside the work item.
            await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
        };

        var act = () => StagedWorkScheduler.RunAsync(
            items,
            tierSelector: x => x.tier,
            maxTierInclusive: 0,
            maxConcurrency: 1,
            workAsync: work,
            ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        executed.Should().Equal(0);
    }
}
