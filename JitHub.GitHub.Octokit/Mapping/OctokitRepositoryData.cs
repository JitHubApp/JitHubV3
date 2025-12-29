namespace JitHub.GitHub.Octokit.Mapping;

public sealed record OctokitRepositoryData(
    long Id,
    string Name,
    string? OwnerLogin,
    bool IsPrivate,
    string? DefaultBranch,
    string? Description,
    DateTimeOffset? UpdatedAt);
