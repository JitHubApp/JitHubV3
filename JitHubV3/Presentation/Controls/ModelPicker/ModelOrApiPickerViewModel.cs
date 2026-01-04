using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<ModelOrApiPickerViewModel> _logger;

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

            _logger.LogInformation(
                "ModelPicker: IsOpen changed to {IsOpen} (Thread={ThreadId})",
                _isOpen,
                Environment.CurrentManagedThreadId);

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
        IAiModelDownloadQueue downloads,
        ILogger<ModelOrApiPickerViewModel>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _logger = logger ?? NullLogger<ModelOrApiPickerViewModel>.Instance;

        DownloadProgressList = new DownloadProgressListViewModel(downloads ?? throw new ArgumentNullException(nameof(downloads)));

        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        CancelCommand = new RelayCommand(() =>
        {
            _logger.LogInformation("ModelPicker: Cancel clicked (Thread={ThreadId})", Environment.CurrentManagedThreadId);
            LastCloseReason = ModelPickerCloseReason.Canceled;
            IsOpen = false;
        });
    }

    public void SetInvocation(ModelPickerInvocation invocation)
    {
        _invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
        _logger.LogInformation(
            "ModelPicker: Invocation set (PrimaryAction={PrimaryAction}, Slots={SlotsCount}, PersistSelection={PersistSelection})",
            _invocation.PrimaryAction,
            _invocation.Slots.Count,
            _invocation.PersistSelection);
        OnPropertyChanged(nameof(PrimaryActionText));
    }

    public IReadOnlyList<PickerSelectedModel> GetSelectedModelsSnapshot()
        => SelectedModels.ToArray();

    private async Task OpenAsync(ModelPickerInvocation invocation)
    {
        _logger.LogInformation(
            "ModelPicker: OpenAsync start (PrimaryAction={PrimaryAction}, Slots={SlotsCount}, PersistSelection={PersistSelection}, Thread={ThreadId})",
            invocation.PrimaryAction,
            invocation.Slots.Count,
            invocation.PersistSelection,
            Environment.CurrentManagedThreadId);

        try
        {
            await RefreshCategoriesAsync(invocation, CancellationToken.None);

            _logger.LogInformation(
                "ModelPicker: Categories refreshed (Count={Count}, Thread={ThreadId})",
                Categories.Count,
                Environment.CurrentManagedThreadId);

            var selection = await _modelStore.GetSelectionAsync(CancellationToken.None);
            _logger.LogInformation(
                "ModelPicker: Current selection (RuntimeId={RuntimeId}, ModelId={ModelId})",
                selection?.RuntimeId,
                selection?.ModelId);
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
                    _logger.LogInformation("ModelPicker: Selecting category from selection mapping (CategoryId={CategoryId})", categoryId);
                    SelectedCategory = match;
                }
                else
                {
                    _logger.LogWarning(
                        "ModelPicker: Selection mapped to CategoryId={CategoryId} but not found in Categories (Available={Available})",
                        categoryId,
                        string.Join(",", Categories.Select(c => c.Id)));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModelPicker: OpenAsync failed; picker may render empty");
        }

        if (SelectedCategory is null)
        {
            _logger.LogWarning(
                "ModelPicker: SelectedCategory still null; defaulting to first (CategoriesCount={Count})",
                Categories.Count);
            SelectedCategory = Categories.FirstOrDefault();
        }

        await RefreshActiveAsync(CancellationToken.None);

        _logger.LogInformation(
            "ModelPicker: OpenAsync end (SelectedCategory={SelectedCategory}, ActiveCategoryType={ActiveType})",
            SelectedCategory?.Id,
            ActiveCategory?.GetType().Name);
    }

    private void UpdateActiveCategory()
    {
        ActiveCategory = SelectedCategory is null ? null : GetPaneForCategoryId(SelectedCategory.Id);

        _logger.LogInformation(
            "ModelPicker: UpdateActiveCategory (SelectedCategory={SelectedId}, ActiveType={ActiveType})",
            SelectedCategory?.Id,
            ActiveCategory?.GetType().Name);

        ApplyCommand.NotifyCanExecuteChanged();

        if (IsOpen)
        {
            _ = RefreshActiveAsync(CancellationToken.None);
        }
    }

    private async Task RefreshCategoriesAsync(ModelPickerInvocation invocation, CancellationToken ct)
    {
        _logger.LogInformation(
            "ModelPicker: RefreshCategoriesAsync start (Thread={ThreadId})",
            Environment.CurrentManagedThreadId);

        var available = await _registry.GetAvailableAsync(invocation, ct);

        _logger.LogInformation(
            "ModelPicker: Registry available (Count={Count}, Ids={Ids})",
            available.Count,
            string.Join(",", available.Select(a => a.Id)));

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

        _logger.LogInformation(
            "ModelPicker: Registry total definitions cached (Count={Count}, Ids={Ids})",
            _definitionsById.Count,
            string.Join(",", _definitionsById.Keys));

        foreach (var d in available)
        {
            if (!HasPaneForCategoryId(d.Id))
            {
                _logger.LogWarning(
                    "ModelPicker: Skipping available category {CategoryId} because no pane is registered/resolvable",
                    d.Id);
                continue;
            }

            Categories.Add(new ModelPickerCategoryItem(
                Id: d.Id,
                DisplayName: d.DisplayName,
                IconSymbol: GetIconSymbolForCategoryId(d.Id),
                IconUri: d.IconUri));
        }

        _logger.LogInformation(
            "ModelPicker: Categories built (Count={Count}, Ids={Ids})",
            Categories.Count,
            string.Join(",", Categories.Select(c => c.Id)));

        if (Categories.Count == 0)
        {
            _logger.LogError(
                "ModelPicker: No categories available; UI will be empty (Invocation PrimaryAction={PrimaryAction})",
                invocation.PrimaryAction);
            ActiveCategory = null;
            SelectedCategory = null;
            ApplyCommand.NotifyCanExecuteChanged();
            return;
        }

        if (SelectedCategory is null || Categories.All(c => !string.Equals(c.Id, SelectedCategory.Id, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "ModelPicker: SelectedCategory was missing/invalid; selecting first category (First={First})",
                Categories.FirstOrDefault()?.Id);
            SelectedCategory = Categories.FirstOrDefault();
        }
    }

    private PickerCategoryViewModel? GetPaneForCategoryId(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            _logger.LogWarning("ModelPicker: GetPaneForCategoryId called with blank id");
            return null;
        }

        if (_paneCacheById.TryGetValue(categoryId, out var cached))
        {
            _logger.LogInformation("ModelPicker: Reusing cached pane for {CategoryId} ({Type})", categoryId, cached.GetType().Name);
            return cached;
        }

        if (!_definitionsById.TryGetValue(categoryId, out var def))
        {
            _logger.LogWarning(
                "ModelPicker: No definition found for CategoryId={CategoryId} (Known={Known})",
                categoryId,
                string.Join(",", _definitionsById.Keys));
            return null;
        }

        var paneType = def.PaneViewModelType;
        if (paneType is null || !typeof(PickerCategoryViewModel).IsAssignableFrom(paneType))
        {
            _logger.LogWarning(
                "ModelPicker: Definition {CategoryId} has invalid PaneViewModelType={PaneType}",
                categoryId,
                paneType?.FullName);
            return null;
        }

        var pane = _services.GetService(paneType) as PickerCategoryViewModel;
        if (pane is null)
        {
            _logger.LogError(
                "ModelPicker: Failed to resolve pane instance for CategoryId={CategoryId} Type={PaneType}",
                categoryId,
                paneType.FullName);
            return null;
        }

        _logger.LogInformation(
            "ModelPicker: Created pane for CategoryId={CategoryId} Type={PaneType}",
            categoryId,
            paneType.Name);

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
            _logger.LogWarning(
                "ModelPicker: RefreshActiveAsync with null ActiveCategory (Thread={ThreadId})",
                Environment.CurrentManagedThreadId);
            SelectedModels.Clear();
            SelectedModelChips.Clear();
            return;
        }

        try
        {
            _logger.LogInformation("ModelPicker: Refreshing active pane ({PaneType})", active.GetType().Name);
            await active.RefreshAsync(ct);
            _logger.LogInformation("ModelPicker: Active pane refreshed ({PaneType})", active.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModelPicker: Active pane refresh failed ({PaneType})", active.GetType().Name);
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
            _logger.LogWarning("ModelPicker: ApplyAsync with null ActiveCategory; closing");
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
                _logger.LogInformation("ModelPicker: Applying active pane ({PaneType})", active.GetType().Name);
                await active.ApplyAsync(CancellationToken.None);
                _logger.LogInformation("ModelPicker: Apply complete ({PaneType})", active.GetType().Name);
            }
            else
            {
                _logger.LogInformation("ModelPicker: PersistSelection=false; skipping ApplyAsync for pane {PaneType}", active.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModelPicker: Apply failed ({PaneType})", active.GetType().Name);
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
