using System.Text;
using System.Text.Json;
using JitHub.GitHub.Abstractions.Security;

namespace JitHubV3.Services.Ai;

public sealed class AnthropicRuntime : IAiRuntime
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly IAiModelStore _modelStore;
    private readonly AnthropicRuntimeConfig _config;

    public string RuntimeId => "anthropic";

    public AnthropicRuntime(HttpClient http, ISecretStore secrets, IAiModelStore modelStore, AnthropicRuntimeConfig config)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<AiGitHubQueryPlan?> BuildGitHubQueryPlanAsync(AiGitHubQueryBuildRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ct.ThrowIfCancellationRequested();

        var modelId = await TryGetSelectedModelIdAsync(ct).ConfigureAwait(false) ?? _config.ModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var apiKey = await _secrets.GetAsync(AiSecretKeys.AnthropicApiKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var system = AiPromptBuilder.BuildSystemPrompt(request.AllowedDomains);

        var payload = new
        {
            model = modelId,
            max_tokens = _config.MaxOutputTokens,
            temperature = _config.Temperature,
            system,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = request.Input ?? string.Empty,
                },
            },
        };

        var json = JsonSerializer.Serialize(payload);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _config.MessagesPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        httpRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", _config.AnthropicVersion);

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var content = TryExtractAnthropicText(body);
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

    private static string? TryExtractAnthropicText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) &&
                    string.Equals(type.GetString(), "text", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
