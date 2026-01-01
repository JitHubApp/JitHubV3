using JitHub.GitHub.Abstractions.Security;
using Microsoft.Extensions.Configuration;

namespace JitHubV3.Services.Ai;

public sealed class ConfiguredAiRuntimeCatalog : IAiRuntimeCatalog
{
    private readonly IConfiguration _configuration;
    private readonly ISecretStore _secrets;
    private readonly IAiRuntimeSettingsStore _settingsStore;

    public ConfiguredAiRuntimeCatalog(IConfiguration configuration, ISecretStore secrets, IAiRuntimeSettingsStore settingsStore)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<IReadOnlyList<AiRuntimeDescriptor>> GetAvailableRuntimesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var settings = await _settingsStore.GetAsync(ct).ConfigureAwait(false);

        var runtimes = new List<AiRuntimeDescriptor>();

        var openAi = AiRuntimeEffectiveConfiguration.GetEffective(OpenAiRuntimeConfig.FromConfiguration(_configuration), settings);
        if (!string.IsNullOrWhiteSpace(openAi.ModelId))
        {
            var key = await _secrets.GetAsync(AiSecretKeys.OpenAiApiKey, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(key))
            {
                runtimes.Add(new AiRuntimeDescriptor(
                    RuntimeId: "openai",
                    DisplayName: "OpenAI",
                    RequiresApiKey: true,
                    Description: "OpenAI Chat Completions (API key)"));
            }
        }

        var anthropic = AiRuntimeEffectiveConfiguration.GetEffective(AnthropicRuntimeConfig.FromConfiguration(_configuration), settings);
        if (!string.IsNullOrWhiteSpace(anthropic.ModelId))
        {
            var key = await _secrets.GetAsync(AiSecretKeys.AnthropicApiKey, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(key))
            {
                runtimes.Add(new AiRuntimeDescriptor(
                    RuntimeId: "anthropic",
                    DisplayName: "Anthropic",
                    RequiresApiKey: true,
                    Description: "Anthropic Messages API (API key)"));
            }
        }

        var foundry = AiRuntimeEffectiveConfiguration.GetEffective(AzureAiFoundryRuntimeConfig.FromConfiguration(_configuration), settings);
        if (!string.IsNullOrWhiteSpace(foundry.Endpoint) && !string.IsNullOrWhiteSpace(foundry.ModelId))
        {
            var key = await _secrets.GetAsync(AiSecretKeys.AzureAiFoundryApiKey, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(key))
            {
                runtimes.Add(new AiRuntimeDescriptor(
                    RuntimeId: "azure-ai-foundry",
                    DisplayName: "Azure AI Foundry",
                    RequiresApiKey: true,
                    Description: "OpenAI-compatible chat completions endpoint (API key)"));
            }
        }

        return runtimes;
    }
}
