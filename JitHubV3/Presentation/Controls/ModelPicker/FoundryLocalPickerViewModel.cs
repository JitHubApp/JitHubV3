using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JitHubV3.Services.Ai;
using JitHubV3.Services.Ai.ExternalProviders.FoundryLocal;
using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class FoundryLocalPickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "FoundryLocalTemplate";

    private readonly IFoundryLocalModelProvider _provider;
    private readonly IAiModelDownloadQueue _downloads;
    private readonly IAiModelStore _modelStore;
    private readonly ILocalModelShellActions _shell;

    public ObservableCollection<FoundryModelPairViewModel> AvailableModels { get; } = new();

    public ObservableCollection<FoundryCatalogModelGroupViewModel> CatalogModels { get; } = new();

    private FoundryModelPairViewModel? _selectedModel;
    public FoundryModelPairViewModel? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FooterSummary));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    private string _foundryLocalUrl = string.Empty;
    public string FoundryLocalUrl
    {
        get => _foundryLocalUrl;
        private set => SetProperty(ref _foundryLocalUrl, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    private bool _isNotAvailable;
    public bool IsNotAvailable
    {
        get => _isNotAvailable;
        private set => SetProperty(ref _isNotAvailable, value);
    }

    public bool ShowModels => !IsLoading && !IsNotAvailable;

    public bool ShowEmptyState => AvailableModels.Count == 0;

    public IAsyncRelayCommand CopyUrlCommand { get; }

    public IAsyncRelayCommand<DownloadableFoundryModelViewModel> DownloadModelCommand { get; }

    public FoundryLocalPickerViewModel(
        IFoundryLocalModelProvider provider,
        IAiModelDownloadQueue downloads,
        IAiModelStore modelStore,
        ILocalModelShellActions shell)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));

        CopyUrlCommand = new AsyncRelayCommand(CopyUrlAsync);
        DownloadModelCommand = new AsyncRelayCommand<DownloadableFoundryModelViewModel>(DownloadModelAsync);

        AvailableModels.CollectionChanged += (_, __) => OnPropertyChanged(nameof(ShowEmptyState));
        _downloads.DownloadsChanged += () => OnPropertyChanged(nameof(ShowEmptyState));
    }

    public override string FooterSummary
    {
        get
        {
            var model = SelectedModel;
            if (model is null)
            {
                return "No model selected";
            }

            return $"Selected: Foundry Local · {model.Name}";
        }
    }

    public override bool CanApply => SelectedModel is not null;

    public override IReadOnlyList<PickerSelectedModel> GetSelectedModels()
    {
        var model = SelectedModel;
        if (model is null)
        {
            return Array.Empty<PickerSelectedModel>();
        }

        return new[]
        {
            new PickerSelectedModel(
                SlotId: "default",
                RuntimeId: "local-foundry",
                ModelId: model.Name,
                DisplayName: model.Name)
        };
    }

    public override void RemoveSelectedModel(PickerSelectedModel model)
    {
        SelectedModel = null;
    }

    public override async Task ApplyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var model = SelectedModel;
        if (model is null)
        {
            return;
        }

        await _modelStore.SetSelectionAsync(new AiModelSelection(RuntimeId: "local-foundry", ModelId: model.Name), ct)
            .ConfigureAwait(false);
    }

    public override async Task RefreshAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IsLoading = true;
        IsNotAvailable = false;
        OnPropertyChanged(nameof(ShowModels));

        try
        {
            var available = await _provider.IsAvailable(ct).ConfigureAwait(false);
            if (!available)
            {
                AvailableModels.Clear();
                CatalogModels.Clear();
                FoundryLocalUrl = string.Empty;

                IsNotAvailable = true;
                return;
            }

            FoundryLocalUrl = _provider.Url;

            var downloaded = await _provider.GetModelsAsync(ignoreCached: true, ct).ConfigureAwait(false);
            var catalog = await _provider.GetAllModelsInCatalogAsync(ct).ConfigureAwait(false);

            var downloadedNames = downloaded.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);

            AvailableModels.Clear();
            foreach (var m in downloaded.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                AvailableModels.Add(new FoundryModelPairViewModel(m));
            }

            CatalogModels.Clear();
            foreach (var group in catalog
                         .OfType<FoundryLocalModelDetails>()
                         .GroupBy(m => GetAlias(m) ?? m.Name, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var modelsInGroup = group.ToArray();

                var groupVm = new FoundryCatalogModelGroupViewModel(
                    alias: group.Key,
                    license: modelsInGroup.Select(m => m.License).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)));

                foreach (var m in modelsInGroup.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                {
                    groupVm.Details.Add(new FoundryCatalogModelDetailsViewModel(m));

                    if (!downloadedNames.Contains(m.Name))
                    {
                        groupVm.Models.Add(new DownloadableFoundryModelViewModel(m));
                    }
                }

                CatalogModels.Add(groupVm);
            }

            // Best-effort: restore selection if it points at a Foundry Local model.
            try
            {
                var selection = await _modelStore.GetSelectionAsync(ct).ConfigureAwait(false);
                if (selection is not null && string.Equals(selection.RuntimeId, "local-foundry", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedModel = AvailableModels.FirstOrDefault(x => string.Equals(x.Name, selection.ModelId, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
                // ignore
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowModels));
        }
    }

    private async Task CopyUrlAsync()
    {
        var url = FoundryLocalUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        await _shell.CopyTextAsync(url).ConfigureAwait(false);
    }

    private async Task DownloadModelAsync(DownloadableFoundryModelViewModel? model)
    {
        if (model is null)
        {
            return;
        }

        if (!model.CanDownload)
        {
            return;
        }

        model.BeginDownload();

        var request = new AiModelDownloadRequest(
            ModelId: model.ModelName,
            RuntimeId: "local-foundry",
            SourceUri: new Uri($"fl://{model.ModelName}"),
            InstallPath: GetDefaultInstallPath(model.ModelName));

        var handle = _downloads.Enqueue(request);

        // Keep the in-flyout progress ring reasonably smooth but not noisy.
        var lastUpdate = DateTimeOffset.MinValue;
        var subscription = handle.Subscribe(p =>
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - lastUpdate) < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            lastUpdate = now;
            model.UpdateFromProgress(p);
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await handle.Task.ConfigureAwait(false);
            }
            catch
            {
                // ignore; progress will reflect failure.
            }
            finally
            {
                subscription.Dispose();
                // Refresh to move model from catalog->available.
                try
                {
                    await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }
        });
    }

    private static string? GetAlias(FoundryLocalModelDetails details)
    {
        if (details.ProviderModelDetails is JitHubV3.Services.Ai.FoundryLocal.FoundryCatalogModel m)
        {
            return m.Alias;
        }

        return null;
    }

    private static string GetDefaultInstallPath(string folderName)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "JitHubV3", "ai", "models", folderName);
    }
}

public sealed class FoundryModelPairViewModel
{
    private readonly FoundryLocalModelDetails _details;

    public FoundryModelPairViewModel(FoundryLocalModelDetails details)
    {
        _details = details;
    }

    public string Name => _details.Name;

    public string? License => _details.License;

    public long? SizeBytes => _details.SizeBytes;

    public string Description => _details.Description;

    public string ExecutionProviderShort
        => _details.ProviderModelDetails is JitHubV3.Services.Ai.FoundryLocal.FoundryCatalogModel m
            ? FoundryLocalText.GetShortExecutionProvider(m.Runtime.ExecutionProvider)
            : string.Empty;
}

public sealed class FoundryCatalogModelGroupViewModel
{
    public FoundryCatalogModelGroupViewModel(string alias, string? license)
    {
        Alias = alias;
        License = license;
    }

    public string Alias { get; }

    public string? License { get; }

    public ObservableCollection<FoundryCatalogModelDetailsViewModel> Details { get; } = new();

    public ObservableCollection<DownloadableFoundryModelViewModel> Models { get; } = new();
}

public sealed class FoundryCatalogModelDetailsViewModel
{
    private readonly FoundryLocalModelDetails _details;

    public FoundryCatalogModelDetailsViewModel(FoundryLocalModelDetails details)
    {
        _details = details;

        if (_details.ProviderModelDetails is JitHubV3.Services.Ai.FoundryLocal.FoundryCatalogModel m)
        {
            RuntimeDeviceType = m.Runtime.DeviceType;
            RuntimeExecutionProvider = m.Runtime.ExecutionProvider;
            ExecutionProviderShort = FoundryLocalText.GetShortExecutionProvider(m.Runtime.ExecutionProvider);
            SizeInBytes = _details.SizeBytes;
        }
    }

    public string ModelName => _details.Name;

    public string ExecutionProviderShort { get; } = string.Empty;

    public string RuntimeDeviceType { get; } = string.Empty;

    public string RuntimeExecutionProvider { get; } = string.Empty;

    public long? SizeInBytes { get; }
}

public sealed partial class DownloadableFoundryModelViewModel : ObservableObject
{
    private readonly FoundryLocalModelDetails _details;

    public DownloadableFoundryModelViewModel(FoundryLocalModelDetails details)
    {
        _details = details;
    }

    public FoundryLocalModelDetails ModelDetails => _details;

    public string ModelName => _details.Name;

    public string DisplayText
        => _details.ProviderModelDetails is JitHubV3.Services.Ai.FoundryLocal.FoundryCatalogModel m
            ? $"{FoundryLocalText.GetShortExecutionProvider(m.Runtime.ExecutionProvider)} · {FoundryLocalText.FormatBytes(_details.SizeBytes)}"
            : _details.Name;

    private bool _canDownload = true;
    public bool CanDownload
    {
        get => _canDownload;
        private set => SetProperty(ref _canDownload, value);
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public void BeginDownload()
    {
        CanDownload = false;
        Progress = 0;
    }

    public void UpdateFromProgress(AiModelDownloadProgress p)
    {
        if (p.Progress is not null)
        {
            Progress = Math.Clamp(p.Progress.Value, 0.0, 1.0) * 100.0;
        }

        if (p.Status is AiModelDownloadStatus.Completed)
        {
            Progress = 100.0;
            CanDownload = false;
        }
        else if (p.Status is AiModelDownloadStatus.Failed or AiModelDownloadStatus.Canceled or AiModelDownloadStatus.VerificationFailed)
        {
            // Allow retry.
            CanDownload = true;
        }
    }
}

internal static class FoundryLocalText
{
    public static string GetShortExecutionProvider(string executionProvider)
    {
        if (string.IsNullOrWhiteSpace(executionProvider))
        {
            return string.Empty;
        }

        // Match AI Dev Gallery convention in spirit (short, recognizable labels).
        if (executionProvider.Contains("Dml", StringComparison.OrdinalIgnoreCase))
        {
            return "DML";
        }

        if (executionProvider.Contains("Cuda", StringComparison.OrdinalIgnoreCase))
        {
            return "CUDA";
        }

        if (executionProvider.Contains("Cpu", StringComparison.OrdinalIgnoreCase))
        {
            return "CPU";
        }

        return executionProvider;
    }

    public static string FormatBytes(long? bytes)
    {
        if (bytes is null || bytes <= 0)
        {
            return string.Empty;
        }

        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;

        var b = (double)bytes.Value;
        if (b >= GB)
        {
            return $"{b / GB:0.#} GB";
        }

        if (b >= MB)
        {
            return $"{b / MB:0.#} MB";
        }

        if (b >= KB)
        {
            return $"{b / KB:0.#} KB";
        }

        return $"{bytes} B";
    }
}
