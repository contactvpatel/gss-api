namespace GSS.Core.Models;

/// <summary>
/// One page of rows ordered by ID. <see cref="NextAfterId"/> is the last row's ID when more rows follow (the keyset
/// cursor for the next page), else null. <see cref="TotalCount"/> is set only when the caller asked for it.
/// </summary>
public sealed record PageSlice<T>(IReadOnlyList<T> Items, int? NextAfterId, int? TotalCount)
{
    /// <summary>True when rows follow this page.</summary>
    public bool HasMore => NextAfterId.HasValue;
}
