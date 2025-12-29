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

        var query = new IssueQuery(IssueStateFilter.Open);
        var page = PageRequest.FirstPage(pageSize: 30);

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
                    ApplyIssues(cached.Items);
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
            ApplyIssues(first.Items);
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

    private void ApplyIssues(IReadOnlyList<IssueSummary> items)
    {
        SyncById(
            Issues,
            items,
            getId: x => x.Id,
            shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);
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

        // Build desired order without a full reset (avoids ListView flicker).
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

        // Remove any trailing items not present in the source.
        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static bool IsForCurrentRequest(CacheKey key, RepoKey repo, IssueQuery query, PageRequest page)
    {
        static string? Get(CacheKey k, string name)
            => k.Parameters.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.Ordinal)).Value;

        var owner = Get(key, "owner");
        var repoName = Get(key, "repo");
        var state = Get(key, "state");
        var search = Get(key, "search");
        var pageSize = Get(key, "pageSize");
        var pageNumber = Get(key, "pageNumber");
        var cursor = Get(key, "cursor");

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
