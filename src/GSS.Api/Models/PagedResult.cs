namespace GSS.Api.Models;

/// <summary>
/// One numbered page. The total (and page count) is present only when requested with includeTotal.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public class PagedResult<T>
{
    /// <summary>Creates a page.</summary>
    public PagedResult(IReadOnlyList<T> items, int pageNumber, int pageSize, bool hasNextPage, int? totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
        PageNumber = pageNumber;
        PageSize = pageSize;
        HasNextPage = hasNextPage;
        TotalCount = totalCount;
    }

    /// <summary>Items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Page number (1-based).</summary>
    public int PageNumber { get; }

    /// <summary>Items per page.</summary>
    public int PageSize { get; }

    /// <summary>Total items across all pages; null unless includeTotal was requested.</summary>
    public int? TotalCount { get; }

    /// <summary>Total pages; null unless includeTotal was requested.</summary>
    public int? TotalPages => TotalCount is { } total ? (int)Math.Ceiling(total / (double)PageSize) : null;

    /// <summary>True when a previous page exists.</summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>True when a next page exists.</summary>
    public bool HasNextPage { get; }

    /// <summary>Position of the first item on this page (1-based), or 0 when the page is empty.</summary>
    public int FirstItemIndex => Items.Count == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;

    /// <summary>Position of the last item on this page (1-based), or 0 when the page is empty.</summary>
    public int LastItemIndex => Items.Count == 0 ? 0 : ((PageNumber - 1) * PageSize) + Items.Count;
}
