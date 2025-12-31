using JitHub.GitHub.Abstractions.Models;

namespace JitHub.GitHub.Octokit.Mapping;

internal sealed record OctokitCodeSearchItemData(
    string Path,
    RepoKey Repo,
    string? Sha,
    string? Url);
