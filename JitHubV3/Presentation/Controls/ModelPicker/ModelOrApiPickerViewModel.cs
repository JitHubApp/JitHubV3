using System.Collections.ObjectModel;
using JitHub.GitHub.Abstractions.Security;
using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class ModelOrApiPickerViewModel : ObservableObject
{
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

    public string FooterSummary => ActiveCategory?.FooterSummary ?? "Selected: (none)";

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
                _ = RefreshActiveAsync(CancellationToken.None);
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
