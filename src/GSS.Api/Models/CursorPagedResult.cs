namespace GSS.Api.Models;

/// <summary>
/// One cursor page. Pass nextCursor as cursor to get the next page; it is null on the last page.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public class CursorPagedResult<T>
{
    /// <summary>Creates a page.</summary>
    public CursorPagedResult(IReadOnlyList<T> items, int pageSize, string? nextCursor, int? totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
        PageSize = pageSize;
        NextCursor = nextCursor;
        TotalCount = totalCount;
    }

    /// <summary>Items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Items per page.</summary>
    public int PageSize { get; }

    /// <summary>Cursor for the next page; null on the last page.</summary>
    public string? NextCursor { get; }

    /// <summary>True when a next page exists.</summary>
    public bool HasMore => NextCursor != null;

    /// <summary>Total items across all pages; null unless includeTotal was requested.</summary>
    public int? TotalCount { get; }
}
