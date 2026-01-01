using JitHubV3.Presentation.ComposeSearch;

namespace JitHubV3.Services.Ai;

/// <summary>
/// Local Foundry runtime provider.
///
/// Behavior:
/// - If a `foundry` executable is present on PATH or FOUNDRY_HOME is set, this runtime will attempt
///   to call it to list models and (optionally) run a model. If the executable is not available,
///   it falls back to a lightweight local heuristic that builds a constrained GitHub search query.
///
/// This implementation keeps outputs constrained and uses existing validator utilities before returning.
/// </summary>
public sealed class LocalFoundryRuntime : IAiRuntime
{
    private readonly IAiModelStore _modelStore;
    private readonly ILocalFoundryClient _foundry;

    public string RuntimeId => "local-foundry";

    public LocalFoundryRuntime(IAiModelStore modelStore, ILocalFoundryClient foundry)
    {
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _foundry = foundry ?? throw new ArgumentNullException(nameof(foundry));
    }

    public async Task<AiGitHubQueryPlan?> BuildGitHubQueryPlanAsync(AiGitHubQueryBuildRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        ct.ThrowIfCancellationRequested();

        // Try to run Foundry locally if available and a model is selected.
        // If no model is selected, fall back to the heuristic (we don't auto-select silently).
        if (_foundry.IsAvailable())
        {
            var selected = await _modelStore.GetSelectionAsync(ct).ConfigureAwait(false);
            var modelId = selected is not null && string.Equals(selected.RuntimeId, RuntimeId, StringComparison.OrdinalIgnoreCase)
                ? selected.ModelId
                : null;

            if (!string.IsNullOrWhiteSpace(modelId))
            {
                var result = await _foundry.TryBuildQueryPlanJsonAsync(modelId, request.Input ?? string.Empty, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result)
                    && AiJsonUtilities.TryDeserializeFirstJsonObject<AiGitHubQueryPlanCandidate>(result, out var candidate)
                    && candidate is not null)
                {
                    return AiGitHubQueryPlanValidator.Validate(candidate);
                }
            }
        }

        // Fallback to a local lightweight heuristic converter
        var plan = BuildLocalHeuristicPlan(request.Input ?? string.Empty, request.AllowedDomains);
        return plan;
    }

    private static AiGitHubQueryPlan BuildLocalHeuristicPlan(string input, IReadOnlyList<ComposeSearchDomain>? allowed)
    {
        // Very small deterministic heuristic to map plain text -> basic GitHub search query
        // - detect requested domain by keywords
        // - detect language tokens ("Python","C#","TypeScript") and prepend language: qualifier
        // This is intentionally conservative and returns a single-domain plan when possible.

        var lower = input?.ToLowerInvariant() ?? string.Empty;

        var domains = new List<ComposeSearchDomain>();
        if (allowed is not null && allowed.Count > 0)
        {
            domains.AddRange(allowed);
        }
        else
        {
            if (lower.Contains("issue") || lower.Contains("pr") || lower.Contains("pull request")) domains.Add(ComposeSearchDomain.IssuesAndPullRequests);
            if (lower.Contains("repo") || lower.Contains("repository")) domains.Add(ComposeSearchDomain.Repositories);
            if (lower.Contains("code") || lower.Contains("function") || lower.Contains("class")) domains.Add(ComposeSearchDomain.Code);
            if (lower.Contains("user") || lower.Contains("author")) domains.Add(ComposeSearchDomain.Users);
        }

        if (domains.Count == 0) domains.Add(ComposeSearchDomain.Repositories);

        var languagePrefix = string.Empty;
        if (lower.Contains("python")) languagePrefix = "language:Python ";
        else if (lower.Contains("c#") || lower.Contains("csharp")) languagePrefix = "language:C# ";
        else if (lower.Contains("typescript")) languagePrefix = "language:TypeScript ";
        else if (lower.Contains("javascript") || lower.Contains("js ")) languagePrefix = "language:JavaScript ";

        // Strip out polite phrases to keep query compact
        var queryCore = lower;
        queryCore = queryCore.Replace("find", "").Replace("show", "").Replace("me", "").Replace("repos", "").Replace("repositories", "");
        queryCore = queryCore.Trim();
        if (queryCore.Length > 120) queryCore = queryCore.Substring(0, 120);

        var query = (languagePrefix + queryCore).Trim();
        if (string.IsNullOrWhiteSpace(query)) query = "language:Python stars:>10";

        var explanation = "Local heuristic: lightweight conversion when Foundry not available.";
        return new AiGitHubQueryPlan(query, domains, explanation);
    }
}
