namespace JitHubV3.Services.Ai;

public interface IAiRuntime
{
    string RuntimeId { get; }

    /// <summary>
    /// Constrained capability: translate natural language into a GitHub search query + domain selection.
    /// Implementations must treat output as untrusted and validate it before returning.
    /// </summary>
    Task<AiGitHubQueryPlan?> BuildGitHubQueryPlanAsync(AiGitHubQueryBuildRequest request, CancellationToken ct);
}
