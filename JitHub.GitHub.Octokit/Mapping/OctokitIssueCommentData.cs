namespace JitHub.GitHub.Octokit.Mapping;

public sealed record OctokitIssueCommentData(
    long Id,
    string? AuthorLogin,
    string? Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
