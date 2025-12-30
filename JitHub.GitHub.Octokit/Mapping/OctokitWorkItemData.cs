using JitHub.GitHub.Abstractions.Models;

namespace JitHub.GitHub.Octokit.Mapping;

internal sealed record OctokitWorkItemData(
    long Id,
    RepoKey Repo,
    int Number,
    string Title,
    bool IsPullRequest,
    string? State,
    string? AuthorLogin,
    int CommentCount,
    DateTimeOffset? UpdatedAt);
