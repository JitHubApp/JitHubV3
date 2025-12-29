namespace JitHub.GitHub.Abstractions.Models;

public sealed record IssueComment(
    long Id,
    string? AuthorLogin,
    string? Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
