using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<CacheRuntime>? _logger;

    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, Inflight> _inflight = new();

    public CacheRuntime(ICacheStore store, ICacheEventBus events, CacheRuntimeOptions? options = null, ILogger<CacheRuntime>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _options = options ?? new CacheRuntimeOptions();
        _logger = logger;

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
            _logger?.LogDebug("Cache hit: {Operation} {Key}", key.Operation, key.ToString());
            return new CacheSnapshot<T>(key, HasValue: true, value, metadata, IsFromCache: true);
        }

        _logger?.LogDebug("Cache miss: {Operation} {Key}", key.Operation, key.ToString());
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
            _logger?.LogDebug("Cache refresh (forced): {Operation} {Key}", key.Operation, key.ToString());
            return RefreshAsync<T>(key, fetchAsync, ct);
        }

        var cached = GetCached<T>(key);
        if (cached.HasValue)
        {
            _logger?.LogDebug("Cache returning cached value and starting refresh in background: {Operation} {Key}", key.Operation, key.ToString());
            _ = StartRefreshInBackground<T>(key, fetchAsync, ct);
            return Task.FromResult(cached);
        }

        _logger?.LogDebug("Cache empty; fetching: {Operation} {Key}", key.Operation, key.ToString());
        return RefreshAsync<T>(key, fetchAsync, ct);
    }

    public Task<CacheSnapshot<T>> RefreshAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> fetchAsync, CancellationToken ct)
    {
        if (fetchAsync is null)
        {
            throw new ArgumentNullException(nameof(fetchAsync));
        }

        _logger?.LogDebug("Cache refresh requested: {Operation} {Key}", key.Operation, key.ToString());
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

        _logger?.LogInformation(
            "Polling started: {Operation} interval={IntervalMs}ms jitterMax={JitterMs}ms",
            key.Operation,
            (long)options.Interval.TotalMilliseconds,
            (long)options.JitterMax.GetValueOrDefault(TimeSpan.Zero).TotalMilliseconds);

        return PollLoop();

        async Task PollLoop()
        {
            var jitterMax = options.JitterMax.GetValueOrDefault(TimeSpan.Zero);
            var rng = jitterMax > TimeSpan.Zero ? Random.Shared : null;

            using var timer = new PeriodicTimer(options.Interval);
            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    _logger?.LogDebug("Polling tick: {Operation}", key.Operation);

                    if (rng is not null)
                    {
                        var jitterMs = rng.NextDouble() * jitterMax.TotalMilliseconds;
                        if (jitterMs >= 1)
                        {
                            _logger?.LogDebug("Polling jitter: {Operation} jitterMs={JitterMs}", key.Operation, (long)jitterMs);
                            await Task.Delay(TimeSpan.FromMilliseconds(jitterMs), ct).ConfigureAwait(false);
                        }
                    }

                    await RefreshAsync<T>(key, fetchAsync, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _logger?.LogInformation("Polling stopped: {Operation}", key.Operation);
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
                _logger?.LogDebug("Inflight start: {Operation} {Key} await={Await}", key.Operation, key.ToString(), awaitResult);
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
            else
            {
                _logger?.LogDebug("Inflight join: {Operation} {Key} await={Await}", key.Operation, key.ToString(), awaitResult);
            }

            inflight.RefCount++;
        }

        var released = 0;
        void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                Release(key);
            }
        }

        var registration = callerToken.Register(ReleaseOnce);

        if (!awaitResult)
        {
            _ = inflight.Task.ContinueWith(
                _ =>
                {
                    registration.Dispose();
                    ReleaseOnce();
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
                ReleaseOnce();
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
        _logger?.LogDebug("Refresh start: {Operation} {Key}", key.Operation, key.ToString());
        await _refreshConcurrency.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var value = await fetchAsync(ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;

            DateTimeOffset? expiresAt = _options.DefaultTtl is null ? null : now.Add(_options.DefaultTtl.Value);
            var metadata = new CacheItemMetadata(now, ExpiresAtUtc: expiresAt);
            _store.Set(key, value, metadata);

            _events.Publish(new CacheEvent(CacheEventKind.Updated, key, typeof(T), value, Error: null, TimestampUtc: now));

            _logger?.LogInformation("Refresh success: {Operation} {Key}", key.Operation, key.ToString());
        }
        catch (OperationCanceledException)
        {
            // Silent; cancellation is expected for navigation/polling scopes.
            _logger?.LogDebug("Refresh canceled: {Operation} {Key}", key.Operation, key.ToString());
        }
        catch (Exception ex)
        {
            _events.Publish(new CacheEvent(CacheEventKind.RefreshFailed, key, typeof(T), Value: null, Error: ex, TimestampUtc: DateTimeOffset.UtcNow));

            _logger?.LogWarning(ex, "Refresh failed: {Operation} {Key}", key.Operation, key.ToString());
        }
        finally
        {
            _refreshConcurrency.Release();
        }
    }
}
