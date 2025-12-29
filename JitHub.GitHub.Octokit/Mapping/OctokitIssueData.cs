namespace JitHub.GitHub.Octokit.Mapping;

public sealed record OctokitIssueData(
    long Id,
    int Number,
    string Title,
    string? State,
    string? AuthorLogin,
    int CommentCount,
    DateTimeOffset? UpdatedAt);
