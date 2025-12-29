using System.Collections.ObjectModel;
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

    private CancellationTokenSource? _activeCts;
    private IDisposable? _subscription;
    private int _loadGate;

    public DashboardContext Context { get; } = new();

    public ObservableCollection<RepositorySummary> Repositories { get; } = new();

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
        StatusBarViewModel statusBar)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _repositoryService = repositoryService;
        _events = events;
        _statusBar = statusBar;

        SelectRepoCommand = new RelayCommand<RepositorySummary?>(SelectRepo);
    }

    public string Title { get; } = "Dashboard";

    public async Task ActivateAsync()
    {
        Deactivate();
        _activeCts = new CancellationTokenSource();

        _statusBar.Set(
            message: "Dashboard",
            isBusy: false,
            isRefreshing: false,
            freshness: DataFreshnessState.Unknown,
            lastUpdatedAt: null);

        await LoadReposAsync(_activeCts.Token);
    }

    public void Deactivate()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;

        _subscription?.Dispose();
        _subscription = null;

        Interlocked.Exchange(ref _loadGate, 0);
        IsLoadingRepos = false;
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
}
