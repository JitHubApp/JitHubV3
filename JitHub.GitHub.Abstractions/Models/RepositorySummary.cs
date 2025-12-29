namespace JitHub.GitHub.Abstractions.Models;

public sealed record RepositorySummary(
    long Id,
    string Name,
    string OwnerLogin,
    bool IsPrivate,
    string? DefaultBranch,
    string? Description,
    DateTimeOffset? UpdatedAt);
