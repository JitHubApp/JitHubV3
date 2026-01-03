using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JitHubV3.Services.Ai;
using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class LocalModelsPickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "LocalModelsTemplate";

    private readonly IAiLocalModelCatalog _catalog;
    private readonly IAiModelDownloadQueue _downloads;
    private readonly IAiModelStore _modelStore;
    private readonly IAiStatusEventPublisher _events;
    private readonly IReadOnlyList<AiLocalModelDefinition> _definitions;

    private readonly Dictionary<Guid, IDisposable> _subscriptionsByDownloadId = new();
    private bool _initialized;

    public ObservableCollection<LocalModelOptionViewModel> Items { get; } = new();

    private LocalModelOptionViewModel? _selectedItem;
    public LocalModelOptionViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FooterSummary));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public LocalModelsPickerViewModel(
        IAiLocalModelCatalog catalog,
        IAiModelDownloadQueue downloads,
        IAiModelStore modelStore,
        IAiStatusEventPublisher events,
        IReadOnlyList<AiLocalModelDefinition> definitions)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _definitions = definitions ?? Array.Empty<AiLocalModelDefinition>();

        _downloads.DownloadsChanged += OnDownloadsChanged;
    }

    public override string FooterSummary
    {
        get
        {
            var item = SelectedItem;
            if (item is null)
            {
                return "No model selected";
            }

            return item.IsDownloaded
                ? $"Selected: Local · {item.DisplayNameOrId}"
                : $"Selected: Local · {item.DisplayNameOrId} (not downloaded)";
        }
    }

    public override bool CanApply
    {
        get
        {
            var item = SelectedItem;
            return item is not null && item.IsDownloaded;
        }
    }

    public override async Task RefreshAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var items = await _catalog.GetCatalogAsync(ct).ConfigureAwait(false);

        Items.Clear();

        foreach (var i in items)
        {
            var def = _definitions.FirstOrDefault(d =>
                string.Equals(d.ModelId, i.ModelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.RuntimeId, i.RuntimeId, StringComparison.OrdinalIgnoreCase));

            Uri? downloadUri = null;
            if (def?.DownloadUri is not null && Uri.TryCreate(def.DownloadUri, UriKind.Absolute, out var parsed))
            {
                downloadUri = parsed;
            }

            Items.Add(new LocalModelOptionViewModel(
                downloads: _downloads,
                modelId: i.ModelId,
                runtimeId: i.RuntimeId,
                displayName: i.DisplayName,
                isDownloaded: i.IsDownloaded,
                installPath: i.InstallPath,
                downloadUri: downloadUri,
                artifactFileName: def?.ArtifactFileName,
                expectedBytes: def?.ExpectedBytes,
                expectedSha256: def?.ExpectedSha256));
        }

        if (!_initialized)
        {
            _initialized = true;

            // Best-effort: restore selection if it points at a local model.
            try
            {
                var selection = await _modelStore.GetSelectionAsync(ct).ConfigureAwait(false);
                if (selection is not null && string.Equals(selection.RuntimeId, "local-foundry", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedItem = Items.FirstOrDefault(x => string.Equals(x.ModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
                // ignore
            }
        }

        AttachToActiveDownloads();

        OnPropertyChanged(nameof(FooterSummary));
        OnPropertyChanged(nameof(CanApply));
    }

    public override IReadOnlyList<PickerSelectedModel> GetSelectedModels()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return Array.Empty<PickerSelectedModel>();
        }

        return new[]
        {
            new PickerSelectedModel(
                SlotId: "default",
                RuntimeId: item.RuntimeId,
                ModelId: item.ModelId,
                DisplayName: item.DisplayNameOrId)
        };
    }

    public override void RemoveSelectedModel(PickerSelectedModel model)
    {
        SelectedItem = null;
    }

    public override async Task ApplyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var item = SelectedItem;
        if (item is null || !item.IsDownloaded)
        {
            return;
        }

        await _modelStore.SetSelectionAsync(new AiModelSelection(RuntimeId: "local-foundry", ModelId: item.ModelId), ct).ConfigureAwait(false);
    }

    private void OnDownloadsChanged()
    {
        AttachToActiveDownloads();
    }

    private void AttachToActiveDownloads()
    {
        var active = _downloads.GetActiveDownloads();

        foreach (var handle in active)
        {
            if (_subscriptionsByDownloadId.ContainsKey(handle.Id))
            {
                continue;
            }

            var item = Items.FirstOrDefault(i =>
                string.Equals(i.ModelId, handle.Request.ModelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.RuntimeId, handle.Request.RuntimeId, StringComparison.OrdinalIgnoreCase));

            if (item is null)
            {
                continue;
            }

            _subscriptionsByDownloadId[handle.Id] = handle.Subscribe(p =>
            {
                _events.Publish(new AiDownloadProgressChanged(handle.Id, handle.Request, p));
                item.ApplyProgress(p);
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(FooterSummary));
            });

            item.AttachHandle(handle);
        }

        // Cleanup subscriptions for downloads that are no longer active.
        var activeIds = active.Select(a => a.Id).ToHashSet();
        foreach (var kvp in _subscriptionsByDownloadId.ToArray())
        {
            if (activeIds.Contains(kvp.Key))
            {
                continue;
            }

            kvp.Value.Dispose();
            _subscriptionsByDownloadId.Remove(kvp.Key);
        }
    }
}

public sealed partial class LocalModelOptionViewModel : ObservableObject
{
    private readonly IAiModelDownloadQueue _downloads;

    private AiModelDownloadHandle? _handle;

    public string ModelId { get; }

    public string RuntimeId { get; }

    public string? DisplayName { get; }

    public string DisplayNameOrId => string.IsNullOrWhiteSpace(DisplayName) ? ModelId : DisplayName!;

    public string StatusText
    {
        get
        {
            if (IsDownloaded)
            {
                return "Downloaded";
            }

            return DownloadStatus switch
            {
                AiModelDownloadStatus.Queued => "Queued",
                AiModelDownloadStatus.Downloading => IsProgressIndeterminate
                    ? "Downloading…"
                    : $"Downloading… {ProgressPercent:0.#}%",
                AiModelDownloadStatus.Completed => "Downloaded",
                AiModelDownloadStatus.Canceled => "Canceled",
                AiModelDownloadStatus.Failed => "Failed",
                _ => "",
            };
        }
    }

    public string InstallPath { get; }

    public Uri? DownloadUri { get; }

    public string? ArtifactFileName { get; }

    public long? ExpectedBytes { get; }

    public string? ExpectedSha256 { get; }

    private bool _isDownloaded;
    public bool IsDownloaded
    {
        get => _isDownloaded;
        private set => SetProperty(ref _isDownloaded, value);
    }

    private AiModelDownloadStatus _downloadStatus;
    public AiModelDownloadStatus DownloadStatus
    {
        get => _downloadStatus;
        private set => SetProperty(ref _downloadStatus, value);
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    private bool _isProgressIndeterminate;
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public bool CanDownload => !IsDownloaded && DownloadUri is not null && _handle is null;

    public bool CanCancel => _handle is not null;

    public IAsyncRelayCommand DownloadCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public LocalModelOptionViewModel(
        IAiModelDownloadQueue downloads,
        string modelId,
        string runtimeId,
        string? displayName,
        bool isDownloaded,
        string installPath,
        Uri? downloadUri,
        string? artifactFileName,
        long? expectedBytes,
        string? expectedSha256)
    {
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));

        ModelId = modelId;
        RuntimeId = runtimeId;
        DisplayName = displayName;
        InstallPath = installPath;
        DownloadUri = downloadUri;
        ArtifactFileName = artifactFileName;
        ExpectedBytes = expectedBytes;
        ExpectedSha256 = expectedSha256;

        IsDownloaded = isDownloaded;
        DownloadStatus = isDownloaded ? AiModelDownloadStatus.Completed : AiModelDownloadStatus.Queued;
        ProgressPercent = isDownloaded ? 100 : 0;
        IsProgressIndeterminate = !isDownloaded;

        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => CanDownload);
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
    }

    internal void AttachHandle(AiModelDownloadHandle handle)
    {
        _handle = handle;
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(StatusText));
        DownloadCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public async Task DownloadAsync()
    {
        if (!CanDownload || DownloadUri is null)
        {
            return;
        }

        var handle = _downloads.Enqueue(new AiModelDownloadRequest(
            ModelId: ModelId,
            RuntimeId: RuntimeId,
            SourceUri: DownloadUri,
            InstallPath: InstallPath,
            ArtifactFileName: ArtifactFileName,
            ExpectedBytes: ExpectedBytes,
            ExpectedSha256: ExpectedSha256));

        AttachHandle(handle);

        try
        {
            await handle.Task.ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }
    }

    public void Cancel()
    {
        if (_handle is null)
        {
            return;
        }

        _downloads.Cancel(_handle.Id);
    }

    public void ApplyProgress(AiModelDownloadProgress p)
    {
        DownloadStatus = p.Status;
        IsProgressIndeterminate = p.Progress is null;
        ProgressPercent = p.Progress is null ? 0 : Math.Round(p.Progress.Value * 100, 1);

        if (p.Status == AiModelDownloadStatus.Completed)
        {
            IsDownloaded = true;
            _handle = null;
        }

        if (p.Status == AiModelDownloadStatus.Canceled || p.Status == AiModelDownloadStatus.Failed)
        {
            _handle = null;
        }

        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(StatusText));

        DownloadCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
