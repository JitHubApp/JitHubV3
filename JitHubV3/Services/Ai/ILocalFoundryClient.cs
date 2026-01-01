namespace JitHubV3.Services.Ai;

public sealed record LocalFoundryModel(
    string ModelId,
    string? DisplayName = null);

/// <summary>
/// Best-effort client for a locally installed Foundry CLI/runtime.
///
/// This is intentionally minimal and resilient: failures should not crash the app.
/// </summary>
public interface ILocalFoundryClient
{
    bool IsAvailable();

    ValueTask<IReadOnlyList<LocalFoundryModel>> ListModelsAsync(CancellationToken ct);

    /// <summary>
    /// Executes the constrained "GitHub query builder" capability on a specific local model.
    /// Returns raw text that should contain a JSON object compatible with <see cref="AiGitHubQueryPlanCandidate"/>.
    /// </summary>
    ValueTask<string?> TryBuildQueryPlanJsonAsync(string modelId, string input, CancellationToken ct);
}
