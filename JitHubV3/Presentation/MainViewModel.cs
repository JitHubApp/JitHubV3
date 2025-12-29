namespace JitHubV3.Presentation;

using System.Collections.ObjectModel;
using System.Linq;
using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Threading;

public partial class MainViewModel : ObservableObject
    , IActivatableViewModel
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly IDispatcher _dispatcher;
    private IAuthenticationService _authentication;
    private readonly IGitHubRepositoryService _repositoryService;
    private readonly ICacheEventBus _events;
    private readonly StatusBarViewModel _statusBar;

    private INavigator _navigator;

    private CancellationTokenSource? _activeCts;
    private IDisposable? _subscription;

    private string? _repoStatus;
    private bool _isLoading;
    private int _loadGate;
    private int _repositoryCount;
    private ObservableCollection<RepositorySummary> _repositories = new();

    public string? RepoStatus
    {
        get => _repoStatus;
        set => SetProperty(ref _repoStatus, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public int RepositoryCount
    {
        get => _repositoryCount;
        set => SetProperty(ref _repositoryCount, value);
    }

    public ObservableCollection<RepositorySummary> Repositories
    {
        get => _repositories;
        set => SetProperty(ref _repositories, value);
    }

    public MainViewModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        ILogger<MainViewModel> logger,
        IDispatcher dispatcher,
        IAuthenticationService authentication,
        IGitHubRepositoryService repositoryService,
        ICacheEventBus events,
        StatusBarViewModel statusBar,
        INavigator navigator)
    {
        _navigator = navigator;
        _logger = logger;
        _dispatcher = dispatcher;
        _authentication = authentication;
        _repositoryService = repositoryService;
        _events = events;
        _statusBar = statusBar;
        Title = "Main";
        Title += $" - {localizer["ApplicationName"]}";
        Title += $" - {appInfo?.Value?.Environment}";
        Logout = new AsyncRelayCommand(DoLogout);
    }
    public string? Title { get; }

    public ICommand Logout { get; }

    public async Task LoadReposAsync(CancellationToken ct)
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

            if (!string.Equals(e.Key.Operation, "github.repos.mine", StringComparison.Ordinal))
            {
                return;
            }

            sawCacheUpdate = true;
            _ = _dispatcher.ExecuteAsync(async _ =>
            {
                try
                {
                    var cached = await _repositoryService.GetMyRepositoriesAsync(RefreshMode.CacheOnly, ct);
                    SyncById(
                        Repositories,
                        cached,
                        getId: x => x.Id,
                        shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);

                    RepositoryCount = Repositories.Count;

                    RepoStatus = cached.Count switch
                    {
                        0 => "No repos found.",
                        _ => $"Loaded {cached.Count} repos."
                    };

                    _statusBar.Set(
                        message: "Repositories updated",
                        isBusy: false,
                        isRefreshing: false,
                        freshness: DataFreshnessState.Fresh,
                        lastUpdatedAt: DateTimeOffset.Now);
                }
                catch
                {
                    // Ignore UI update failures during shutdown/cancellation.
                }
            });
        });

        _statusBar.Set(
            message: "Loading repositories…",
            isBusy: true,
            isRefreshing: false,
            freshness: DataFreshnessState.Unknown,
            lastUpdatedAt: null);

        await _dispatcher.ExecuteAsync(_ =>
        {
            IsLoading = true;
            RepoStatus = "Loading...";
            return ValueTask.CompletedTask;
        });

        try
        {
            _logger.LogInformation("Loading repositories (RefreshMode={RefreshMode})", RefreshMode.PreferCacheThenRefresh);
            var repos = await _repositoryService.GetMyRepositoriesAsync(RefreshMode.PreferCacheThenRefresh, ct);

            _logger.LogInformation("Repositories loaded: {Count}", repos.Count);
            await _dispatcher.ExecuteAsync(_ =>
            {
                SyncById(
                    Repositories,
                    repos,
                    getId: x => x.Id,
                    shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);

                RepositoryCount = Repositories.Count;

                RepoStatus = repos.Count switch
                {
                    0 => "No repos found.",
                    _ => $"Loaded {repos.Count} repos."
                };

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
            await _dispatcher.ExecuteAsync(_ =>
            {
                RepoStatus = $"Failed to load repos: {ex.Message}";
                return ValueTask.CompletedTask;
            });

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
                IsLoading = false;
                return ValueTask.CompletedTask;
            });

            _statusBar.Set(isBusy: false, isRefreshing: false);
            Interlocked.Exchange(ref _loadGate, 0);
        }
    }

    public Task ActivateAsync()
    {
        Deactivate();
        _activeCts = new CancellationTokenSource();
        return LoadReposAsync(_activeCts.Token);
    }

    public void Deactivate()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;

        _subscription?.Dispose();
        _subscription = null;

        Interlocked.Exchange(ref _loadGate, 0);
    }

    private static void SyncById<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> source,
        Func<T, long> getId,
        Func<T, T, bool> shouldReplace)
    {
        if (source.Count == 0)
        {
            if (target.Count != 0)
            {
                target.Clear();
            }

            return;
        }

        for (var desiredIndex = 0; desiredIndex < source.Count; desiredIndex++)
        {
            var desiredItem = source[desiredIndex];
            var desiredId = getId(desiredItem);

            var currentIndex = -1;
            for (var i = desiredIndex; i < target.Count; i++)
            {
                if (getId(target[i]) == desiredId)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                target.Insert(desiredIndex, desiredItem);
                continue;
            }

            if (currentIndex != desiredIndex)
            {
                target.Move(currentIndex, desiredIndex);
            }

            if (shouldReplace(target[desiredIndex], desiredItem))
            {
                target[desiredIndex] = desiredItem;
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    public Task OpenRepoAsync(RepositorySummary repo)
    {
        var key = new RepoKey(repo.OwnerLogin, repo.Name);
        var data = new RepoRouteData(key, DisplayName: $"{repo.OwnerLogin}/{repo.Name}");
        return _navigator.NavigateViewModelAsync<IssuesViewModel>(this, data: data);
    }

    public async Task DoLogout(CancellationToken token)
    {
        try
        {
            await _authentication.LogoutAsync(token);
        }
        finally
        {
            Deactivate();
            _statusBar.Clear();
            RepoStatus = null;
            Repositories = new ObservableCollection<RepositorySummary>();
            RepositoryCount = 0;
            await _navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.ClearBackStack);
        }
    }
}
