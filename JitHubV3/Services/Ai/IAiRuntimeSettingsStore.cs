namespace JitHubV3.Services.Ai;

public interface IAiRuntimeSettingsStore
{
    ValueTask<AiRuntimeSettings> GetAsync(CancellationToken ct);

    ValueTask SetAsync(AiRuntimeSettings settings, CancellationToken ct);
}

public sealed record AiRuntimeSettings(
    OpenAiRuntimeSettings? OpenAi = null,
    AnthropicRuntimeSettings? Anthropic = null,
    AzureAiFoundryRuntimeSettings? AzureAiFoundry = null);

public sealed record OpenAiRuntimeSettings(
    string? ModelId = null);

public sealed record AnthropicRuntimeSettings(
    string? ModelId = null);

public sealed record AzureAiFoundryRuntimeSettings(
    string? Endpoint = null,
    string? ModelId = null,
    string? ApiKeyHeaderName = null);
