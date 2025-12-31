using JitHubV3.Presentation.ComposeSearch;

namespace JitHubV3.Services.Ai;

public sealed record AiRuntimeDescriptor(
    string RuntimeId,
    string DisplayName,
    bool RequiresApiKey,
    string? Description = null);

public sealed record AiModelSelection(
    string RuntimeId,
    string ModelId);

public sealed record AiGitHubQueryBuildRequest(
    string Input,
    IReadOnlyList<ComposeSearchDomain>? AllowedDomains = null);

public sealed record AiGitHubQueryPlan(
    string Query,
    IReadOnlyList<ComposeSearchDomain> Domains,
    string? Explanation = null);

public sealed record AiGitHubQueryPlanCandidate(
    string? Query,
    string? Domain = null,
    IReadOnlyList<string>? Domains = null,
    string? Explanation = null);
