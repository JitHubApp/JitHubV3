using System.Collections.ObjectModel;
using System.ComponentModel;
using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class DownloadProgressListViewModel : ObservableObject, IDisposable
{
    private readonly IAiModelDownloadQueue _downloads;
    private readonly Dictionary<Guid, DownloadProgressItemViewModel> _itemsById = new();

    public ObservableCollection<DownloadProgressItemViewModel> Items { get; } = new();

    public bool IsVisible => Items.Count > 0;

    public DownloadProgressListViewModel(IAiModelDownloadQueue downloads)
    {
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));

        _downloads.DownloadsChanged += OnDownloadsChanged;
        RefreshFromQueue();

        Items.CollectionChanged += (_, __) => OnPropertyChanged(nameof(IsVisible));
    }

    public void Dispose()
    {
        _downloads.DownloadsChanged -= OnDownloadsChanged;

        foreach (var item in _itemsById.Values)
        {
            item.Dispose();
        }

        _itemsById.Clear();
        Items.Clear();
    }

    private void OnDownloadsChanged()
    {
        RefreshFromQueue();
    }

    private void RefreshFromQueue()
    {
        var active = _downloads.GetActiveDownloads();
        var ids = new HashSet<Guid>(active.Select(h => h.Id));

        // Remove missing items
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var existing = Items[i];
            if (!ids.Contains(existing.DownloadId))
            {
                Items.RemoveAt(i);
                if (_itemsById.Remove(existing.DownloadId, out var removed))
                {
                    removed.Dispose();
                }
            }
        }

        // Add new items
        foreach (var h in active)
        {
            if (_itemsById.ContainsKey(h.Id))
            {
                continue;
            }

            var vm = new DownloadProgressItemViewModel(h);
            _itemsById[h.Id] = vm;
            Items.Add(vm);
        }
    }
}

public sealed partial class DownloadProgressItemViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable _subscription;

    public Guid DownloadId { get; }

    private string _modelId;
    public string ModelId
    {
        get => _modelId;
        private set => SetProperty(ref _modelId, value);
    }

    private AiModelDownloadStatus _status;
    public AiModelDownloadStatus Status
    {
        get => _status;
        private set
        {
            if (!SetProperty(ref _status, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
        }
    }

    private double? _progress;
    public double? Progress
    {
        get => _progress;
        private set
        {
            if (!SetProperty(ref _progress, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
        }
    }

    public double ProgressPercent => Progress is null ? 0 : Math.Round(Progress.Value * 100, 1);

    public bool IsProgressIndeterminate => Progress is null;

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasErrorMessage));
        }
    }

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string StatusText => Status switch
    {
        AiModelDownloadStatus.Queued => "Queued",
        AiModelDownloadStatus.Downloading => "Downloading",
        AiModelDownloadStatus.Verifying => "Verifying",
        AiModelDownloadStatus.VerificationFailed => "Verification failed",
        AiModelDownloadStatus.Completed => "Completed",
        AiModelDownloadStatus.Canceled => "Canceled",
        AiModelDownloadStatus.Failed => "Failed",
        _ => "",
    };

    public DownloadProgressItemViewModel(AiModelDownloadHandle handle)
    {
        if (handle is null)
        {
            throw new ArgumentNullException(nameof(handle));
        }

        DownloadId = handle.Id;
        _modelId = handle.Request.ModelId;
        _status = handle.Latest.Status;
        _progress = handle.Latest.Progress;
        _errorMessage = handle.Latest.ErrorMessage;

        _subscription = handle.Subscribe(OnProgress);
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    private void OnProgress(AiModelDownloadProgress p)
    {
        ModelId = p.ModelId;
        Status = p.Status;
        Progress = p.Progress;
        ErrorMessage = p.ErrorMessage;
    }
}
