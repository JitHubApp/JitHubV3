using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    private readonly IAiLocalModelInventoryStore _inventory;
    private readonly IAiLocalModelDefinitionStore _definitionStore;
    private readonly ILocalModelShellActions _shell;
    private readonly IAiStatusEventPublisher _events;
    private readonly IReadOnlyList<AiLocalModelDefinition> _builtInDefinitions;

    private readonly Dictionary<Guid, IDisposable> _subscriptionsByDownloadId = new();
    private bool _initialized;

    public ObservableCollection<LocalModelGroupViewModel> Groups { get; } = new();

    private readonly LocalModelGroupViewModel _availableGroup = new("Available");
    private readonly LocalModelGroupViewModel _downloadableGroup = new("Downloadable");
    private readonly LocalModelGroupViewModel _unavailableGroup = new("Unavailable");

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
        IAiLocalModelInventoryStore inventory,
        IAiLocalModelDefinitionStore definitionStore,
        ILocalModelShellActions shell,
        IAiStatusEventPublisher events,
        IReadOnlyList<AiLocalModelDefinition> definitions)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _definitionStore = definitionStore ?? throw new ArgumentNullException(nameof(definitionStore));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _builtInDefinitions = definitions ?? Array.Empty<AiLocalModelDefinition>();

        _downloads.DownloadsChanged += OnDownloadsChanged;

        Groups.Add(_availableGroup);
        Groups.Add(_downloadableGroup);
        Groups.Add(_unavailableGroup);
    }

    public bool HasDownloadedModels => _availableGroup.Items.Count > 0;

    public bool ShowEmptyState => !HasDownloadedModels;

    public bool HasAnyModels => _availableGroup.Items.Count + _downloadableGroup.Items.Count + _unavailableGroup.Items.Count > 0;

    private bool _isAddPanelVisible;
    public bool IsAddPanelVisible
    {
        get => _isAddPanelVisible;
        private set => SetProperty(ref _isAddPanelVisible, value);
    }

    private AddModelMode _addMode;
    public AddModelMode AddMode
    {
        get => _addMode;
        private set
        {
            if (!SetProperty(ref _addMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAddViaUrl));
            OnPropertyChanged(nameof(IsAddLocalPath));
        }
    }

    public bool IsAddViaUrl => AddMode == AddModelMode.Url;

    public bool IsAddLocalPath => AddMode == AddModelMode.LocalPath;

    private string _addModelId = string.Empty;
    public string AddModelId
    {
        get => _addModelId;
        set => SetProperty(ref _addModelId, value);
    }

    private string _addDisplayName = string.Empty;
    public string AddDisplayName
    {
        get => _addDisplayName;
        set => SetProperty(ref _addDisplayName, value);
    }

    private string _addInstallPath = string.Empty;
    public string AddInstallPath
    {
        get => _addInstallPath;
        set => SetProperty(ref _addInstallPath, value);
    }

    private string _addDownloadUri = string.Empty;
    public string AddDownloadUri
    {
        get => _addDownloadUri;
        set => SetProperty(ref _addDownloadUri, value);
    }

    private string _addArtifactFileName = string.Empty;
    public string AddArtifactFileName
    {
        get => _addArtifactFileName;
        set => SetProperty(ref _addArtifactFileName, value);
    }

    private string _addModelCardUri = string.Empty;
    public string AddModelCardUri
    {
        get => _addModelCardUri;
        set => SetProperty(ref _addModelCardUri, value);
    }

    private string _addLicenseUri = string.Empty;
    public string AddLicenseUri
    {
        get => _addLicenseUri;
        set => SetProperty(ref _addLicenseUri, value);
    }

    public IRelayCommand ShowAddViaUrlCommand => new RelayCommand(() => ShowAddPanel(AddModelMode.Url));

    public IRelayCommand ShowAddLocalPathCommand => new RelayCommand(() => ShowAddPanel(AddModelMode.LocalPath));

    public IRelayCommand CancelAddModelCommand => new RelayCommand(() => IsAddPanelVisible = false);

    public IAsyncRelayCommand AddModelCommand => new AsyncRelayCommand(AddModelAsync);

    private void ShowAddPanel(AddModelMode mode)
    {
        AddMode = mode;
        IsAddPanelVisible = true;
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

        var definitions = await GetMergedDefinitionsAsync(ct).ConfigureAwait(false);

        _availableGroup.Items.Clear();
        _downloadableGroup.Items.Clear();
        _unavailableGroup.Items.Clear();

        foreach (var i in items)
        {
            var def = definitions.FirstOrDefault(d =>
                string.Equals(d.ModelId, i.ModelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.RuntimeId, i.RuntimeId, StringComparison.OrdinalIgnoreCase));

            Uri? downloadUri = null;
            if (def?.DownloadUri is not null && Uri.TryCreate(def.DownloadUri, UriKind.Absolute, out var parsed))
            {
                downloadUri = parsed;
            }

            Uri? modelCardUri = null;
            if (def?.ModelCardUri is not null && Uri.TryCreate(def.ModelCardUri, UriKind.Absolute, out var modelCardParsed))
            {
                modelCardUri = modelCardParsed;
            }

            Uri? licenseUri = null;
            if (def?.LicenseUri is not null && Uri.TryCreate(def.LicenseUri, UriKind.Absolute, out var licenseParsed))
            {
                licenseUri = licenseParsed;
            }

            var option = new LocalModelOptionViewModel(
                downloads: _downloads,
                modelId: i.ModelId,
                runtimeId: i.RuntimeId,
                displayName: i.DisplayName,
                isDownloaded: i.IsDownloaded,
                installPath: i.InstallPath,
                downloadUri: downloadUri,
                artifactFileName: def?.ArtifactFileName,
                expectedBytes: def?.ExpectedBytes,
                expectedSha256: def?.ExpectedSha256,
                modelCardUri: modelCardUri,
                licenseUri: licenseUri,
                viewModelCardAsync: ViewModelCardAsync,
                viewLicenseAsync: ViewLicenseAsync,
                copyPathAsync: CopyPathAsync,
                openContainingFolderAsync: OpenContainingFolderAsync,
                deleteAsync: DeleteAsync);

            if (option.IsDownloaded)
            {
                _availableGroup.Items.Add(option);
            }
            else if (option.DownloadUri is not null)
            {
                _downloadableGroup.Items.Add(option);
            }
            else
            {
                _unavailableGroup.Items.Add(option);
            }
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
                    SelectedItem = GetAllItems().FirstOrDefault(x => string.Equals(x.ModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase));
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
        OnPropertyChanged(nameof(HasDownloadedModels));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasAnyModels));
    }

    private async ValueTask<IReadOnlyList<AiLocalModelDefinition>> GetMergedDefinitionsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var custom = await _definitionStore.GetDefinitionsAsync(ct).ConfigureAwait(false);
        if (custom.Count == 0)
        {
            return _builtInDefinitions;
        }

        var merged = new List<AiLocalModelDefinition>(_builtInDefinitions.Count + custom.Count);
        merged.AddRange(_builtInDefinitions);

        foreach (var d in custom)
        {
            if (merged.Any(x => string.Equals(x.ModelId, d.ModelId, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.RuntimeId, d.RuntimeId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            merged.Add(d);
        }

        return merged;
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

    private IReadOnlyList<LocalModelOptionViewModel> GetAllItems()
        => _availableGroup.Items.Concat(_downloadableGroup.Items).Concat(_unavailableGroup.Items).ToArray();

    private static Uri? DeriveModelCardUri(Uri? downloadUri)
    {
        if (downloadUri is null)
        {
            return null;
        }

        try
        {
            if (string.Equals(downloadUri.Host, "huggingface.co", StringComparison.OrdinalIgnoreCase))
            {
                // Convert resolve URLs to a model card: https://huggingface.co/<org>/<model>
                var segments = downloadUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2)
                {
                    return new Uri($"https://huggingface.co/{segments[0]}/{segments[1]}");
                }
            }
        }
        catch
        {
            // ignore
        }

        return downloadUri;
    }

    private static Uri? DeriveLicenseUri(Uri? modelCardUri)
    {
        // Best-effort: if we don't have a license URL, fall back to the model card.
        return modelCardUri;
    }

    private async Task ViewModelCardAsync(LocalModelOptionViewModel item)
    {
        var uri = item.ModelCardUri ?? DeriveModelCardUri(item.DownloadUri) ?? item.DownloadUri;
        if (uri is null)
        {
            return;
        }

        await _shell.LaunchUriAsync(uri);
    }

    private async Task ViewLicenseAsync(LocalModelOptionViewModel item)
    {
        var modelCard = item.ModelCardUri ?? DeriveModelCardUri(item.DownloadUri);
        var uri = item.LicenseUri ?? DeriveLicenseUri(modelCard) ?? item.DownloadUri;
        if (uri is null)
        {
            return;
        }

        await _shell.LaunchUriAsync(uri);
    }

    private Task CopyPathAsync(LocalModelOptionViewModel item)
    {
        return _shell.CopyTextAsync(item.PathToCopy);
    }

    private Task OpenContainingFolderAsync(LocalModelOptionViewModel item)
    {
        var folder = item.ContainingFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return Task.CompletedTask;
        }

        return _shell.OpenFolderAsync(folder);
    }

    private async Task DeleteAsync(LocalModelOptionViewModel item)
    {
        if (!item.IsDownloaded)
        {
            return;
        }

        try
        {
            item.Cancel();

            if (File.Exists(item.InstallPath))
            {
                File.Delete(item.InstallPath);
            }
            else if (Directory.Exists(item.InstallPath))
            {
                Directory.Delete(item.InstallPath, recursive: true);
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            var inv = await _inventory.GetInventoryAsync(CancellationToken.None).ConfigureAwait(false);
            var updated = inv
                .Where(x => !(string.Equals(x.ModelId, item.ModelId, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(x.RuntimeId, item.RuntimeId, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            await _inventory.SetInventoryAsync(updated, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task AddModelAsync()
    {
        var modelId = (AddModelId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        var runtimeId = "local-foundry";

        string? downloadUri = null;
        if (AddMode == AddModelMode.Url)
        {
            downloadUri = string.IsNullOrWhiteSpace(AddDownloadUri) ? null : AddDownloadUri.Trim();
            if (string.IsNullOrWhiteSpace(downloadUri) || !Uri.TryCreate(downloadUri, UriKind.Absolute, out _))
            {
                return;
            }
        }

        var defs = await _definitionStore.GetDefinitionsAsync(CancellationToken.None).ConfigureAwait(false);
        if (defs.Any(d => string.Equals(d.ModelId, modelId, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(d.RuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase)))
        {
            IsAddPanelVisible = false;
            return;
        }

        var modelCardUri = string.IsNullOrWhiteSpace(AddModelCardUri) ? null : AddModelCardUri.Trim();
        var licenseUri = string.IsNullOrWhiteSpace(AddLicenseUri) ? null : AddLicenseUri.Trim();

        var newDef = new AiLocalModelDefinition(
            ModelId: modelId,
            DisplayName: string.IsNullOrWhiteSpace(AddDisplayName) ? null : AddDisplayName.Trim(),
            RuntimeId: runtimeId,
            DefaultInstallFolderName: modelId,
            DownloadUri: downloadUri,
            ArtifactFileName: string.IsNullOrWhiteSpace(AddArtifactFileName) ? null : AddArtifactFileName.Trim(),
            ExpectedBytes: null,
            ExpectedSha256: null,
            ModelCardUri: modelCardUri,
            LicenseUri: licenseUri);

        var updatedDefs = defs.Concat(new[] { newDef }).ToArray();
        await _definitionStore.SetDefinitionsAsync(updatedDefs, CancellationToken.None).ConfigureAwait(false);

        if (AddMode == AddModelMode.LocalPath)
        {
            var path = (AddInstallPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                var inv = await _inventory.GetInventoryAsync(CancellationToken.None).ConfigureAwait(false);
                var updatedInv = inv.Concat(new[]
                {
                    new AiLocalModelInventoryEntry(ModelId: modelId, RuntimeId: runtimeId, InstallPath: path)
                }).ToArray();
                await _inventory.SetInventoryAsync(updatedInv, CancellationToken.None).ConfigureAwait(false);
            }
        }

        IsAddPanelVisible = false;
        await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
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

            var item = GetAllItems().FirstOrDefault(i =>
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

public enum AddModelMode
{
    Url,
    LocalPath,
}

public sealed partial class LocalModelGroupViewModel : ObservableObject
{
    public LocalModelGroupViewModel(string title)
    {
        Title = title;
        Items.CollectionChanged += (_, __) => OnPropertyChanged(nameof(IsEmpty));
    }

    public string Title { get; }

    public ObservableCollection<LocalModelOptionViewModel> Items { get; } = new();

    public bool IsEmpty => Items.Count == 0;
}

public sealed partial class LocalModelOptionViewModel : ObservableObject
{
    private readonly IAiModelDownloadQueue _downloads;

    private readonly Func<LocalModelOptionViewModel, Task>? _viewModelCardAsync;
    private readonly Func<LocalModelOptionViewModel, Task>? _viewLicenseAsync;
    private readonly Func<LocalModelOptionViewModel, Task>? _copyPathAsync;
    private readonly Func<LocalModelOptionViewModel, Task>? _openContainingFolderAsync;
    private readonly Func<LocalModelOptionViewModel, Task>? _deleteAsync;

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
                AiModelDownloadStatus.Verifying => "Verifying…",
                AiModelDownloadStatus.Completed => "Downloaded",
                AiModelDownloadStatus.Canceled => "Canceled",
                AiModelDownloadStatus.Failed => "Failed",
                AiModelDownloadStatus.VerificationFailed => "Verification failed",
                _ => "",
            };
        }
    }

    public string InstallPath { get; }

    public Uri? DownloadUri { get; }

    public Uri? ModelCardUri { get; }

    public Uri? LicenseUri { get; }

    public string? ArtifactFileName { get; }

    public long? ExpectedBytes { get; }

    public string? ExpectedSha256 { get; }

    public string? FileSizeText { get; }

    public bool HasFileSize => !string.IsNullOrWhiteSpace(FileSizeText);

    public string PathToCopy
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstallPath))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(ArtifactFileName))
            {
                try
                {
                    return Path.Combine(InstallPath, ArtifactFileName);
                }
                catch
                {
                    // ignore
                }
            }

            return InstallPath;
        }
    }

    public string ContainingFolderPath
    {
        get
        {
            try
            {
                if (Directory.Exists(InstallPath))
                {
                    return InstallPath;
                }

                var dir = Path.GetDirectoryName(InstallPath);
                return dir ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

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

    public bool CanViewModelCard => _viewModelCardAsync is not null && (ModelCardUri is not null || DownloadUri is not null);

    public bool CanViewLicense => _viewLicenseAsync is not null && (LicenseUri is not null || ModelCardUri is not null || DownloadUri is not null);

    public bool CanCopyPath => _copyPathAsync is not null && !string.IsNullOrWhiteSpace(PathToCopy);

    public bool CanOpenContainingFolder => _openContainingFolderAsync is not null && !string.IsNullOrWhiteSpace(ContainingFolderPath);

    public bool CanDelete => _deleteAsync is not null && IsDownloaded;

    public IAsyncRelayCommand ViewModelCardCommand { get; }

    public IAsyncRelayCommand ViewLicenseCommand { get; }

    public IAsyncRelayCommand CopyPathCommand { get; }

    public IAsyncRelayCommand OpenContainingFolderCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

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
        string? expectedSha256,
        Uri? modelCardUri,
        Uri? licenseUri,
        Func<LocalModelOptionViewModel, Task>? viewModelCardAsync,
        Func<LocalModelOptionViewModel, Task>? viewLicenseAsync,
        Func<LocalModelOptionViewModel, Task>? copyPathAsync,
        Func<LocalModelOptionViewModel, Task>? openContainingFolderAsync,
        Func<LocalModelOptionViewModel, Task>? deleteAsync)
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
        ModelCardUri = modelCardUri;
        LicenseUri = licenseUri;

        _viewModelCardAsync = viewModelCardAsync;
        _viewLicenseAsync = viewLicenseAsync;
        _copyPathAsync = copyPathAsync;
        _openContainingFolderAsync = openContainingFolderAsync;
        _deleteAsync = deleteAsync;

        IsDownloaded = isDownloaded;
        DownloadStatus = isDownloaded ? AiModelDownloadStatus.Completed : AiModelDownloadStatus.Queued;
        ProgressPercent = isDownloaded ? 100 : 0;
        IsProgressIndeterminate = !isDownloaded;

        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => CanDownload);
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);

        ViewModelCardCommand = new AsyncRelayCommand(() => _viewModelCardAsync?.Invoke(this) ?? Task.CompletedTask, () => CanViewModelCard);
        ViewLicenseCommand = new AsyncRelayCommand(() => _viewLicenseAsync?.Invoke(this) ?? Task.CompletedTask, () => CanViewLicense);
        CopyPathCommand = new AsyncRelayCommand(() => _copyPathAsync?.Invoke(this) ?? Task.CompletedTask, () => CanCopyPath);
        OpenContainingFolderCommand = new AsyncRelayCommand(() => _openContainingFolderAsync?.Invoke(this) ?? Task.CompletedTask, () => CanOpenContainingFolder);
        DeleteCommand = new AsyncRelayCommand(() => _deleteAsync?.Invoke(this) ?? Task.CompletedTask, () => CanDelete);

        FileSizeText = ComputeFileSizeText(isDownloaded, installPath, artifactFileName);
    }

    private static string? ComputeFileSizeText(bool isDownloaded, string installPath, string? artifactFileName)
    {
        if (!isDownloaded)
        {
            return null;
        }

        try
        {
            var artifactPath = string.IsNullOrWhiteSpace(artifactFileName)
                ? null
                : Path.Combine(installPath, artifactFileName);

            if (!string.IsNullOrWhiteSpace(artifactPath) && File.Exists(artifactPath))
            {
                return FormatBytes(new FileInfo(artifactPath).Length);
            }

            if (File.Exists(installPath))
            {
                return FormatBytes(new FileInfo(installPath).Length);
            }

            if (Directory.Exists(installPath))
            {
                long total = 0;
                foreach (var f in Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(f).Length;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return total > 0 ? FormatBytes(total) : null;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.#} GB";
        }

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.#} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / kb:0.#} KB";
        }

        return $"{bytes} B";
    }

    internal void AttachHandle(AiModelDownloadHandle handle)
    {
        _handle = handle;
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(StatusText));
        DownloadCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CanDelete));
        DeleteCommand.NotifyCanExecuteChanged();
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

        if (p.Status == AiModelDownloadStatus.Canceled
            || p.Status == AiModelDownloadStatus.Failed
            || p.Status == AiModelDownloadStatus.VerificationFailed)
        {
            _handle = null;
        }

        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(StatusText));

        DownloadCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CanDelete));
        DeleteCommand.NotifyCanExecuteChanged();
    }
}
