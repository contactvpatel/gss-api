using GSS.Business.Models;
using GSS.Core.Models;

namespace GSS.Business.Services.Interfaces;

/// <summary>
/// CRUD operations every entity service has, implemented once by <c>EntityServiceBase</c>. An entity's interface
/// extends this and declares only its own queries. Exposing a method here adds no endpoint: controllers decide the
/// HTTP surface; controllers decide which CRUD methods and query shapes become endpoints.
/// </summary>
/// <typeparam name="TDto">Response DTO.</typeparam>
/// <typeparam name="TCreateDto">Create request DTO.</typeparam>
/// <typeparam name="TUpdateDto">Update and patch request DTO.</typeparam>
public interface IEntityService<TDto, TCreateDto, TUpdateDto>
{
    /// <summary>Gets an entity by ID, or NotFound.</summary>
    Task<Result<TDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Gets all active entities.</summary>
    Task<IReadOnlyList<TDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one page (1-based, OFFSET); the total only when <paramref name="includeTotal"/> is true.</summary>
    Task<PageSlice<TDto>> GetPagedAsync(int pageNumber, int pageSize, bool includeTotal = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the page after <paramref name="afterId"/> (keyset; 0 for the first page); the total only when <paramref name="includeTotal"/> is true.</summary>
    Task<PageSlice<TDto>> GetPagedAfterAsync(int afterId, int pageSize, bool includeTotal = false, CancellationToken cancellationToken = default);

    /// <summary>Creates an entity, or returns the rule violation.</summary>
    Task<Result<TDto>> CreateAsync(TCreateDto createDto, CancellationToken cancellationToken = default);

    /// <summary>Updates an entity (null fields keep the current value), or NotFound or the rule violation.</summary>
    Task<Result<TDto>> UpdateAsync(int id, TUpdateDto updateDto, CancellationToken cancellationToken = default);

    /// <summary>Partially updates an entity: the same merge as <see cref="UpdateAsync"/>.</summary>
    Task<Result<TDto>> PatchAsync(int id, TUpdateDto patchDto, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes an entity: Success (the ID) if deleted or already deleted, NotFound if it never existed, or the rule violation.</summary>
    Task<Result<int>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates entities in one transaction (the caller's Unit of Work, if open), or returns the rule violation.</summary>
    Task<Result<IReadOnlyList<TDto>>> CreateBatchAsync(IReadOnlyList<TCreateDto> createDtos, CancellationToken cancellationToken = default);

    /// <summary>Updates entities in one transaction, all or nothing: Failure for duplicate IDs, NotFound naming missing IDs, or the rule violation.</summary>
    Task<Result<IReadOnlyList<TDto>>> UpdateBatchAsync(IReadOnlyList<(int Id, TUpdateDto UpdateDto)> updates, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes entities in one transaction, all or nothing: Failure for duplicate IDs, NotFound naming IDs that never existed.</summary>
    Task<Result<IReadOnlyList<int>>> DeleteBatchAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
}
