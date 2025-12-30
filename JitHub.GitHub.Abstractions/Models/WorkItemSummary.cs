namespace JitHub.GitHub.Abstractions.Models;

public sealed record WorkItemSummary(
    long Id,
    RepoKey Repo,
    int Number,
    string Title,
    bool IsPullRequest,
    string? State,
    string? AuthorLogin,
    int CommentCount,
    DateTimeOffset? UpdatedAt);
