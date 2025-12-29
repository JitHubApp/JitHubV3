namespace JitHub.GitHub.Abstractions.Models;

public sealed record IssueDetail(
    long Id,
    int Number,
    string Title,
    IssueState State,
    string? AuthorLogin,
    string? Body,
    int CommentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
