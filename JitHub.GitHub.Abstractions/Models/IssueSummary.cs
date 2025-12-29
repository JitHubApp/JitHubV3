namespace JitHub.GitHub.Abstractions.Models;

public sealed record IssueSummary(
    long Id,
    int Number,
    string Title,
    IssueState State,
    string? AuthorLogin,
    int CommentCount,
    DateTimeOffset? UpdatedAt);
