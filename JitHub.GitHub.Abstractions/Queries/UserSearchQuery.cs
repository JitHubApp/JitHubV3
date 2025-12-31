namespace JitHub.GitHub.Abstractions.Queries;

public sealed record UserSearchQuery(
    string Query,
    UserSortField? Sort = null,
    UserSortDirection? Direction = null);
