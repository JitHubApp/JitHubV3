using JitHub.GitHub.Abstractions.Models;

namespace JitHubV3.Presentation;

public sealed record IssueConversationRouteData(
    RepoKey Repo,
    int IssueNumber,
    string DisplayName);
