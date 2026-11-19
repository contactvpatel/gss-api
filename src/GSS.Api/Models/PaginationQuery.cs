namespace GSS.Api.Models;

/// <summary>
/// List paging. Send pageSize (then each response's nextCursor as cursor) for cursor paging, page for numbered pages,
/// or neither for all items.
/// </summary>
public class PaginationQuery
{
    /// <summary>Items per page when pageSize is omitted.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Page number (1-based) for numbered pages; omit for cursor paging. Deep pages are slower: prefer the cursor.</summary>
    public int Page { get; set; }

    /// <summary>Items per page (1-100, default 20).</summary>
    public int? PageSize { get; set; }

    /// <summary>The nextCursor of the previous page; omit for the first page. Not combined with page.</summary>
    public string? Cursor { get; set; }

    /// <summary>Also return the total item count. Slower: it counts every item.</summary>
    public bool IncludeTotal { get; set; }

    /// <summary>Numbered pages: page is set.</summary>
    public bool IsPageNumbered => Page > 0;

    /// <summary>Cursor paging: no page, and a cursor or page size.</summary>
    public bool IsCursorPaged => !IsPageNumbered && (Cursor != null || PageSize != null);

    /// <summary>The page size to use.</summary>
    public int EffectivePageSize => PageSize ?? DefaultPageSize;
}
