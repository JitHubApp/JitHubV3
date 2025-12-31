using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JitHub.GitHub.Abstractions.Security;

namespace JitHubV3.Services.Ai;

public sealed class OpenAiRuntime : IAiRuntime
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly IAiModelStore _modelStore;
    private readonly OpenAiRuntimeConfig _config;

    public string RuntimeId => "openai";

    public OpenAiRuntime(HttpClient http, ISecretStore secrets, IAiModelStore modelStore, OpenAiRuntimeConfig config)
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

        var apiKey = await _secrets.GetAsync(AiSecretKeys.OpenAiApiKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var system = AiPromptBuilder.BuildSystemPrompt(request.AllowedDomains);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = modelId,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = request.Input ?? string.Empty },
            },
            ["max_tokens"] = _config.MaxOutputTokens,
            ["temperature"] = _config.Temperature,
        };

        if (_config.UseJsonObjectMode)
        {
            payload["response_format"] = new { type = "json_object" };
        }

        var json = JsonSerializer.Serialize(payload);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _config.ChatCompletionsPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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
