using System.Collections.Concurrent;

namespace JitHub.Data.Caching;

public sealed class CacheRuntime
{
    private sealed class Inflight
    {
        public Inflight(Task task, CancellationTokenSource cts)
        {
            Task = task;
            Cts = cts;
        }

        public Task Task { get; }
        public CancellationTokenSource Cts { get; }
        public int RefCount;
    }

    private readonly ICacheStore _store;
    private readonly ICacheEventBus _events;
    private readonly CacheRuntimeOptions _options;
    private readonly SemaphoreSlim _refreshConcurrency;

    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, Inflight> _inflight = new();

    public CacheRuntime(ICacheStore store, ICacheEventBus events, CacheRuntimeOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _options = options ?? new CacheRuntimeOptions();

        if (_options.MaxConcurrentRefreshes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.MaxConcurrentRefreshes, "MaxConcurrentRefreshes must be greater than 0.");
        }

        _refreshConcurrency = new SemaphoreSlim(_options.MaxConcurrentRefreshes);
    }

    public CacheSnapshot<T> GetCached<T>(CacheKey key)
    {
        if (_store.TryGet<T>(key, out var value, out var metadata))
        {
            return new CacheSnapshot<T>(key, HasValue: true, value, metadata, IsFromCache: true);
        }

        return new CacheSnapshot<T>(key, HasValue: false, Value: default, Metadata: null, IsFromCache: true);
    }

    public Task<CacheSnapshot<T>> GetOrRefreshAsync<T>(
        CacheKey key,
        bool preferCacheThenRefresh,
        Func<CancellationToken, Task<T>> fetchAsync,
        CancellationToken ct)
    {
        if (fetchAsync is null)
        {
            throw new ArgumentNullException(nameof(fetchAsync));
        }

        if (!preferCacheThenRefresh)
        {
            return RefreshAsync<T>(key, fetchAsync, ct);
        }

        var cached = GetCached<T>(key);
        if (cached.HasValue)
        {
            _ = StartRefreshInBackground<T>(key, fetchAsync, ct);
            return Task.FromResult(cached);
        }

        return RefreshAsync<T>(key, fetchAsync, ct);
    }

    public Task<CacheSnapshot<T>> RefreshAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> fetchAsync, CancellationToken ct)
    {
        if (fetchAsync is null)
        {
            throw new ArgumentNullException(nameof(fetchAsync));
        }

        return JoinOrStartInflight<T>(key, fetchAsync, ct, awaitResult: true);
    }

    public Task StartPolling<T>(
        CacheKey key,
        PollingOptions options,
        Func<CancellationToken, Task<T>> fetchAsync,
        CancellationToken ct)
    {
        if (options.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Interval, "Interval must be greater than 0.");
        }

        if (fetchAsync is null)
        {
            throw new ArgumentNullException(nameof(fetchAsync));
        }

        return PollLoop();

        async Task PollLoop()
        {
            var jitterMax = options.JitterMax.GetValueOrDefault(TimeSpan.Zero);
            var rng = jitterMax > TimeSpan.Zero ? Random.Shared : null;

            using var timer = new PeriodicTimer(options.Interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (rng is not null)
                {
                    var jitterMs = rng.NextDouble() * jitterMax.TotalMilliseconds;
                    if (jitterMs >= 1)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(jitterMs), ct).ConfigureAwait(false);
                    }
                }

                await RefreshAsync<T>(key, fetchAsync, ct).ConfigureAwait(false);
            }
        }
    }

    private Task StartRefreshInBackground<T>(CacheKey key, Func<CancellationToken, Task<T>> fetchAsync, CancellationToken ct)
    {
        return JoinOrStartInflight<T>(key, fetchAsync, ct, awaitResult: false);
    }

    private Task<CacheSnapshot<T>> JoinOrStartInflight<T>(
        CacheKey key,
        Func<CancellationToken, Task<T>> fetchAsync,
        CancellationToken callerToken,
        bool awaitResult)
    {
        Inflight inflight;
        lock (_gate)
        {
            if (!_inflight.TryGetValue(key, out inflight!))
            {
                var cts = new CancellationTokenSource();
                var task = RunRefresh<T>(key, fetchAsync, cts.Token);

                inflight = new Inflight(task, cts);
                _inflight.Add(key, inflight);

                _ = task.ContinueWith(
                    _ =>
                    {
                        lock (_gate)
                        {
                            _inflight.Remove(key);
                        }
                        cts.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            inflight.RefCount++;
        }

        var registration = callerToken.Register(() => Release(key));

        if (!awaitResult)
        {
            _ = inflight.Task.ContinueWith(
                _ =>
                {
                    registration.Dispose();
                    Release(key);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return Task.FromResult(GetCached<T>(key));
        }

        return AwaitAndRelease();

        async Task<CacheSnapshot<T>> AwaitAndRelease()
        {
            try
            {
                await inflight.Task.ConfigureAwait(false);
                return GetCached<T>(key);
            }
            finally
            {
                registration.Dispose();
                Release(key);
            }
        }
    }

    private void Release(CacheKey key)
    {
        lock (_gate)
        {
            if (!_inflight.TryGetValue(key, out var inflight))
            {
                return;
            }

            inflight.RefCount--;
            if (inflight.RefCount <= 0)
            {
                inflight.Cts.Cancel();
            }
        }
    }

    private async Task RunRefresh<T>(CacheKey key, Func<CancellationToken, Task<T>> fetchAsync, CancellationToken ct)
    {
        await _refreshConcurrency.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var value = await fetchAsync(ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;

            DateTimeOffset? expiresAt = _options.DefaultTtl is null ? null : now.Add(_options.DefaultTtl.Value);
            var metadata = new CacheItemMetadata(now, ExpiresAtUtc: expiresAt);
            _store.Set(key, value, metadata);

            _events.Publish(new CacheEvent(CacheEventKind.Updated, key, typeof(T), value, Error: null, TimestampUtc: now));
        }
        catch (OperationCanceledException)
        {
            // Silent; cancellation is expected for navigation/polling scopes.
        }
        catch (Exception ex)
        {
            _events.Publish(new CacheEvent(CacheEventKind.RefreshFailed, key, typeof(T), Value: null, Error: ex, TimestampUtc: DateTimeOffset.UtcNow));
        }
        finally
        {
            _refreshConcurrency.Release();
        }
    }
}
