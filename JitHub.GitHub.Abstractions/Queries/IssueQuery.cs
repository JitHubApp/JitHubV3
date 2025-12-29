namespace JitHub.GitHub.Abstractions.Queries;

public sealed record IssueQuery(
    IssueStateFilter State,
    string? SearchText = null);
