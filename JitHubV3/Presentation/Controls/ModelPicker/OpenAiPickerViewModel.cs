using JitHub.GitHub.Abstractions.Security;
using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class OpenAiPickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "OpenAiTemplate";

    private readonly IAiRuntimeSettingsStore _settingsStore;
    private readonly ISecretStore _secrets;
    private readonly IAiModelStore _modelStore;
    private readonly OpenAiRuntimeConfig _baseConfig;

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

    public OpenAiPickerViewModel(
        IAiRuntimeSettingsStore settingsStore,
        ISecretStore secrets,
        IAiModelStore modelStore,
        OpenAiRuntimeConfig baseConfig)
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

            return HasApiKey() ? $"Selected: OpenAI · {model}" : $"Selected: OpenAI · {model} (API key required)";
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
            if (selection is not null && string.Equals(selection.RuntimeId, "openai", StringComparison.OrdinalIgnoreCase))
            {
                modelId = selection.ModelId;
            }
        }

        ModelId = modelId;

        var stored = await _secrets.GetAsync(AiSecretKeys.OpenAiApiKey, ct).ConfigureAwait(false);
        HasStoredApiKey = !string.IsNullOrWhiteSpace(stored);

        // Do not populate ApiKey with the stored secret.
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

        // Persist model id override.
        var settings = await _settingsStore.GetAsync(ct).ConfigureAwait(false);
        var updated = settings with { OpenAi = new OpenAiRuntimeSettings(ModelId: model) };
        await _settingsStore.SetAsync(updated, ct).ConfigureAwait(false);

        // Persist API key only if user typed one.
        var key = (ApiKey ?? string.Empty).Trim();
        if (key.Length > 0)
        {
            await _secrets.SetAsync(AiSecretKeys.OpenAiApiKey, key, ct).ConfigureAwait(false);
            HasStoredApiKey = true;
            ApiKey = null;
        }

        await _modelStore.SetSelectionAsync(new AiModelSelection(RuntimeId: "openai", ModelId: model), ct).ConfigureAwait(false);
    }

    private bool HasApiKey()
        => HasStoredApiKey || !string.IsNullOrWhiteSpace(ApiKey);
}
