namespace JitHub.Data.Caching;

public static class StagedWorkScheduler
{
    public static async Task RunAsync<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, int> tierSelector,
        int maxTierInclusive,
        int maxConcurrency,
        Func<TItem, CancellationToken, Task> workAsync,
        CancellationToken ct)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (tierSelector is null)
        {
            throw new ArgumentNullException(nameof(tierSelector));
        }

        if (workAsync is null)
        {
            throw new ArgumentNullException(nameof(workAsync));
        }

        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        var planned = items
            .Select((item, index) => (item, index, tier: tierSelector(item)))
            .Where(x => x.tier <= maxTierInclusive)
            .OrderBy(x => x.tier)
            .ThenBy(x => x.index)
            .ToArray();

        if (planned.Length == 0)
        {
            return;
        }

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task>(capacity: planned.Length);

        foreach (var (item, _, _) in planned)
        {
            ct.ThrowIfCancellationRequested();

            tasks.Add(RunOneAsync(item, semaphore, workAsync, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task RunOneAsync<TItem>(
        TItem item,
        SemaphoreSlim semaphore,
        Func<TItem, CancellationToken, Task> workAsync,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await workAsync(item, ct).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
