namespace JitHub.GitHub.Abstractions.Models;

public sealed record ActivitySummary(
    string Id,
    RepoKey? Repo,
    string Type,
    string? ActorLogin,
    string? Description,
    DateTimeOffset CreatedAt);
