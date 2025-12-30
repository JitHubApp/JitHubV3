using JitHub.GitHub.Abstractions.Queries;

namespace JitHub.GitHub.Abstractions.Queries;

public sealed record IssueSearchQuery(
    string Query,
    IssueSortField? Sort = null,
    IssueSortDirection? Direction = null);
