using JitHub.GitHub.Abstractions.Models;

namespace JitHubV3.Presentation;

public sealed record RepoRouteData(
    RepoKey Repo,
    string DisplayName);
