using JitHub.GitHub.Abstractions.Security;
using JitHubV3.Services.Ai;
using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class AnthropicPickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "AnthropicTemplate";

    private readonly IAiRuntimeSettingsStore _settingsStore;
    private readonly ISecretStore _secrets;
    private readonly IAiModelStore _modelStore;
    private readonly AnthropicRuntimeConfig _baseConfig;

    private string? _modelId;
    public string? ModelId
    {
        get => _modelId;
        set
        {
            if (!SetProperty(ref _modelId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(FooterSummary));
        }
    }

    private string? _apiKey;
    public string? ApiKey
    {
        get => _apiKey;
        set
        {
            if (!SetProperty(ref _apiKey, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(FooterSummary));
        }
    }

    private bool _hasStoredApiKey;
    public bool HasStoredApiKey
    {
        get => _hasStoredApiKey;
        private set
        {
            if (!SetProperty(ref _hasStoredApiKey, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(FooterSummary));
        }
    }

    public AnthropicPickerViewModel(
        IAiRuntimeSettingsStore settingsStore,
        ISecretStore secrets,
        IAiModelStore modelStore,
        AnthropicRuntimeConfig baseConfig)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _baseConfig = baseConfig ?? throw new ArgumentNullException(nameof(baseConfig));
    }

    public override string FooterSummary
    {
        get
        {
            var model = (ModelId ?? string.Empty).Trim();
            if (model.Length == 0)
            {
                return "No model selected";
            }

            return HasApiKey() ? $"Selected: Anthropic · {model}" : $"Selected: Anthropic · {model} (API key required)";
        }
    }

    public override bool CanApply
    {
        get
        {
            var model = (ModelId ?? string.Empty).Trim();
            return model.Length > 0 && HasApiKey();
        }
    }

    public override async Task RefreshAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var settings = await _settingsStore.GetAsync(ct).ConfigureAwait(false);
        var effective = AiRuntimeEffectiveConfiguration.GetEffective(_baseConfig, settings);

        var modelId = effective.ModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            var selection = await _modelStore.GetSelectionAsync(ct).ConfigureAwait(false);
            if (selection is not null && string.Equals(selection.RuntimeId, "anthropic", StringComparison.OrdinalIgnoreCase))
            {
                modelId = selection.ModelId;
            }
        }

        ModelId = modelId;

        var stored = await _secrets.GetAsync(AiSecretKeys.AnthropicApiKey, ct).ConfigureAwait(false);
        HasStoredApiKey = !string.IsNullOrWhiteSpace(stored);

        ApiKey = null;
    }

    public override async Task ApplyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var model = (ModelId ?? string.Empty).Trim();
        if (model.Length == 0)
        {
            return;
        }

        var settings = await _settingsStore.GetAsync(ct).ConfigureAwait(false);
        var updated = settings with { Anthropic = new AnthropicRuntimeSettings(ModelId: model) };
        await _settingsStore.SetAsync(updated, ct).ConfigureAwait(false);

        var key = (ApiKey ?? string.Empty).Trim();
        if (key.Length > 0)
        {
            await _secrets.SetAsync(AiSecretKeys.AnthropicApiKey, key, ct).ConfigureAwait(false);
            HasStoredApiKey = true;
            ApiKey = null;
        }

        await _modelStore.SetSelectionAsync(new AiModelSelection(RuntimeId: "anthropic", ModelId: model), ct).ConfigureAwait(false);
    }

    private bool HasApiKey()
        => HasStoredApiKey || !string.IsNullOrWhiteSpace(ApiKey);

    public override IReadOnlyList<PickerSelectedModel> GetSelectedModels()
    {
        var model = (ModelId ?? string.Empty).Trim();
        if (model.Length == 0)
        {
            return Array.Empty<PickerSelectedModel>();
        }

        return new[]
        {
            new PickerSelectedModel(
                SlotId: "default",
                RuntimeId: "anthropic",
                ModelId: model,
                DisplayName: model)
        };
    }

    public override void RemoveSelectedModel(PickerSelectedModel model)
    {
        ModelId = null;
    }
}
