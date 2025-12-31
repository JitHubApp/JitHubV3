namespace JitHub.GitHub.Abstractions.Queries;

public sealed record CodeSearchQuery(
    string Query,
    CodeSortField? Sort = null,
    CodeSortDirection? Direction = null);
