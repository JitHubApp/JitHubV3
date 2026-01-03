using JitHub.GitHub.Abstractions.Security;
using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class AzureAiFoundryPickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "FoundryTemplate";

    private readonly IAiRuntimeSettingsStore _settingsStore;
    private readonly ISecretStore _secrets;
    private readonly IAiModelStore _modelStore;
    private readonly AzureAiFoundryRuntimeConfig _baseConfig;

    private string? _endpoint;
    public string? Endpoint
    {
        get => _endpoint;
        set
        {
            if (!SetProperty(ref _endpoint, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(FooterSummary));
        }
    }

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

    private string? _apiKeyHeaderName;
    public string? ApiKeyHeaderName
    {
        get => _apiKeyHeaderName;
        set
        {
            if (!SetProperty(ref _apiKeyHeaderName, value))
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

    public AzureAiFoundryPickerViewModel(
        IAiRuntimeSettingsStore settingsStore,
        ISecretStore secrets,
        IAiModelStore modelStore,
        AzureAiFoundryRuntimeConfig baseConfig)
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
            var endpoint = (Endpoint ?? string.Empty).Trim();
            var model = (ModelId ?? string.Empty).Trim();

            if (endpoint.Length == 0 || model.Length == 0)
            {
                return "No model selected";
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                return "Selected: Azure AI Foundry · (invalid endpoint)";
            }

            return HasApiKey()
                ? $"Selected: Azure AI Foundry · {model}"
                : $"Selected: Azure AI Foundry · {model} (API key required)";
        }
    }

    public override bool CanApply
    {
        get
        {
            var endpoint = (Endpoint ?? string.Empty).Trim();
            var model = (ModelId ?? string.Empty).Trim();
            if (endpoint.Length == 0 || model.Length == 0)
            {
                return false;
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                return false;
            }

            return HasApiKey();
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
            if (selection is not null && string.Equals(selection.RuntimeId, "azure-ai-foundry", StringComparison.OrdinalIgnoreCase))
            {
                modelId = selection.ModelId;
            }
        }

        Endpoint = effective.Endpoint;
        ModelId = modelId;
        ApiKeyHeaderName = effective.ApiKeyHeaderName;

        var stored = await _secrets.GetAsync(AiSecretKeys.AzureAiFoundryApiKey, ct).ConfigureAwait(false);
        HasStoredApiKey = !string.IsNullOrWhiteSpace(stored);

        ApiKey = null;
    }

    public override async Task ApplyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var endpoint = (Endpoint ?? string.Empty).Trim();
        var model = (ModelId ?? string.Empty).Trim();
        var header = (ApiKeyHeaderName ?? string.Empty).Trim();

        if (endpoint.Length == 0 || model.Length == 0)
        {
            return;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            return;
        }

        var settings = await _settingsStore.GetAsync(ct).ConfigureAwait(false);
        var updated = settings with
        {
            AzureAiFoundry = new AzureAiFoundryRuntimeSettings(
                Endpoint: endpoint,
                ModelId: model,
                ApiKeyHeaderName: string.IsNullOrWhiteSpace(header) ? null : header)
        };

        await _settingsStore.SetAsync(updated, ct).ConfigureAwait(false);

        var key = (ApiKey ?? string.Empty).Trim();
        if (key.Length > 0)
        {
            await _secrets.SetAsync(AiSecretKeys.AzureAiFoundryApiKey, key, ct).ConfigureAwait(false);
            HasStoredApiKey = true;
            ApiKey = null;
        }

        await _modelStore.SetSelectionAsync(new AiModelSelection(RuntimeId: "azure-ai-foundry", ModelId: model), ct).ConfigureAwait(false);
    }

    private bool HasApiKey()
        => HasStoredApiKey || !string.IsNullOrWhiteSpace(ApiKey);
}
