using System.Collections.ObjectModel;
using JitHub.GitHub.Abstractions.Security;
using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class ModelOrApiPickerViewModel : ObservableObject
{
    private readonly IAiModelStore _modelStore;
    private readonly LocalModelsPickerViewModel _localModels;
    private readonly OpenAiPickerViewModel _openAi;
    private readonly AnthropicPickerViewModel _anthropic;
    private readonly AzureAiFoundryPickerViewModel _foundry;

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
            if (!SetProperty(ref _activeCategory, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FooterSummary));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public string FooterSummary => ActiveCategory?.FooterSummary ?? "No model selected";

    public bool CanApply => ActiveCategory?.CanApply ?? false;

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
                _ = OpenAsync();
            }
        }
    }

    public IAsyncRelayCommand ApplyCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public ModelOrApiPickerViewModel(
        IAiLocalModelCatalog localCatalog,
        IAiModelDownloadQueue downloads,
        IAiModelStore modelStore,
        IReadOnlyList<AiLocalModelDefinition> localDefinitions,
        IAiRuntimeSettingsStore settingsStore,
        ISecretStore secrets,
        OpenAiRuntimeConfig openAi,
        AnthropicRuntimeConfig anthropic,
        AzureAiFoundryRuntimeConfig foundry)
    {
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _localModels = new LocalModelsPickerViewModel(localCatalog, downloads, modelStore, localDefinitions);
        _openAi = new OpenAiPickerViewModel(settingsStore, secrets, modelStore, openAi);
        _anthropic = new AnthropicPickerViewModel(settingsStore, secrets, modelStore, anthropic);
        _foundry = new AzureAiFoundryPickerViewModel(settingsStore, secrets, modelStore, foundry);

        Categories.Add(new ModelPickerCategoryItem(Id: "local-models", DisplayName: "Local models"));
        Categories.Add(new ModelPickerCategoryItem(Id: "openai", DisplayName: "OpenAI"));
        Categories.Add(new ModelPickerCategoryItem(Id: "anthropic", DisplayName: "Anthropic"));
        Categories.Add(new ModelPickerCategoryItem(Id: "azure-ai-foundry", DisplayName: "Azure AI Foundry"));

        SelectedCategory = Categories.FirstOrDefault();
        UpdateActiveCategory();

        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        CancelCommand = new RelayCommand(() => IsOpen = false);
    }

    private async Task OpenAsync()
    {
        try
        {
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

        await RefreshActiveAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void UpdateActiveCategory()
    {
        ActiveCategory = SelectedCategory?.Id switch
        {
            "local-models" => _localModels,
            "openai" => _openAi,
            "anthropic" => _anthropic,
            "azure-ai-foundry" => _foundry,
            _ => null,
        };

        ApplyCommand.NotifyCanExecuteChanged();

        if (IsOpen)
        {
            _ = RefreshActiveAsync(CancellationToken.None);
        }
    }

    private async Task RefreshActiveAsync(CancellationToken ct)
    {
        var active = ActiveCategory;
        if (active is null)
        {
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
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyAsync()
    {
        var active = ActiveCategory;
        if (active is null)
        {
            IsOpen = false;
            return;
        }

        try
        {
            await active.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        IsOpen = false;
    }
}
