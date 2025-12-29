using System.Collections.ObjectModel;
using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Polling;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public partial class IssuesViewModel : ObservableObject
    , IActivatableViewModel
{
    private readonly IGitHubIssueService _issueService;
    private readonly IGitHubIssuePollingService _polling;
    private readonly ICacheEventBus _events;
    private readonly IDispatcher _dispatcher;
    private readonly INavigator _navigator;

    private readonly RepoRouteData _repo;

    private CancellationTokenSource? _activeCts;
    private IDisposable? _subscription;
    private string? _status;

    public IssuesViewModel(
        RepoRouteData repo,
        IGitHubIssueService issueService,
        IGitHubIssuePollingService polling,
        ICacheEventBus events,
        IDispatcher dispatcher,
        INavigator navigator)
    {
        _repo = repo;
        _issueService = issueService;
        _polling = polling;
        _events = events;
        _dispatcher = dispatcher;
        _navigator = navigator;

        Title = repo.DisplayName;
        Logout = new AsyncRelayCommand(DoLogout);
    }

    public string Title { get; }

    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ObservableCollection<IssueSummary> Issues { get; } = new();

    public ICommand Logout { get; }

    public async Task ActivateAsync()
    {
        Deactivate();

        _activeCts = new CancellationTokenSource();
        var ct = _activeCts.Token;

        Status = "Loading...";

        var query = new IssueQuery(IssueStateFilter.Open);
        var page = PageRequest.FirstPage(pageSize: 30);

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

            // Re-read from cache and update UI without flicker.
            _ = _dispatcher.ExecuteAsync(async _ =>
            {
                try
                {
                    var cached = await _issueService.GetIssuesAsync(_repo.Repo, query, page, RefreshMode.CacheOnly, ct);
                    ApplyIssues(cached.Items);
                    Status = null;
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

            _ = _polling.StartIssuesPollingAsync(
                _repo.Repo,
                query,
                page,
                new PollingRequest(TimeSpan.FromSeconds(10)),
                ct);
        }
        catch (Exception ex)
        {
            Status = $"Failed to load issues: {ex.Message}";
        }
    }

    public void Deactivate()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;

        _subscription?.Dispose();
        _subscription = null;
    }

    private void ApplyIssues(IReadOnlyList<IssueSummary> items)
    {
        // Simple in-place update to avoid visible flicker.
        if (Issues.Count == items.Count)
        {
            var same = true;
            for (var i = 0; i < items.Count; i++)
            {
                if (Issues[i].Id != items[i].Id || Issues[i].UpdatedAt != items[i].UpdatedAt)
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        Issues.Clear();
        foreach (var item in items)
        {
            Issues.Add(item);
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
