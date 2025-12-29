using System;
using System.Collections.ObjectModel;
using System.Linq;
using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace JitHubV3.Presentation;

public partial class IssueConversationViewModel : ObservableObject, IActivatableViewModel
{
    private readonly IssueConversationRouteData _data;
    private readonly IGitHubIssueConversationService _conversation;
    private readonly ICacheEventBus _events;
    private readonly IDispatcher _dispatcher;
    private readonly INavigator _navigator;
    private readonly StatusBarViewModel _statusBar;
    private readonly ILogger<IssueConversationViewModel> _logger;

    private CancellationTokenSource? _activeCts;
    private IDisposable? _subscription;

    private string? _status;
    private IssueDetail? _issue;

    public IssueConversationViewModel(
        IssueConversationRouteData data,
        IGitHubIssueConversationService conversation,
        ICacheEventBus events,
        StatusBarViewModel statusBar,
        ILogger<IssueConversationViewModel> logger,
        IDispatcher dispatcher,
        INavigator navigator)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

        Title = data.DisplayName;
        GoBack = new AsyncRelayCommand(DoGoBack);
    }

    public string Title { get; }

    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public IssueDetail? Issue
    {
        get => _issue;
        set => SetProperty(ref _issue, value);
    }

    public ObservableCollection<IssueComment> Comments { get; } = new();

    public ICommand GoBack { get; }

    public Task ActivateAsync()
    {
        Deactivate();
        _activeCts = new CancellationTokenSource();

        _logger.LogInformation(
            "Issue conversation activate: {Repo} #{Number}",
            $"{_data.Repo.Owner}/{_data.Repo.Name}",
            _data.IssueNumber);

        return LoadAsync(_activeCts.Token);
    }

    public void Deactivate()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;

        _subscription?.Dispose();
        _subscription = null;

        _statusBar.Set(isBusy: false, isRefreshing: false);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var sawCacheUpdate = false;

        _subscription?.Dispose();
        _subscription = _events.Subscribe(e =>
        {
            if (e.Kind != CacheEventKind.Updated)
            {
                return;
            }

            var isIssue = string.Equals(e.Key.Operation, "github.issue.get", StringComparison.Ordinal);
            var isComments = string.Equals(e.Key.Operation, "github.issue.comments.list", StringComparison.Ordinal);
            if (!isIssue && !isComments)
            {
                return;
            }

            if (!IsForCurrentIssue(e.Key, _data.Repo, _data.IssueNumber))
            {
                return;
            }

            _logger.LogDebug(
                "Issue conversation cache updated; refreshing UI: {Repo} #{Number} op={Op}",
                $"{_data.Repo.Owner}/{_data.Repo.Name}",
                _data.IssueNumber,
                e.Key.Operation);

            sawCacheUpdate = true;

            _ = _dispatcher.ExecuteAsync(async _ =>
            {
                try
                {
                    if (isIssue)
                    {
                        Issue = await _conversation.GetIssueAsync(_data.Repo, _data.IssueNumber, RefreshMode.CacheOnly, ct);
                    }

                    if (isComments)
                    {
                        var cachedComments = await _conversation.GetCommentsAsync(_data.Repo, _data.IssueNumber, RefreshMode.CacheOnly, ct);
                        ObservableCollectionSync.SyncById(
                            Comments,
                            cachedComments,
                            getId: x => x.Id,
                            shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);
                    }

                    Status = null;
                    _statusBar.Set(
                        message: "Conversation updated",
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
            message: $"Loading {_data.DisplayName}…",
            isBusy: true,
            isRefreshing: false,
            freshness: DataFreshnessState.Unknown,
            lastUpdatedAt: null);

        // Avoid jarring flicker: keep existing Issue/Comments when reloading.
        await _dispatcher.ExecuteAsync(_ =>
        {
            if (Issue is null)
            {
                Status = "Loading...";
            }

            return ValueTask.CompletedTask;
        });

        // Try to show cached data immediately (if available) without forcing a clear.
        try
        {
            var cachedIssue = await _conversation.GetIssueAsync(_data.Repo, _data.IssueNumber, RefreshMode.CacheOnly, ct);
            var cachedComments = await _conversation.GetCommentsAsync(_data.Repo, _data.IssueNumber, RefreshMode.CacheOnly, ct);

            await _dispatcher.ExecuteAsync(_ =>
            {
                Issue ??= cachedIssue;
                ObservableCollectionSync.SyncById(
                    Comments,
                    cachedComments,
                    getId: x => x.Id,
                    shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);

                Status = null;
                return ValueTask.CompletedTask;
            });

            _statusBar.Set(
                message: "Conversation (cached)",
                isBusy: false,
                isRefreshing: false,
                freshness: DataFreshnessState.Cached,
                lastUpdatedAt: null);
        }
        catch
        {
            // CacheOnly may throw when empty; ignore.
        }

        try
        {
            var issue = await _conversation.GetIssueAsync(_data.Repo, _data.IssueNumber, RefreshMode.PreferCacheThenRefresh, ct);
            var comments = await _conversation.GetCommentsAsync(_data.Repo, _data.IssueNumber, RefreshMode.PreferCacheThenRefresh, ct);

            await _dispatcher.ExecuteAsync(_ =>
            {
                Issue = issue;
                ObservableCollectionSync.SyncById(
                    Comments,
                    comments,
                    getId: x => x.Id,
                    shouldReplace: (current, next) => current.UpdatedAt != next.UpdatedAt || current != next);

                Status = null;
                return ValueTask.CompletedTask;
            });

            _statusBar.Set(
                message: "Conversation loaded",
                isBusy: false,
                isRefreshing: false,
                freshness: sawCacheUpdate ? DataFreshnessState.Fresh : DataFreshnessState.Cached,
                lastUpdatedAt: sawCacheUpdate ? DateTimeOffset.Now : null);

            _logger.LogInformation(
                "Issue conversation loaded: {Repo} #{Number} comments={Count} freshness={Freshness}",
                $"{_data.Repo.Owner}/{_data.Repo.Name}",
                _data.IssueNumber,
                comments.Count,
                sawCacheUpdate ? "Fresh" : "Cached");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug(
                "Issue conversation canceled: {Repo} #{Number}",
                $"{_data.Repo.Owner}/{_data.Repo.Name}",
                _data.IssueNumber);
            _statusBar.Set(isBusy: false, isRefreshing: false);
            // Ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Issue conversation failed: {Repo} #{Number}",
                $"{_data.Repo.Owner}/{_data.Repo.Name}",
                _data.IssueNumber);
            await _dispatcher.ExecuteAsync(_ =>
            {
                Status = $"Failed to load conversation: {ex.Message}";
                return ValueTask.CompletedTask;
            });

            _statusBar.Set(
                message: "Failed to load conversation",
                isBusy: false,
                isRefreshing: false,
                freshness: DataFreshnessState.Unknown);
        }
    }

    private static bool IsForCurrentIssue(CacheKey key, RepoKey repo, int issueNumber)
    {
        var owner = key.GetParameterValue("owner");
        var repoName = key.GetParameterValue("repo");
        var num = key.GetParameterValue("issueNumber");

        return string.Equals(owner, repo.Owner, StringComparison.Ordinal)
            && string.Equals(repoName, repo.Name, StringComparison.Ordinal)
            && string.Equals(num, issueNumber.ToString(), StringComparison.Ordinal);
    }

    private Task DoGoBack(CancellationToken ct)
        => _navigator.NavigateBackAsync(this, cancellation: ct);
}
