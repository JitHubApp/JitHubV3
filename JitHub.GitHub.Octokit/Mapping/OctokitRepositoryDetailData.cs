using JitHub.GitHub.Abstractions.Models;

namespace JitHub.GitHub.Octokit.Mapping;

internal sealed record OctokitRepositoryDetailData(
    RepoKey Repo,
    bool IsPrivate,
    string? DefaultBranch,
    string? Description,
    DateTimeOffset? UpdatedAt,
    int StargazersCount,
    int ForksCount,
    int WatchersCount);
