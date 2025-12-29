namespace JitHub.GitHub.Abstractions.Paging;

public sealed record PagedResult<T>(
    T Items,
    PageRequest? Next);
