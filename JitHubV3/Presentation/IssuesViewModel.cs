using System.Collections.ObjectModel;
using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Polling;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace JitHubV3.Presentation;

public partial class IssuesViewModel : ObservableObject
    , IActivatableViewModel
{
    private readonly IGitHubIssueService _issueService;
    private readonly IGitHubIssuePollingService _polling;
    private readonly ICacheEventBus _events;
    private readonly IDispatcher _dispatcher;
    private readonly INavigator _navigator;
    private readonly StatusBarViewModel _statusBar;
    private readonly ILogger<IssuesViewModel> _logger;

    private readonly RepoRouteData _repo;

    private CancellationTokenSource? _activeCts;
    private IDisposable? _subscription;
    private string? _status;

    private readonly IssueQuery _query = new(IssueStateFilter.Open);
    private readonly int _pageSize = 30;
    private PageRequest _firstPage;
    private PageRequest? _nextPage;
    private readonly List<IssueSummary> _loaded = new();

    private int _loadMoreGate;

    public IssuesViewModel(
        RepoRouteData repo,
        IGitHubIssueService issueService,
        IGitHubIssuePollingService polling,
        ICacheEventBus events,
        StatusBarViewModel statusBar,
        ILogger<IssuesViewModel> logger,
        IDispatcher dispatcher,
        INavigator navigator)
    {
        _repo = repo;
        _issueService = issueService;
        _polling = polling;
        _events = events;
        _statusBar = statusBar;
        _logger = logger;
        _dispatcher = dispatcher;
        _navigator = navigator;

        Title = repo.DisplayName;
        Logout = new AsyncRelayCommand(DoLogout);
        GoBack = new AsyncRelayCommand(DoGoBack);

        _firstPage = PageRequest.FirstPage(_pageSize);
    }

    public string Title { get; }

    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ObservableCollection<IssueSummary> Issues { get; } = new();

    public ICommand Logout { get; }

    public ICommand GoBack { get; }

    public Task OpenIssueAsync(IssueSummary issue)
    {
        var display = $"{_repo.DisplayName} #{issue.Number}";
        var data = new IssueConversationRouteData(_repo.Repo, issue.Number, display);
        return _navigator.NavigateViewModelAsync<IssueConversationViewModel>(this, data: data);
    }

    public async Task ActivateAsync()
    {
        Deactivate();

        _activeCts = new CancellationTokenSource();
        var ct = _activeCts.Token;

        var sawCacheUpdate = false;

        _statusBar.Set(
            message: $"Loading issues for {_repo.DisplayName}…",
            isBusy: true,
            isRefreshing: false,
            freshness: DataFreshnessState.Unknown,
            lastUpdatedAt: null);

        Status = "Loading...";

        var query = _query;
        var page = _firstPage;

        _loaded.Clear();
        _nextPage = null;

        _logger.LogInformation("Issues activate: {Repo} state={State}", _repo.DisplayName, query.State);

        _subscription = _events.Subscribe(e =>
        {
            if (e.Kind != CacheEventKind.Updated)
            {
                return;
            }

            if (!string.Equals(e.Key.Operation, "github.issues.list", StringComparison.Ordinal))
            {
                return;
            }

            if (!IsForCurrentRequest(e.Key, _repo.Repo, query, page))
            {
                return;
            }

            _logger.LogDebug("Issues cache updated; refreshing UI: {Repo}", _repo.DisplayName);
            sawCacheUpdate = true;

            // Re-read from cache and update UI without flicker.
            _ = _dispatcher.ExecuteAsync(async _ =>
            {
                try
                {
                    var cached = await _issueService.GetIssuesAsync(_repo.Repo, query, page, RefreshMode.CacheOnly, ct);

                    // Keep already-loaded additional pages, but refresh the first page.
                    var cachedIds = cached.Items.Select(i => i.Id).ToHashSet();
                    var merged = cached.Items.Concat(_loaded.Where(i => !cachedIds.Contains(i.Id))).ToArray();
                    SetLoaded(merged, next: _nextPage);

                    Status = null;

                    _statusBar.Set(
                        message: "Issues updated",
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

        try
        {
            var first = await _issueService.GetIssuesAsync(_repo.Repo, query, page, RefreshMode.PreferCacheThenRefresh, ct);
            SetLoaded(first.Items, next: first.Next);
            Status = Issues.Count == 0 ? "No issues found." : null;

            _logger.LogInformation("Issues loaded: {Count} (polling every {Interval}s)", Issues.Count, 10);

            _statusBar.Set(
                message: Issues.Count == 0 ? "No issues" : $"{Issues.Count} issues",
                isBusy: false,
                isRefreshing: false,
                freshness: sawCacheUpdate ? DataFreshnessState.Fresh : DataFreshnessState.Cached,
                lastUpdatedAt: sawCacheUpdate ? DateTimeOffset.Now : null);

            _ = _polling.StartIssuesPollingAsync(
                _repo.Repo,
                query,
                page,
                new PollingRequest(TimeSpan.FromSeconds(10)),
                ct);
        }
        catch (OperationCanceledException)
        {
            _statusBar.Set(isBusy: false, isRefreshing: false);
        }
        catch (Exception ex)
        {
            Status = $"Failed to load issues: {ex.Message}";
            _statusBar.Set(
                message: "Failed to load issues",
                isBusy: false,
                isRefreshing: false,
                freshness: DataFreshnessState.Unknown);
        }
    }

    public Task TryLoadNextPageAsync()
    {
        if (_nextPage is not { } next)
        {
            return Task.CompletedTask;
        }

        if (_activeCts is null)
        {
            return Task.CompletedTask;
        }

        if (Interlocked.Exchange(ref _loadMoreGate, 1) == 1)
        {
            return Task.CompletedTask;
        }

        return LoadNextPageCoreAsync(next);
    }

    private async Task LoadNextPageCoreAsync(PageRequest next)
    {
        try
        {
            var ct = _activeCts?.Token ?? CancellationToken.None;
            var result = await _issueService.GetIssuesAsync(_repo.Repo, _query, next, RefreshMode.PreferCacheThenRefresh, ct);

            if (result.Items.Count > 0)
            {
                var existingIds = _loaded.Select(i => i.Id).ToHashSet();
                foreach (var item in result.Items)
                {
                    if (existingIds.Add(item.Id))
                    {
                        _loaded.Add(item);
                    }
                }

                ApplyIssues(_loaded);
            }

            _nextPage = result.Next;
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load next issues page for {Repo}", _repo.DisplayName);
        }
        finally
        {
            Interlocked.Exchange(ref _loadMoreGate, 0);
        }
    }

    private Task DoGoBack(CancellationToken token)
        => _navigator.NavigateBackAsync(this, cancellation: token);

    public void Deactivate()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;

        _subscription?.Dispose();
        _subscription = null;

        _statusBar.Set(isBusy: false, isRefreshing: false);
    }

    private void SetLoaded(IReadOnlyList<IssueSummary> items, PageRequest? next)
    {
        _loaded.Clear();
        _loaded.AddRange(items);
        _nextPage = next;
        ApplyIssues(_loaded);
    }

    private void ApplyIssues(IReadOnlyList<IssueSummary> items)
    {
        ObservableCollectionSync.SyncById(
            Issues,
            items,
            getId: x => x.Id,
            shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);
    }

    private static bool IsForCurrentRequest(CacheKey key, RepoKey repo, IssueQuery query, PageRequest page)
    {
        var owner = key.GetParameterValue("owner");
        var repoName = key.GetParameterValue("repo");
        var state = key.GetParameterValue("state");
        var search = key.GetParameterValueOrEmpty("search");
        var pageSize = key.GetParameterValue("pageSize");
        var pageNumber = key.GetParameterValueOrEmpty("pageNumber");
        var cursor = key.GetParameterValueOrEmpty("cursor");

        var expectedSearch = string.IsNullOrWhiteSpace(query.SearchText) ? string.Empty : query.SearchText.Trim();
        var expectedPageNumber = page.PageNumber?.ToString() ?? string.Empty;
        var expectedCursor = page.Cursor ?? string.Empty;

        return string.Equals(owner, repo.Owner, StringComparison.Ordinal)
            && string.Equals(repoName, repo.Name, StringComparison.Ordinal)
            && string.Equals(state, query.State.ToString(), StringComparison.Ordinal)
            && string.Equals(search, expectedSearch, StringComparison.Ordinal)
            && string.Equals(pageSize, page.PageSize.ToString(), StringComparison.Ordinal)
            && string.Equals(pageNumber, expectedPageNumber, StringComparison.Ordinal)
            && string.Equals(cursor, expectedCursor, StringComparison.Ordinal);
    }

    private async Task DoLogout(CancellationToken token)
    {
        // Minimal: rely on auth service owned by MainViewModel flow.
        await _navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.ClearBackStack);
    }
}
