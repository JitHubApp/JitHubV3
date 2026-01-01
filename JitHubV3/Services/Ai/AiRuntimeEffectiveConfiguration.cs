namespace JitHubV3.Services.Ai;

public static class AiRuntimeEffectiveConfiguration
{
    public static OpenAiRuntimeConfig GetEffective(OpenAiRuntimeConfig baseConfig, AiRuntimeSettings settings)
    {
        var overrideModelId = settings.OpenAi?.ModelId;
        return baseConfig with
        {
            ModelId = string.IsNullOrWhiteSpace(overrideModelId) ? baseConfig.ModelId : overrideModelId,
        };
    }

    public static AnthropicRuntimeConfig GetEffective(AnthropicRuntimeConfig baseConfig, AiRuntimeSettings settings)
    {
        var overrideModelId = settings.Anthropic?.ModelId;
        return baseConfig with
        {
            ModelId = string.IsNullOrWhiteSpace(overrideModelId) ? baseConfig.ModelId : overrideModelId,
        };
    }

    public static AzureAiFoundryRuntimeConfig GetEffective(AzureAiFoundryRuntimeConfig baseConfig, AiRuntimeSettings settings)
    {
        var s = settings.AzureAiFoundry;

        var endpoint = string.IsNullOrWhiteSpace(s?.Endpoint) ? baseConfig.Endpoint : s!.Endpoint;
        var modelId = string.IsNullOrWhiteSpace(s?.ModelId) ? baseConfig.ModelId : s!.ModelId;
        var header = string.IsNullOrWhiteSpace(s?.ApiKeyHeaderName) ? baseConfig.ApiKeyHeaderName : s!.ApiKeyHeaderName;

        return baseConfig with
        {
            Endpoint = endpoint,
            ModelId = modelId,
            ApiKeyHeaderName = header,
        };
    }
}
