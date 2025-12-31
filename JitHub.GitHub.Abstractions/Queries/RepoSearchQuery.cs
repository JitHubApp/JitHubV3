namespace JitHub.GitHub.Abstractions.Queries;

public sealed record RepoSearchQuery(
    string Query,
    RepoSortField? Sort = null,
    RepoSortDirection? Direction = null);
