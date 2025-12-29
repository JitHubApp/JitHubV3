namespace JitHub.GitHub.Abstractions.Paging;

public readonly record struct PageRequest
{
    private PageRequest(int pageSize, int? pageNumber, string? cursor)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "PageSize must be greater than 0.");
        }

        if (pageNumber is not null && pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "PageNumber must be greater than 0.");
        }

        PageSize = pageSize;
        PageNumber = pageNumber;
        Cursor = cursor;
    }

    public int PageSize { get; }

    /// <summary>
    /// Optional 1-based page number. Mutually exclusive with <see cref="Cursor" />.
    /// </summary>
    public int? PageNumber { get; }

    /// <summary>
    /// Optional cursor token for cursor-based pagination. Mutually exclusive with <see cref="PageNumber" />.
    /// </summary>
    public string? Cursor { get; }

    public static PageRequest FirstPage(int pageSize) => new(pageSize, pageNumber: 1, cursor: null);

    public static PageRequest FromPageNumber(int pageNumber, int pageSize) => new(pageSize, pageNumber, cursor: null);

    public static PageRequest FromCursor(string? cursor, int pageSize)
    {
        cursor = cursor?.Trim();
        return new(pageSize, pageNumber: null, cursor: string.IsNullOrEmpty(cursor) ? null : cursor);
    }
}
