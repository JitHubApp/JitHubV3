using JitHub.GitHub.Abstractions.Models;

namespace JitHub.GitHub.Octokit.Mapping;

internal sealed record OctokitActivityEventData(
    string? Id,
    RepoKey? Repo,
    string? Type,
    string? ActorLogin,
    string? Description,
    DateTimeOffset CreatedAt);
