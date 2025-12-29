using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed partial class DashboardViewModel : ObservableObject, IActivatableViewModel
{
    private const string ReposCacheOperation = "github.repos.mine";

    private readonly ILogger<DashboardViewModel> _logger;
    private readonly IDispatcher _dispatcher;
    private readonly IGitHubRepositoryService _repositoryService;
    private readonly ICacheEventBus _events;
    private readonly StatusBarViewModel _statusBar;
    private readonly IReadOnlyList<IDashboardCardProvider> _cardProviders;

    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _cardsCts;
    private IDisposable? _subscription;
    private int _loadGate;

    public DashboardContext Context { get; } = new();

    public ObservableCollection<RepositorySummary> Repositories { get; } = new();

    public ObservableCollection<DashboardCardModel> Cards { get; } = new();

    private bool _isLoadingCards;
    public bool IsLoadingCards
    {
        get => _isLoadingCards;
        set => SetProperty(ref _isLoadingCards, value);
    }

    private bool _isLoadingRepos;
    public bool IsLoadingRepos
    {
        get => _isLoadingRepos;
        set => SetProperty(ref _isLoadingRepos, value);
    }

    private int _repositoryCount;
    public int RepositoryCount
    {
        get => _repositoryCount;
        set => SetProperty(ref _repositoryCount, value);
    }

    private long? _selectedRepositoryId;
    public long? SelectedRepositoryId
    {
        get => _selectedRepositoryId;
        set
        {
            if (!SetProperty(ref _selectedRepositoryId, value))
            {
                return;
            }

            OnSelectedRepositoryIdChanged(value);
        }
    }

    public IRelayCommand<RepositorySummary?> SelectRepoCommand { get; }

    public DashboardViewModel(
        ILogger<DashboardViewModel> logger,
        IDispatcher dispatcher,
        IGitHubRepositoryService repositoryService,
        ICacheEventBus events,
        StatusBarViewModel statusBar,
        IEnumerable<IDashboardCardProvider> cardProviders)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _repositoryService = repositoryService;
        _events = events;
        _statusBar = statusBar;
        _cardProviders = (cardProviders ?? Enumerable.Empty<IDashboardCardProvider>())
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.ProviderId, StringComparer.Ordinal)
            .ToArray();

        SelectRepoCommand = new RelayCommand<RepositorySummary?>(SelectRepo);
    }

    public string Title { get; } = "Dashboard";

    public async Task ActivateAsync()
    {
        Deactivate();
        _activeCts = new CancellationTokenSource();

        Context.PropertyChanged += OnContextPropertyChanged;

        _statusBar.Set(
            message: "Dashboard",
            isBusy: false,
            isRefreshing: false,
            freshness: DataFreshnessState.Unknown,
            lastUpdatedAt: null);

        _ = RefreshCardsAsync(RefreshMode.CacheOnly, _activeCts.Token);

        await LoadReposAsync(_activeCts.Token);
    }

    public void Deactivate()
    {
        Context.PropertyChanged -= OnContextPropertyChanged;

        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;

        _cardsCts?.Cancel();
        _cardsCts?.Dispose();
        _cardsCts = null;

        _subscription?.Dispose();
        _subscription = null;

        Interlocked.Exchange(ref _loadGate, 0);
        IsLoadingRepos = false;
        IsLoadingCards = false;
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardContext.SelectedRepo))
        {
            return;
        }

        var ct = _activeCts?.Token;
        if (ct is null)
        {
            return;
        }

        _ = RefreshCardsAsync(RefreshMode.CacheOnly, ct.Value);
    }

    private void OnSelectedRepositoryIdChanged(long? value)
    {
        if (value is null)
        {
            Context.SelectedRepo = null;
            return;
        }

        var repo = Repositories.FirstOrDefault(r => r.Id == value.Value);
        if (repo is null)
        {
            return;
        }

        SelectRepoCommand.Execute(repo);
    }

    private async Task LoadReposAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _loadGate, 1) == 1)
        {
            return;
        }

        var sawCacheUpdate = false;
        _subscription?.Dispose();
        _subscription = _events.Subscribe(e =>
        {
            if (e.Kind != CacheEventKind.Updated)
            {
                return;
            }

            if (!string.Equals(e.Key.Operation, ReposCacheOperation, StringComparison.Ordinal))
            {
                return;
            }

            sawCacheUpdate = true;
            _ = _dispatcher.ExecuteAsync(async _ =>
            {
                try
                {
                    var cached = await _repositoryService.GetMyRepositoriesAsync(RefreshMode.CacheOnly, ct);
                    ObservableCollectionSync.SyncById(
                        Repositories,
                        cached,
                        getId: x => x.Id,
                        shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);

                    RepositoryCount = Repositories.Count;
                }
                catch
                {
                    // Ignore UI update failures during shutdown/cancellation.
                }
            });
        });

        await _dispatcher.ExecuteAsync(_ =>
        {
            IsLoadingRepos = true;
            return ValueTask.CompletedTask;
        });

        _statusBar.Set(
            message: "Loading repositories…",
            isBusy: true,
            isRefreshing: false,
            freshness: DataFreshnessState.Unknown,
            lastUpdatedAt: null);

        try
        {
            _logger.LogInformation("Loading repositories (RefreshMode={RefreshMode})", RefreshMode.PreferCacheThenRefresh);
            var repos = await _repositoryService.GetMyRepositoriesAsync(RefreshMode.PreferCacheThenRefresh, ct);

            await _dispatcher.ExecuteAsync(_ =>
            {
                ObservableCollectionSync.SyncById(
                    Repositories,
                    repos,
                    getId: x => x.Id,
                    shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);

                RepositoryCount = Repositories.Count;
                return ValueTask.CompletedTask;
            });

            _statusBar.Set(
                message: repos.Count == 0 ? "No repositories" : $"{repos.Count} repositories",
                isBusy: false,
                isRefreshing: false,
                freshness: sawCacheUpdate ? DataFreshnessState.Fresh : DataFreshnessState.Cached,
                lastUpdatedAt: sawCacheUpdate ? DateTimeOffset.Now : null);
        }
        catch (OperationCanceledException)
        {
            _statusBar.Set(isBusy: false, isRefreshing: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load repositories");
            _statusBar.Set(
                message: "Failed to load repositories",
                isBusy: false,
                isRefreshing: false,
                freshness: DataFreshnessState.Unknown);
        }
        finally
        {
            await _dispatcher.ExecuteAsync(_ =>
            {
                IsLoadingRepos = false;
                return ValueTask.CompletedTask;
            });

            _statusBar.Set(isBusy: false, isRefreshing: false);
            Interlocked.Exchange(ref _loadGate, 0);
        }
    }

    private void SelectRepo(RepositorySummary? repo)
    {
        if (repo is null)
        {
            return;
        }

        Context.SelectedRepo = new RepoKey(repo.OwnerLogin, repo.Name);
        SelectedRepositoryId = repo.Id;
        _statusBar.Set(message: $"Repo: {repo.OwnerLogin}/{repo.Name}");
    }

    private async Task RefreshCardsAsync(RefreshMode refresh, CancellationToken ct)
    {
        if (_cardProviders.Count == 0)
        {
            return;
        }

        CancellationTokenSource? previousCts;
        var nextCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        previousCts = Interlocked.Exchange(ref _cardsCts, nextCts);
        previousCts?.Cancel();
        previousCts?.Dispose();

        var localCt = nextCts.Token;

        await _dispatcher.ExecuteAsync(_ =>
        {
            IsLoadingCards = true;
            return ValueTask.CompletedTask;
        });

        try
        {
            var combined = new List<DashboardCardModel>();

            foreach (var provider in _cardProviders)
            {
                localCt.ThrowIfCancellationRequested();

                try
                {
                    var cards = await provider.GetCardsAsync(Context, refresh, localCt);
                    if (cards is null || cards.Count == 0)
                    {
                        continue;
                    }

                    combined.AddRange(cards
                        .OrderByDescending(c => c.Importance)
                        .ThenBy(c => c.Title, StringComparer.Ordinal));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dashboard card provider failed (ProviderId={ProviderId})", provider.ProviderId);
                }
            }

            await _dispatcher.ExecuteAsync(_ =>
            {
                ObservableCollectionSync.SyncById(
                    Cards,
                    combined,
                    getId: x => x.CardId,
                    shouldReplace: (current, next) => current != next);

                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation.
        }
        finally
        {
            await _dispatcher.ExecuteAsync(_ =>
            {
                IsLoadingCards = false;
                return ValueTask.CompletedTask;
            });
        }
    }
}
