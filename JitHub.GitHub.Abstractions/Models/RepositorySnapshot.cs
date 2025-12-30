namespace JitHub.GitHub.Abstractions.Models;

public sealed record RepositorySnapshot(
    RepoKey Repo,
    bool IsPrivate,
    string? DefaultBranch,
    string? Description,
    DateTimeOffset? UpdatedAt,
    int StargazersCount,
    int ForksCount,
    int WatchersCount);
