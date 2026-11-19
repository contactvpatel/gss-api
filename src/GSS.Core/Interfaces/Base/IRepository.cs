using GSS.Core.Entities;
using GSS.Core.Models;

namespace GSS.Core.Interfaces.Base;

/// <summary>
/// Generic repository. Reads return active rows only; deletes are soft (IsActive = false).
/// </summary>
public interface IRepository<T> where T : EntityBase
{
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Gets all entities, buffered. Unbounded: prefer <see cref="GetPagedAsync"/> on request paths.</summary>
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets active entities by ID, in any order; missing IDs are skipped.</summary>
    Task<IReadOnlyList<T>> FindByIdsAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Inserts the entity and sets its Id and audit fields.</summary>
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Updates the entity; false if it is no longer active (e.g. deleted by a concurrent request).</summary>
    Task<bool> UpdateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the entity; one already deleted stays deleted.</summary>
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Inserts the entities in one round trip (all or none) and sets their Ids and audit fields.</summary>
    Task AddRangeAsync(IReadOnlyList<T> entities, CancellationToken cancellationToken = default);

    /// <summary>Updates the entities in one round trip, all or nothing: false, changing nothing, if any is missing, inactive or repeated.</summary>
    Task<bool> UpdateRangeAsync(IReadOnlyList<T> entities, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the entities in one round trip; rows already deleted are skipped.</summary>
    Task DeleteRangeAsync(IReadOnlyList<T> entities, CancellationToken cancellationToken = default);

    /// <summary>True if the ID exists (optionally counting soft-deleted rows).</summary>
    Task<bool> ExistsAsync(int id, bool includeSoftDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>The given IDs that exist, in any order (optionally counting soft-deleted rows).</summary>
    Task<IReadOnlyList<int>> FindExistingIdsAsync(IReadOnlyList<int> ids, bool includeSoftDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>One page (1-based, OFFSET) by ID; counts every row only when <paramref name="includeTotal"/> is true.</summary>
    Task<PageSlice<T>> GetPagedAsync(int pageNumber, int pageSize, bool includeTotal = false, CancellationToken cancellationToken = default);

    /// <summary>The page after <paramref name="afterId"/> by ID (keyset: cost independent of depth; 0 for the first page).</summary>
    Task<PageSlice<T>> GetPagedAfterAsync(int afterId, int pageSize, bool includeTotal = false, CancellationToken cancellationToken = default);
}
