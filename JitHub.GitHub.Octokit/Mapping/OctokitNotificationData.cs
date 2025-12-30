using JitHub.GitHub.Abstractions.Models;

namespace JitHub.GitHub.Octokit.Mapping;

internal sealed record OctokitNotificationData(
    string Id,
    RepoKey Repo,
    string Title,
    string? Type,
    DateTimeOffset? UpdatedAt,
    bool Unread);
