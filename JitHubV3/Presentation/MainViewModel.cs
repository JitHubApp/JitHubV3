namespace JitHubV3.Presentation;

using System.Collections.ObjectModel;
using System.Linq;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using Microsoft.Extensions.Logging;

public partial class MainViewModel : ObservableObject
    , IActivatableViewModel
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly IDispatcher _dispatcher;
    private IAuthenticationService _authentication;
    private readonly IGitHubRepositoryService _repositoryService;

    private INavigator _navigator;

    private CancellationTokenSource? _activeCts;

    private string? _repoStatus;
    private bool _isLoading;
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
        INavigator navigator)
    {
        _navigator = navigator;
        _logger = logger;
        _dispatcher = dispatcher;
        _authentication = authentication;
        _repositoryService = repositoryService;
        Title = "Main";
        Title += $" - {localizer["ApplicationName"]}";
        Title += $" - {appInfo?.Value?.Environment}";
        Logout = new AsyncRelayCommand(DoLogout);
    }
    public string? Title { get; }

    public ICommand Logout { get; }

    public async Task LoadReposAsync(CancellationToken ct)
    {
        if (IsLoading)
        {
            return;
        }

        await _dispatcher.ExecuteAsync(_ =>
        {
            IsLoading = true;
            RepoStatus = "Loading...";
            Repositories = new ObservableCollection<RepositorySummary>();
            RepositoryCount = 0;
            return ValueTask.CompletedTask;
        });

        try
        {
            _logger.LogInformation("Loading repositories (RefreshMode={RefreshMode})", RefreshMode.PreferCacheThenRefresh);
            var repos = await _repositoryService.GetMyRepositoriesAsync(RefreshMode.PreferCacheThenRefresh, ct);

            _logger.LogInformation("Repositories loaded: {Count}", repos.Count);
            await _dispatcher.ExecuteAsync(_ =>
            {
                Repositories = new ObservableCollection<RepositorySummary>(repos);
                RepositoryCount = repos.Count;

                RepoStatus = repos.Count switch
                {
                    0 => "No repos found.",
                    _ => $"Loaded {repos.Count} repos."
                };

                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            // Navigation away / re-activation. Don't treat as an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load repositories");
            await _dispatcher.ExecuteAsync(_ =>
            {
                RepoStatus = $"Failed to load repos: {ex.Message}";
                return ValueTask.CompletedTask;
            });
        }
        finally
        {
            await _dispatcher.ExecuteAsync(_ =>
            {
                IsLoading = false;
                return ValueTask.CompletedTask;
            });
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
            RepoStatus = null;
            Repositories = new ObservableCollection<RepositorySummary>();
            RepositoryCount = 0;
            await _navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.ClearBackStack);
        }
    }
}
