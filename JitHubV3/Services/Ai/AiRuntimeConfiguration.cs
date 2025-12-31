using Microsoft.Extensions.Configuration;

namespace JitHubV3.Services.Ai;

public sealed record OpenAiRuntimeConfig
{
    public string Endpoint { get; init; } = "https://api.openai.com";
    public string ChatCompletionsPath { get; init; } = "/v1/chat/completions";
    public string? ModelId { get; init; }
    public int MaxOutputTokens { get; init; } = 256;
    public double Temperature { get; init; } = 0;
    public bool UseJsonObjectMode { get; init; } = true;

    public static OpenAiRuntimeConfig FromConfiguration(IConfiguration config)
    {
        var section = config.GetSection("Ai:OpenAI");
        return new OpenAiRuntimeConfig
        {
            Endpoint = section["Endpoint"] ?? "https://api.openai.com",
            ChatCompletionsPath = section["ChatCompletionsPath"] ?? "/v1/chat/completions",
            ModelId = section["ModelId"],
            MaxOutputTokens = TryReadInt(section["MaxOutputTokens"], fallback: 256),
            Temperature = TryReadDouble(section["Temperature"], fallback: 0),
            UseJsonObjectMode = TryReadBool(section["UseJsonObjectMode"], fallback: true),
        };
    }

    private static int TryReadInt(string? s, int fallback) => int.TryParse(s, out var v) ? v : fallback;

    private static double TryReadDouble(string? s, double fallback) => double.TryParse(s, out var v) ? v : fallback;

    private static bool TryReadBool(string? s, bool fallback) => bool.TryParse(s, out var v) ? v : fallback;
}

public sealed record AnthropicRuntimeConfig
{
    public string Endpoint { get; init; } = "https://api.anthropic.com";
    public string MessagesPath { get; init; } = "/v1/messages";
    public string? ModelId { get; init; }
    public string AnthropicVersion { get; init; } = "2023-06-01";
    public int MaxOutputTokens { get; init; } = 256;
    public double Temperature { get; init; } = 0;

    public static AnthropicRuntimeConfig FromConfiguration(IConfiguration config)
    {
        var section = config.GetSection("Ai:Anthropic");
        return new AnthropicRuntimeConfig
        {
            Endpoint = section["Endpoint"] ?? "https://api.anthropic.com",
            MessagesPath = section["MessagesPath"] ?? "/v1/messages",
            ModelId = section["ModelId"],
            AnthropicVersion = section["AnthropicVersion"] ?? "2023-06-01",
            MaxOutputTokens = TryReadInt(section["MaxOutputTokens"], fallback: 256),
            Temperature = TryReadDouble(section["Temperature"], fallback: 0),
        };
    }

    private static int TryReadInt(string? s, int fallback) => int.TryParse(s, out var v) ? v : fallback;

    private static double TryReadDouble(string? s, double fallback) => double.TryParse(s, out var v) ? v : fallback;
}

public sealed record AzureAiFoundryRuntimeConfig
{
    public string? Endpoint { get; init; }
    public string ChatCompletionsPath { get; init; } = "/v1/chat/completions";
    public string? ModelId { get; init; }
    public int MaxOutputTokens { get; init; } = 256;
    public double Temperature { get; init; } = 0;

    /// <summary>
    /// Header name to pass the API key as. Many Foundry endpoints accept 'api-key'.
    /// </summary>
    public string ApiKeyHeaderName { get; init; } = "api-key";

    public static AzureAiFoundryRuntimeConfig FromConfiguration(IConfiguration config)
    {
        var section = config.GetSection("Ai:AzureAiFoundry");
        return new AzureAiFoundryRuntimeConfig
        {
            Endpoint = section["Endpoint"],
            ChatCompletionsPath = section["ChatCompletionsPath"] ?? "/v1/chat/completions",
            ModelId = section["ModelId"],
            MaxOutputTokens = TryReadInt(section["MaxOutputTokens"], fallback: 256),
            Temperature = TryReadDouble(section["Temperature"], fallback: 0),
            ApiKeyHeaderName = section["ApiKeyHeaderName"] ?? "api-key",
        };
    }

    private static int TryReadInt(string? s, int fallback) => int.TryParse(s, out var v) ? v : fallback;

    private static double TryReadDouble(string? s, double fallback) => double.TryParse(s, out var v) ? v : fallback;
}
