using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using JitHub.GitHub.Abstractions.Security;
using JitHubV3.Services.Ai;
using JitHubV3.Services.Ai.ModelPicker;
using JitHubV3.Presentation.Controls.ModelPicker.PickerDefinitions;
using JitHubV3.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class ModelOrApiPickerViewModel : ObservableObject, IModelPickerOverlayViewModel
{
    private readonly IPickerDefinitionRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly IAiModelStore _modelStore;

    private readonly Dictionary<string, IPickerDefinition> _definitionsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PickerCategoryViewModel> _paneCacheById = new(StringComparer.Ordinal);

    private ModelPickerInvocation _invocation = new(
        PickerPrimaryAction.Apply,
        Slots: Array.Empty<ModelPickerSlot>(),
        PersistSelection: true);

    private INotifyPropertyChanged? _activeCategoryChanged;

    public ObservableCollection<PickerSelectedModel> SelectedModels { get; } = new();

    public ObservableCollection<SelectedModelChipViewModel> SelectedModelChips { get; } = new();

    public DownloadProgressListViewModel DownloadProgressList { get; }

    public string PrimaryActionText => _invocation.PrimaryAction == PickerPrimaryAction.RunSample ? "Run sample" : "Apply";

    public ObservableCollection<ModelPickerCategoryItem> Categories { get; } = new();

    private ModelPickerCategoryItem? _selectedCategory;
    public ModelPickerCategoryItem? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!SetProperty(ref _selectedCategory, value))
            {
                return;
            }

            UpdateActiveCategory();
        }
    }

    private PickerCategoryViewModel? _activeCategory;
    public PickerCategoryViewModel? ActiveCategory
    {
        get => _activeCategory;
        private set
        {
            if (ReferenceEquals(_activeCategory, value))
            {
                return;
            }

            if (_activeCategoryChanged is not null)
            {
                _activeCategoryChanged.PropertyChanged -= OnActiveCategoryPropertyChanged;
                _activeCategoryChanged = null;
            }

            if (!SetProperty(ref _activeCategory, value))
            {
                return;
            }

            _activeCategoryChanged = value;
            if (_activeCategoryChanged is not null)
            {
                _activeCategoryChanged.PropertyChanged += OnActiveCategoryPropertyChanged;
            }

            RefreshSelectedModelsFromActive();

            OnPropertyChanged(nameof(FooterSummary));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public string FooterSummary => ActiveCategory?.FooterSummary ?? "No model selected";

    public bool CanApply => ActiveCategory?.CanApply ?? false;

    private ModelPickerCloseReason _lastCloseReason;
    public ModelPickerCloseReason LastCloseReason
    {
        get => _lastCloseReason;
        private set => SetProperty(ref _lastCloseReason, value);
    }

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!SetProperty(ref _isOpen, value))
            {
                return;
            }

            if (_isOpen)
            {
                LastCloseReason = ModelPickerCloseReason.Unknown;
                _ = OpenAsync(_invocation);
            }
        }
    }

    public IAsyncRelayCommand ApplyCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public ModelOrApiPickerViewModel(
        IPickerDefinitionRegistry registry,
        IServiceProvider services,
        IAiModelStore modelStore,
        IAiModelDownloadQueue downloads)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));

        DownloadProgressList = new DownloadProgressListViewModel(downloads ?? throw new ArgumentNullException(nameof(downloads)));

        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        CancelCommand = new RelayCommand(() =>
        {
            LastCloseReason = ModelPickerCloseReason.Canceled;
            IsOpen = false;
        });
    }

    public void SetInvocation(ModelPickerInvocation invocation)
    {
        _invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
        OnPropertyChanged(nameof(PrimaryActionText));
    }

    public IReadOnlyList<PickerSelectedModel> GetSelectedModelsSnapshot()
        => SelectedModels.ToArray();

    private async Task OpenAsync(ModelPickerInvocation invocation)
    {
        try
        {
            await RefreshCategoriesAsync(invocation, CancellationToken.None).ConfigureAwait(false);

            var selection = await _modelStore.GetSelectionAsync(CancellationToken.None).ConfigureAwait(false);
            var categoryId = selection?.RuntimeId switch
            {
                null => null,
                "local-foundry" => "local-models",
                "openai" => "openai",
                "anthropic" => "anthropic",
                "azure-ai-foundry" => "azure-ai-foundry",
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                var match = Categories.FirstOrDefault(c => string.Equals(c.Id, categoryId, StringComparison.Ordinal));
                if (match is not null)
                {
                    SelectedCategory = match;
                }
            }
        }
        catch
        {
            // ignore
        }

        if (SelectedCategory is null)
        {
            SelectedCategory = Categories.FirstOrDefault();
        }

        await RefreshActiveAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void UpdateActiveCategory()
    {
        ActiveCategory = SelectedCategory is null ? null : GetPaneForCategoryId(SelectedCategory.Id);

        ApplyCommand.NotifyCanExecuteChanged();

        if (IsOpen)
        {
            _ = RefreshActiveAsync(CancellationToken.None);
        }
    }

    private async Task RefreshCategoriesAsync(ModelPickerInvocation invocation, CancellationToken ct)
    {
        var available = await _registry.GetAvailableAsync(invocation, ct).ConfigureAwait(false);

        Categories.Clear();
        _definitionsById.Clear();

        foreach (var def in _registry.GetAll())
        {
            if (string.IsNullOrWhiteSpace(def.Id))
            {
                continue;
            }

            _definitionsById[def.Id] = def;
        }

        foreach (var d in available)
        {
            if (!HasPaneForCategoryId(d.Id))
            {
                continue;
            }

            Categories.Add(new ModelPickerCategoryItem(
                Id: d.Id,
                DisplayName: d.DisplayName,
                IconSymbol: GetIconSymbolForCategoryId(d.Id),
                IconUri: d.IconUri));
        }

        if (Categories.Count == 0)
        {
            ActiveCategory = null;
            SelectedCategory = null;
            ApplyCommand.NotifyCanExecuteChanged();
            return;
        }

        if (SelectedCategory is null || Categories.All(c => !string.Equals(c.Id, SelectedCategory.Id, StringComparison.Ordinal)))
        {
            SelectedCategory = Categories.FirstOrDefault();
        }
    }

    private PickerCategoryViewModel? GetPaneForCategoryId(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return null;
        }

        if (_paneCacheById.TryGetValue(categoryId, out var cached))
        {
            return cached;
        }

        if (!_definitionsById.TryGetValue(categoryId, out var def))
        {
            return null;
        }

        var paneType = def.PaneViewModelType;
        if (paneType is null || !typeof(PickerCategoryViewModel).IsAssignableFrom(paneType))
        {
            return null;
        }

        var pane = _services.GetService(paneType) as PickerCategoryViewModel;
        if (pane is null)
        {
            return null;
        }

        _paneCacheById[categoryId] = pane;
        return pane;
    }

    private bool HasPaneForCategoryId(string categoryId)
    {
        if (_definitionsById.TryGetValue(categoryId, out var def))
        {
            var t = def.PaneViewModelType;
            return t is not null && typeof(PickerCategoryViewModel).IsAssignableFrom(t);
        }

        // Fallback for first RefreshCategoriesAsync call ordering.
        // If a definition exists and can produce a pane, we'll accept it.
        var def2 = _registry.GetAll().FirstOrDefault(d => string.Equals(d.Id, categoryId, StringComparison.Ordinal));
        if (def2 is null)
        {
            return false;
        }

        var paneType = def2.PaneViewModelType;
        return paneType is not null && typeof(PickerCategoryViewModel).IsAssignableFrom(paneType);
    }

    private static Symbol GetIconSymbolForCategoryId(string categoryId)
    {
        return categoryId switch
        {
            "winai" => Symbol.World,
            "local-models" => Symbol.Library,
            "onnx" => Symbol.Document,
            "ollama" => Symbol.Sync,
            "openai" => Symbol.Message,
            "lemonade" => Symbol.Emoji,
            "anthropic" => Symbol.Contact,
            "azure-ai-foundry" => Symbol.Setting,
            _ => Symbol.Tag,
        };
    }

    private async Task RefreshActiveAsync(CancellationToken ct)
    {
        var active = ActiveCategory;
        if (active is null)
        {
            SelectedModels.Clear();
            SelectedModelChips.Clear();
            return;
        }

        try
        {
            await active.RefreshAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        OnPropertyChanged(nameof(FooterSummary));
        OnPropertyChanged(nameof(CanApply));
        RefreshSelectedModelsFromActive();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void OnActiveCategoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshSelectedModelsFromActive();
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSelectedModelsFromActive()
    {
        var active = ActiveCategory;
        var selected = active?.GetSelectedModels() ?? Array.Empty<PickerSelectedModel>();

        SelectedModels.Clear();
        SelectedModelChips.Clear();
        foreach (var s in selected)
        {
            SelectedModels.Add(s);
            SelectedModelChips.Add(new SelectedModelChipViewModel(s, RemoveSelectedModel));
        }
    }

    private async Task ApplyAsync()
    {
        var active = ActiveCategory;
        if (active is null)
        {
            LastCloseReason = ModelPickerCloseReason.Confirmed;
            IsOpen = false;
            return;
        }

        try
        {
            // Phase 5.3 (gap report section 2.3): if invoked in a context where selections
            // should not be persisted, avoid mutating the global selection store.
            if (_invocation.PersistSelection)
            {
                await active.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore
        }

        RefreshSelectedModelsFromActive();

        LastCloseReason = ModelPickerCloseReason.Confirmed;
        IsOpen = false;
    }

    public IRelayCommand<PickerSelectedModel> RemoveSelectedModelCommand => _removeSelectedModelCommand ??= new RelayCommand<PickerSelectedModel>(RemoveSelectedModel);
    private IRelayCommand<PickerSelectedModel>? _removeSelectedModelCommand;

    private void RemoveSelectedModel(PickerSelectedModel? model)
    {
        if (model is null)
        {
            return;
        }

        var active = ActiveCategory;
        if (active is null)
        {
            return;
        }

        active.RemoveSelectedModel(model);
        RefreshSelectedModelsFromActive();
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }
}
