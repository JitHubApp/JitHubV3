namespace JitHubV3.Presentation;

public sealed partial class StatusBarViewModel : ObservableObject
{
    private SynchronizationContext? _uiContext;

    private string? _message;
    private bool _isBusy;
    private bool _isRefreshing;
    private DataFreshnessState _freshness = DataFreshnessState.Unknown;
    private DateTimeOffset? _lastUpdatedAt;

    public void AttachToCurrentThread()
    {
        var current = SynchronizationContext.Current;
        if (current is null)
        {
            return;
        }

        // Always allow the UI layer (Shell) to re-attach on the real UI thread.
        // This prevents a first call from a background thread from “poisoning” the context.
        if (_uiContext is null || !ReferenceEquals(_uiContext, current))
        {
            _uiContext = current;
        }
    }

    public string? Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    public DataFreshnessState Freshness
    {
        get => _freshness;
        set
        {
            if (SetProperty(ref _freshness, value))
            {
                OnPropertyChanged(nameof(FreshnessLabel));
            }
        }
    }

    public DateTimeOffset? LastUpdatedAt
    {
        get => _lastUpdatedAt;
        set
        {
            if (SetProperty(ref _lastUpdatedAt, value))
            {
                OnPropertyChanged(nameof(LastUpdatedLabel));
            }
        }
    }

    public string FreshnessLabel => Freshness switch
    {
        DataFreshnessState.Cached => "Cached",
        DataFreshnessState.Fresh => "Fresh",
        _ => string.Empty,
    };

    public string LastUpdatedLabel
        => LastUpdatedAt is null ? string.Empty : $"Updated {LastUpdatedAt:HH:mm:ss}";

    public void Set(
        string? message = null,
        bool? isBusy = null,
        bool? isRefreshing = null,
        DataFreshnessState? freshness = null,
        DateTimeOffset? lastUpdatedAt = null)
    {
        var ui = _uiContext;
        if (ui is null)
        {
            // Best effort: if the caller is on UI thread and a context exists, capture it.
            AttachToCurrentThread();
            ui = _uiContext;
        }

        if (ui is null || ReferenceEquals(SynchronizationContext.Current, ui))
        {
            ApplySet(message, isBusy, isRefreshing, freshness, lastUpdatedAt);
            return;
        }

        ui.Post(_ => ApplySet(message, isBusy, isRefreshing, freshness, lastUpdatedAt), null);
    }

    private void ApplySet(
        string? message,
        bool? isBusy,
        bool? isRefreshing,
        DataFreshnessState? freshness,
        DateTimeOffset? lastUpdatedAt)
    {
        if (message is not null)
        {
            Message = message;
        }

        if (isBusy is not null)
        {
            IsBusy = isBusy.Value;
        }

        if (isRefreshing is not null)
        {
            IsRefreshing = isRefreshing.Value;
        }

        if (freshness is not null)
        {
            Freshness = freshness.Value;
        }

        if (lastUpdatedAt is not null)
        {
            LastUpdatedAt = lastUpdatedAt;
        }
    }

    public void Clear()
    {
        var ui = _uiContext;
        if (ui is null)
        {
            AttachToCurrentThread();
            ui = _uiContext;
        }

        if (ui is null || ReferenceEquals(SynchronizationContext.Current, ui))
        {
            Message = null;
            IsBusy = false;
            IsRefreshing = false;
            Freshness = DataFreshnessState.Unknown;
            LastUpdatedAt = null;
            return;
        }

        ui.Post(_ =>
        {
            Message = null;
            IsBusy = false;
            IsRefreshing = false;
            Freshness = DataFreshnessState.Unknown;
            LastUpdatedAt = null;
        }, null);
    }
}
