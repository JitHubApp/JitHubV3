using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation;

public sealed partial class DashboardViewModel : ObservableObject, IActivatableViewModel
{
    private const string ReposCacheOperation = "github.repos.mine";
    private const int CardProviderMaxConcurrency = 4;

    private readonly ILogger<DashboardViewModel> _logger;
    private readonly IDispatcher _dispatcher;
    private readonly IGitHubRepositoryService _repositoryService;
    private readonly ComposeSearch.IComposeSearchOrchestrator _composeSearch;
    private readonly ComposeSearch.IComposeSearchStateStore _composeState;
    private readonly ICacheEventBus _events;
    private readonly StatusBarViewModel _statusBar;
    private readonly IReadOnlyList<IDashboardCardProvider> _cardProviders;

    private readonly IAiModelPickerOptionsProvider _aiModelOptions;
    private readonly IAiModelStore _aiModelStore;
    private readonly IAiEnablementStore _aiEnablementStore;
    private readonly IAiModelDownloadQueue _aiModelDownloads;

    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _cardsCts;
    private CancellationTokenSource? _composeCts;
    private IDisposable? _subscription;
    private int _loadGate;

    public DashboardContext Context { get; } = new();

    public ObservableCollection<RepositorySummary> Repositories { get; } = new();

    public ObservableCollection<DashboardCardModel> Cards { get; } = new();

    public ObservableCollection<AiModelPickerOption> AiModelOptions { get; } = new();

    private bool _isAiEnabled = true;
    public bool IsAiEnabled
    {
        get => _isAiEnabled;
        set
        {
            if (!SetProperty(ref _isAiEnabled, value))
            {
                return;
            }

            _ = PersistAiEnablementAsync(value);
        }
    }

    private bool _isAiModelPickerOpen;
    public bool IsAiModelPickerOpen
    {
        get => _isAiModelPickerOpen;
        set => SetProperty(ref _isAiModelPickerOpen, value);
    }

    private AiModelPickerOption? _selectedAiModel;
    public AiModelPickerOption? SelectedAiModel
    {
        get => _selectedAiModel;
        set
        {
            if (!SetProperty(ref _selectedAiModel, value))
            {
                return;
            }

            _ = PersistAiSelectionAsync(value);

            DownloadSelectedAiModelCommand.NotifyCanExecuteChanged();
            CancelAiModelDownloadCommand.NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(CanDownloadSelectedAiModel));
            OnPropertyChanged(nameof(CanCancelAiModelDownload));
        }
    }

    public bool CanDownloadSelectedAiModel
        => SelectedAiModel is { IsLocal: true, IsDownloaded: false }
           && SelectedAiModel.DownloadUri is not null
           && !IsSubmittingCompose;

    public bool CanCancelAiModelDownload
        => _activeAiDownload is not null
           && !_activeAiDownload.Task.IsCompleted;

    private bool _isAiModelDownloadVisible;
    public bool IsAiModelDownloadVisible
    {
        get => _isAiModelDownloadVisible;
        set => SetProperty(ref _isAiModelDownloadVisible, value);
    }

    private bool _isAiModelDownloadIndeterminate;
    public bool IsAiModelDownloadIndeterminate
    {
        get => _isAiModelDownloadIndeterminate;
        set => SetProperty(ref _isAiModelDownloadIndeterminate, value);
    }

    private double _aiModelDownloadProgressPercent;
    public double AiModelDownloadProgressPercent
    {
        get => _aiModelDownloadProgressPercent;
        set => SetProperty(ref _aiModelDownloadProgressPercent, value);
    }

    private string? _aiModelDownloadStatusText;
    public string? AiModelDownloadStatusText
    {
        get => _aiModelDownloadStatusText;
        set => SetProperty(ref _aiModelDownloadStatusText, value);
    }

    private AiModelDownloadHandle? _activeAiDownload;
    private IDisposable? _activeAiDownloadSubscription;

    public IAsyncRelayCommand DownloadSelectedAiModelCommand { get; }
    public IRelayCommand CancelAiModelDownloadCommand { get; }

    public IRelayCommand OpenAiModelPickerCommand { get; }
    public IRelayCommand CloseAiModelPickerCommand { get; }

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

    private string? _composeText;
    public string? ComposeText
    {
        get => _composeText;
        set
        {
            if (!SetProperty(ref _composeText, value))
            {
                return;
            }

            SubmitComposeCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _isSubmittingCompose;
    public bool IsSubmittingCompose
    {
        get => _isSubmittingCompose;
        set
        {
            if (!SetProperty(ref _isSubmittingCompose, value))
            {
                return;
            }

            SubmitComposeCommand.NotifyCanExecuteChanged();
        }
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

    public IAsyncRelayCommand SubmitComposeCommand { get; }

    public DashboardViewModel(
        ILogger<DashboardViewModel> logger,
        IDispatcher dispatcher,
        IGitHubRepositoryService repositoryService,
        ComposeSearch.IComposeSearchOrchestrator composeSearch,
        ComposeSearch.IComposeSearchStateStore composeState,
        ICacheEventBus events,
        StatusBarViewModel statusBar,
        IEnumerable<IDashboardCardProvider> cardProviders,
        IAiModelPickerOptionsProvider aiModelOptions,
        IAiModelStore aiModelStore,
        IAiEnablementStore aiEnablementStore,
        IAiModelDownloadQueue aiModelDownloads)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _repositoryService = repositoryService;
        _composeSearch = composeSearch;
        _composeState = composeState;
        _events = events;
        _statusBar = statusBar;
        _cardProviders = (cardProviders ?? Enumerable.Empty<IDashboardCardProvider>())
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.ProviderId, StringComparer.Ordinal)
            .ToArray();

        _aiModelOptions = aiModelOptions;
        _aiModelStore = aiModelStore;
        _aiEnablementStore = aiEnablementStore;
        _aiModelDownloads = aiModelDownloads;

        SelectRepoCommand = new RelayCommand<RepositorySummary?>(SelectRepo);
        SubmitComposeCommand = new AsyncRelayCommand(SubmitComposeAsync, CanSubmitCompose);

        DownloadSelectedAiModelCommand = new AsyncRelayCommand(DownloadSelectedAiModelAsync, () => CanDownloadSelectedAiModel);
        CancelAiModelDownloadCommand = new RelayCommand(CancelAiModelDownload, () => CanCancelAiModelDownload);

        OpenAiModelPickerCommand = new RelayCommand(() => IsAiModelPickerOpen = true);
        CloseAiModelPickerCommand = new RelayCommand(() => IsAiModelPickerOpen = false);
    }

    public string Title { get; } = "Dashboard";

    public async Task ActivateAsync()
    {
        Deactivate();

                DownloadSelectedAiModelCommand.NotifyCanExecuteChanged();
                CancelAiModelDownloadCommand.NotifyCanExecuteChanged();

                OnPropertyChanged(nameof(CanDownloadSelectedAiModel));
                OnPropertyChanged(nameof(CanCancelAiModelDownload));
        _activeCts = new CancellationTokenSource();

        Context.PropertyChanged += OnContextPropertyChanged;

        _statusBar.Set(
            message: "Dashboard",
            isBusy: false,
            isRefreshing: false,
            freshness: DataFreshnessState.Unknown,
            lastUpdatedAt: null);

        _ = RefreshCardsAsync(RefreshMode.CacheOnly, _activeCts.Token);

        _ = LoadAiEnablementAsync(_activeCts.Token);
        _ = LoadAiModelOptionsAsync(_activeCts.Token);

        await LoadReposAsync(_activeCts.Token);
    }

    private async Task LoadAiEnablementAsync(CancellationToken ct)
    {
        try
        {
            var enabled = await _aiEnablementStore.GetIsEnabledAsync(ct).ConfigureAwait(false);

            await _dispatcher.ExecuteAsync(() =>
            {
                _isAiEnabled = enabled;
                OnPropertyChanged(nameof(IsAiEnabled));
            });
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            // ignore
        }
    }

    private async Task PersistAiEnablementAsync(bool isEnabled)
    {
        try
        {
            await _aiEnablementStore.SetIsEnabledAsync(isEnabled, CancellationToken.None);
        }
        catch
        {
            // ignore
        }
    }

    private async Task LoadAiModelOptionsAsync(CancellationToken ct)
    {
        try
        {
            var options = await _aiModelOptions.GetOptionsAsync(ct).ConfigureAwait(false);
            var selection = await _aiModelStore.GetSelectionAsync(ct).ConfigureAwait(false);

            await _dispatcher.ExecuteAsync(() =>
            {
                AiModelOptions.Clear();
                foreach (var opt in options)
                {
                    AiModelOptions.Add(opt);
                }

                if (selection is not null)
                {
                    SelectedAiModel = options.FirstOrDefault(o =>
                        string.Equals(o.RuntimeId, selection.RuntimeId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(o.ModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase));
                }

                SelectedAiModel ??= AiModelOptions.FirstOrDefault();
            });
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI model picker options");
        }
    }

    private async Task PersistAiSelectionAsync(AiModelPickerOption? option)
    {
        if (option is null)
        {
            return;
        }

        try
        {
            // Persist selection; runtimes/orchestrator resolve from store.
            await _aiModelStore.SetSelectionAsync(new AiModelSelection(option.RuntimeId, option.ModelId), CancellationToken.None);
        }
        catch
        {
            // ignore
        }
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

        _composeCts?.Cancel();
        _composeCts?.Dispose();
        _composeCts = null;

        _subscription?.Dispose();
        _subscription = null;

        _activeAiDownloadSubscription?.Dispose();
        _activeAiDownloadSubscription = null;
        _activeAiDownload = null;
        IsAiModelDownloadVisible = false;
        AiModelDownloadStatusText = null;
        AiModelDownloadProgressPercent = 0;
        IsAiModelDownloadIndeterminate = false;

        Interlocked.Exchange(ref _loadGate, 0);
        IsLoadingRepos = false;
        IsLoadingCards = false;
        IsSubmittingCompose = false;
    }

    private async Task DownloadSelectedAiModelAsync()
    {
        var opt = SelectedAiModel;
        if (opt is null || !opt.IsLocal || opt.IsDownloaded || opt.DownloadUri is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(opt.InstallPath))
        {
            return;
        }

        _activeAiDownloadSubscription?.Dispose();
        _activeAiDownloadSubscription = null;
        _activeAiDownload = null;

        IsAiModelDownloadVisible = true;
        AiModelDownloadStatusText = "Downloading…";
        AiModelDownloadProgressPercent = 0;
        IsAiModelDownloadIndeterminate = true;

        OnPropertyChanged(nameof(CanDownloadSelectedAiModel));
        OnPropertyChanged(nameof(CanCancelAiModelDownload));

        var handle = _aiModelDownloads.Enqueue(new AiModelDownloadRequest(
            ModelId: opt.ModelId,
            RuntimeId: opt.RuntimeId,
            SourceUri: opt.DownloadUri,
            InstallPath: opt.InstallPath!,
            ArtifactFileName: opt.ArtifactFileName,
            ExpectedBytes: opt.ExpectedBytes,
            ExpectedSha256: opt.ExpectedSha256));

        _activeAiDownload = handle;
        CancelAiModelDownloadCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCancelAiModelDownload));

        _activeAiDownloadSubscription = handle.Subscribe(p =>
        {
            _ = _dispatcher.ExecuteAsync(() =>
            {
                IsAiModelDownloadVisible = true;
                IsAiModelDownloadIndeterminate = p.Progress is null;
                AiModelDownloadProgressPercent = p.Progress is null ? 0 : Math.Round(p.Progress.Value * 100, 1);
                AiModelDownloadStatusText = p.Status switch
                {
                    AiModelDownloadStatus.Queued => "Queued…",
                    AiModelDownloadStatus.Downloading => "Downloading…",
                    AiModelDownloadStatus.Completed => "Downloaded",
                    AiModelDownloadStatus.Canceled => "Canceled",
                    AiModelDownloadStatus.Failed => "Download failed",
                    _ => "",
                };

                CancelAiModelDownloadCommand.NotifyCanExecuteChanged();
                DownloadSelectedAiModelCommand.NotifyCanExecuteChanged();

                OnPropertyChanged(nameof(CanDownloadSelectedAiModel));
                OnPropertyChanged(nameof(CanCancelAiModelDownload));
            });
        });

        try
        {
            await handle.Task.ConfigureAwait(false);

            // Refresh options so the model flips to "downloaded".
            var ct = _activeCts?.Token ?? CancellationToken.None;
            _ = LoadAiModelOptionsAsync(ct);
        }
        catch
        {
            // ignore
        }
    }

    private void CancelAiModelDownload()
    {
        if (_activeAiDownload is null)
        {
            return;
        }

        _aiModelDownloads.Cancel(_activeAiDownload.Id);

        OnPropertyChanged(nameof(CanCancelAiModelDownload));
    }

    private bool CanSubmitCompose()
        => !IsSubmittingCompose && !string.IsNullOrWhiteSpace(ComposeText);

    private Task SubmitComposeAsync()
    {
        var text = (ComposeText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return Task.CompletedTask;
        }

        _composeCts?.Cancel();
        _composeCts?.Dispose();
        _composeCts = CancellationTokenSource.CreateLinkedTokenSource(_activeCts?.Token ?? CancellationToken.None);

        return SubmitComposeInternalAsync(text, _composeCts.Token);
    }

    private async Task SubmitComposeInternalAsync(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IsSubmittingCompose = true;
        _statusBar.Set(message: "Searching…", isBusy: true, isRefreshing: false);

        try
        {
            var response = await _composeSearch.SearchAsync(
                new ComposeSearch.ComposeSearchRequest(text, PageSize: 20),
                RefreshMode.CacheOnly,
                ct).ConfigureAwait(false);

            // If AI produced a structured query, show it to the user for editing.
            // Also use it for any follow-up refresh to avoid re-invoking AI and changing queries.
            var effectiveText = string.IsNullOrWhiteSpace(response.Query) ? text : response.Query;
            if (!string.Equals(ComposeText, effectiveText, StringComparison.Ordinal))
            {
                ComposeText = effectiveText;
            }

            _composeState.SetLatest(response);
            var activeCt = _activeCts?.Token;
            if (activeCt is not null)
            {
                _ = RefreshCardsAsync(RefreshMode.CacheOnly, activeCt.Value);
            }

            // Background refresh for fresher results.
            _ = Task.Run(async () =>
            {
                try
                {
                    var refreshed = await _composeSearch.SearchAsync(
                        new ComposeSearch.ComposeSearchRequest(effectiveText, PageSize: 20),
                        RefreshMode.PreferCacheThenRefresh,
                        ct).ConfigureAwait(false);

                    _composeState.SetLatest(refreshed);
                    var activeCt = _activeCts?.Token;
                    if (activeCt is not null)
                    {
                        _ = RefreshCardsAsync(RefreshMode.CacheOnly, activeCt.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Compose background search refresh failed");
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compose search failed");
            _statusBar.Set(message: "Search failed", isBusy: false, isRefreshing: false);
        }
        finally
        {
            IsSubmittingCompose = false;
            _statusBar.Set(isBusy: false, isRefreshing: false);
        }
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
            var plannedProviders = _cardProviders
                .Select((provider, order) => new PlannedProvider(provider, GetTier(provider), order))
                .ToArray();

            var cardsByProviderId = new Dictionary<string, IReadOnlyList<DashboardCardModel>>(StringComparer.Ordinal);
            var cardsLock = new object();

            async Task PublishCardsAsync(CancellationToken publishCt)
            {
                List<DashboardCardModel> snapshot;

                lock (cardsLock)
                {
                    snapshot = BuildSnapshot(plannedProviders, cardsByProviderId);
                }

                await _dispatcher.ExecuteAsync(_ =>
                {
                    ObservableCollectionSync.SyncById(
                        Cards,
                        snapshot,
                        getId: x => x.CardId,
                        shouldReplace: (current, next) => current != next);

                    return ValueTask.CompletedTask;
                });
            }

            async Task RunProviderAsync(PlannedProvider planned, RefreshMode providerRefresh, CancellationToken providerCt)
            {
                providerCt.ThrowIfCancellationRequested();

                try
                {
                    var cards = await planned.Provider.GetCardsAsync(Context, providerRefresh, providerCt).ConfigureAwait(false);

                    var normalized = Normalize(cards);
                    lock (cardsLock)
                    {
                        if (normalized.Count == 0)
                        {
                            cardsByProviderId.Remove(planned.Provider.ProviderId);
                        }
                        else
                        {
                            cardsByProviderId[planned.Provider.ProviderId] = normalized;
                        }
                    }

                    await PublishCardsAsync(providerCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dashboard card provider failed (ProviderId={ProviderId})", planned.Provider.ProviderId);
                }
            }

            // Wave A: always run the cheapest/high-yield providers first.
            await StagedWorkScheduler.RunAsync(
                plannedProviders,
                tierSelector: p => (int)p.Tier,
                maxTierInclusive: (int)DashboardCardProviderTier.SingleCallMultiCard,
                maxConcurrency: CardProviderMaxConcurrency,
                workAsync: (p, token) => RunProviderAsync(p, refresh, token),
                ct: localCt).ConfigureAwait(false);

            if (refresh == RefreshMode.CacheOnly && !localCt.IsCancellationRequested)
            {
                // Wave B: background refresh for everything that can hit the network.
                var backgroundProviders = plannedProviders
                    .Where(p => p.Tier != DashboardCardProviderTier.Local)
                    .ToArray();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await StagedWorkScheduler.RunAsync(
                            backgroundProviders,
                            tierSelector: p => (int)p.Tier,
                            maxTierInclusive: (int)DashboardCardProviderTier.MultiCallEnrichment,
                            maxConcurrency: CardProviderMaxConcurrency,
                            workAsync: (p, token) => RunProviderAsync(p, RefreshMode.PreferCacheThenRefresh, token),
                            ct: localCt).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore cancellation.
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background card refresh failed");
                    }
                });
            }
            else
            {
                // If the caller explicitly requested a refresh, finish the remaining tiers as part of this call.
                var remainingProviders = plannedProviders
                    .Where(p => p.Tier > DashboardCardProviderTier.SingleCallMultiCard)
                    .ToArray();

                await StagedWorkScheduler.RunAsync(
                    remainingProviders,
                    tierSelector: p => (int)p.Tier,
                    maxTierInclusive: (int)DashboardCardProviderTier.MultiCallEnrichment,
                    maxConcurrency: CardProviderMaxConcurrency,
                    workAsync: (p, token) => RunProviderAsync(p, refresh, token),
                    ct: localCt).ConfigureAwait(false);
            }
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

    private static DashboardCardProviderTier GetTier(IDashboardCardProvider provider)
        => provider is IStagedDashboardCardProvider staged
            ? staged.Tier
            : DashboardCardProviderTier.SingleCallSingleCard;

    private static IReadOnlyList<DashboardCardModel> Normalize(IReadOnlyList<DashboardCardModel>? cards)
    {
        if (cards is null || cards.Count == 0)
        {
            return Array.Empty<DashboardCardModel>();
        }

        return cards
            .OrderByDescending(c => c.Importance)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .ThenBy(c => c.CardId)
            .ToArray();
    }

    private static List<DashboardCardModel> BuildSnapshot(
        IReadOnlyList<PlannedProvider> providerOrder,
        IReadOnlyDictionary<string, IReadOnlyList<DashboardCardModel>> cardsByProviderId)
    {
        var combined = new List<DashboardCardModel>();

        foreach (var planned in providerOrder)
        {
            if (!cardsByProviderId.TryGetValue(planned.Provider.ProviderId, out var cards) || cards.Count == 0)
            {
                continue;
            }

            combined.AddRange(cards);
        }

        return combined;
    }

    private sealed record PlannedProvider(IDashboardCardProvider Provider, DashboardCardProviderTier Tier, int Order);
}
