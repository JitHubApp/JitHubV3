namespace JitHub.GitHub.Octokit.Mapping;

public sealed record OctokitIssueDetailData(
    long Id,
    int Number,
    string Title,
    string? State,
    string? AuthorLogin,
    string? Body,
    int CommentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
