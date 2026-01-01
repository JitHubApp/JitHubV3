using System.Text;
using System.Text.Json;
using JitHub.GitHub.Abstractions.Security;

namespace JitHubV3.Services.Ai;

/// <summary>
/// Minimal API-key runtime for Azure AI Foundry hosted models.
///
/// This implementation assumes an OpenAI-compatible chat completions endpoint ("/v1/chat/completions")
/// and authenticates via a configurable API key header (default: "api-key").
/// </summary>
public sealed class AzureAiFoundryRuntime : IAiRuntime
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly IAiModelStore _modelStore;
    private readonly IAiRuntimeSettingsStore _settingsStore;
    private readonly AzureAiFoundryRuntimeConfig _config;

    public string RuntimeId => "azure-ai-foundry";

    public AzureAiFoundryRuntime(
        HttpClient http,
        ISecretStore secrets,
        IAiModelStore modelStore,
        IAiRuntimeSettingsStore settingsStore,
        AzureAiFoundryRuntimeConfig config)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<AiGitHubQueryPlan?> BuildGitHubQueryPlanAsync(AiGitHubQueryBuildRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ct.ThrowIfCancellationRequested();

        var settings = await _settingsStore.GetAsync(ct).ConfigureAwait(false);
        var cfg = AiRuntimeEffectiveConfiguration.GetEffective(_config, settings);

        var modelId = await TryGetSelectedModelIdAsync(ct).ConfigureAwait(false) ?? cfg.ModelId;
        if (string.IsNullOrWhiteSpace(cfg.Endpoint) || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var apiKey = await _secrets.GetAsync(AiSecretKeys.AzureAiFoundryApiKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var system = AiPromptBuilder.BuildSystemPrompt(request.AllowedDomains);

        var payload = new
        {
            model = modelId,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = request.Input ?? string.Empty },
            },
            max_tokens = cfg.MaxOutputTokens,
            temperature = cfg.Temperature,
            response_format = new { type = "json_object" },
        };

        var json = JsonSerializer.Serialize(payload);

        var uri = new Uri(new Uri(cfg.Endpoint), cfg.ChatCompletionsPath);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        httpRequest.Headers.TryAddWithoutValidation(cfg.ApiKeyHeaderName, apiKey);

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var content = TryExtractOpenAiMessageContent(body);
        if (!AiJsonUtilities.TryDeserializeFirstJsonObject<AiGitHubQueryPlanCandidate>(content, out var candidate) || candidate is null)
        {
            return null;
        }

        return AiGitHubQueryPlanValidator.Validate(candidate);
    }

    private async ValueTask<string?> TryGetSelectedModelIdAsync(CancellationToken ct)
    {
        var selection = await _modelStore.GetSelectionAsync(ct).ConfigureAwait(false);
        if (selection is null)
        {
            return null;
        }

        return string.Equals(selection.RuntimeId, RuntimeId, StringComparison.OrdinalIgnoreCase)
            ? selection.ModelId
            : null;
    }

    private static string? TryExtractOpenAiMessageContent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (choices.GetArrayLength() == 0)
            {
                return null;
            }

            var choice0 = choices[0];
            if (choice0.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
            {
                return content.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
