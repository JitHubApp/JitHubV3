namespace JitHubV3.Services.Ai;

public sealed class DefaultAiRuntimeDescriptorCatalog : IAiRuntimeDescriptorCatalog
{
    private static readonly IReadOnlyList<AiRuntimeDescriptorInfo> Declared = new[]
    {
        new AiRuntimeDescriptorInfo(
            RuntimeId: "openai",
            DisplayName: "OpenAI",
            RequiresApiKey: true,
            RequiresEndpoint: false,
            SupportsLocalDownloads: false,
            Description: "OpenAI Chat Completions (API key)"),

        new AiRuntimeDescriptorInfo(
            RuntimeId: "anthropic",
            DisplayName: "Anthropic",
            RequiresApiKey: true,
            RequiresEndpoint: false,
            SupportsLocalDownloads: false,
            Description: "Anthropic Messages API (API key)"),

        new AiRuntimeDescriptorInfo(
            RuntimeId: "azure-ai-foundry",
            DisplayName: "Azure AI Foundry",
            RequiresApiKey: true,
            RequiresEndpoint: true,
            SupportsLocalDownloads: false,
            Description: "OpenAI-compatible chat completions endpoint (API key)"),

        new AiRuntimeDescriptorInfo(
            RuntimeId: "local-foundry",
            DisplayName: "Local Foundry",
            RequiresApiKey: false,
            RequiresEndpoint: false,
            SupportsLocalDownloads: true,
            Description: "Local models via Foundry CLI (downloads and offline execution)")
    };

    public Task<IReadOnlyList<AiRuntimeDescriptorInfo>> GetDeclaredRuntimesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Declared);
    }
}
