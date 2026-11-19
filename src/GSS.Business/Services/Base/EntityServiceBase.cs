using System.Collections;
using System.Reflection;
using GSS.Business.Helpers;
using GSS.Business.Models;
using GSS.Business.Services.Interfaces;
using GSS.Core.Entities;
using GSS.Core.Helpers;
using GSS.Core.Interfaces.Base;
using GSS.Core.Models;
using Mapster;
using Microsoft.Extensions.Logging;

namespace GSS.Business.Services.Base;

/// <summary>
/// Standard CRUD service. The CRUD methods are not virtual: subclasses add rules through
/// <see cref="ValidateCreateAsync"/>, <see cref="ValidateUpdateAsync"/>, <see cref="ValidateDeleteAsync"/> and
/// <see cref="OnCreating"/>, which every
/// single and batch path runs, and add entity-specific queries. Update DTOs reach entities only through the Mapster
/// map (PUT, PATCH and batch update alike).
/// </summary>
public abstract class EntityServiceBase<TEntity, TDto, TCreateDto, TUpdateDto>(
    IRepository<TEntity> repository,
    ILogger logger) : IEntityService<TDto, TCreateDto, TUpdateDto>
    where TEntity : EntityBase
{
    // Compared before and after an update to tell whether anything changed.
    private static readonly PropertyInfo[] EntityProperties = typeof(TEntity)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
        .ToArray();

    /// <summary>Entity repository.</summary>
    protected IRepository<TEntity> Repository { get; } = repository;

    /// <summary>Service logger.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>Entity name used in logs and error messages.</summary>
    protected abstract string EntityName { get; }

    /// <summary>Plural entity name for batch NotFound messages.</summary>
    protected virtual string EntityNamePlural => EntityName + "s";

    /// <summary>
    /// Gets an entity by ID.
    /// </summary>
    public async Task<Result<TDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return entity == null ? Result<TDto>.NotFound() : Result<TDto>.Success(entity.Adapt<TDto>());
    }

    /// <summary>
    /// Gets all entities.
    /// </summary>
    public async Task<IReadOnlyList<TDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return entities.Adapt<IReadOnlyList<TDto>>();
    }

    /// <summary>
    /// Gets one page (1-based, OFFSET); the total only when <paramref name="includeTotal"/> is true.
    /// </summary>
    public async Task<PageSlice<TDto>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        bool includeTotal = false,
        CancellationToken cancellationToken = default)
    {
        var page = await Repository.GetPagedAsync(pageNumber, pageSize, includeTotal, cancellationToken).ConfigureAwait(false);
        return ToDtos(page);
    }

    /// <summary>
    /// Gets the page after <paramref name="afterId"/> (keyset; 0 for the first page); the total only when
    /// <paramref name="includeTotal"/> is true.
    /// </summary>
    public async Task<PageSlice<TDto>> GetPagedAfterAsync(
        int afterId,
        int pageSize,
        bool includeTotal = false,
        CancellationToken cancellationToken = default)
    {
        var page = await Repository.GetPagedAfterAsync(afterId, pageSize, includeTotal, cancellationToken).ConfigureAwait(false);
        return ToDtos(page);
    }

    private static PageSlice<TDto> ToDtos(PageSlice<TEntity> page)
    {
        return new PageSlice<TDto>(page.Items.Adapt<IReadOnlyList<TDto>>(), page.NextAfterId, page.TotalCount);
    }

    /// <summary>
    /// Creates an entity, or returns the <see cref="ValidateCreateAsync"/> violation.
    /// </summary>
    public async Task<Result<TDto>> CreateAsync(TCreateDto createDto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createDto);

        var violation = await ValidateCreateAsync([createDto], cancellationToken).ConfigureAwait(false);
        if (violation != null)
        {
            return violation.ToResult<TDto>();
        }

        var entity = NewEntity(createDto);
        await Repository.AddAsync(entity, cancellationToken).ConfigureAwait(false);

        Logger.Created(EntityName, entity.Id);
        return Result<TDto>.Success(entity.Adapt<TDto>());
    }

    /// <summary>
    /// Business rules for creates, run by single and batch create (a single create is a batch of one) before anything
    /// is written: a violation fails the whole request, null allows it. Override for rules such as unique values or
    /// referenced rows existing; check the whole batch in as few queries as possible.
    /// </summary>
    protected virtual Task<RuleViolation?> ValidateCreateAsync(IReadOnlyList<TCreateDto> createDtos, CancellationToken cancellationToken)
    {
        return Task.FromResult<RuleViolation?>(null);
    }

    /// <summary>
    /// Sets server-owned fields (e.g. a timestamp) on a new entity, after mapping and before insert. Runs for single and
    /// batch create.
    /// </summary>
    protected virtual void OnCreating(TEntity entity, TCreateDto createDto)
    {
    }

    private TEntity NewEntity(TCreateDto createDto)
    {
        var entity = createDto.Adapt<TEntity>()!;
        OnCreating(entity, createDto);
        return entity;
    }

    /// <summary>
    /// Updates an entity through the Mapster map (null fields keep the current value), or returns the
    /// <see cref="ValidateUpdateAsync"/> violation. Writes only when a value changed.
    /// </summary>
    public async Task<Result<TDto>> UpdateAsync(int id, TUpdateDto updateDto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateDto);

        return await ChangeAsync(id, updateDto, "updated", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Partially updates an entity: the same merge as <see cref="UpdateAsync"/> (omitted or null fields keep the
    /// current value), so PUT and PATCH cannot drift apart.
    /// </summary>
    public async Task<Result<TDto>> PatchAsync(int id, TUpdateDto patchDto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patchDto);

        return await ChangeAsync(id, patchDto, "patched", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads the entity once, validates, applies the change and writes it when anything changed.</summary>
    private async Task<Result<TDto>> ChangeAsync(
        int id,
        TUpdateDto updateDto,
        string change,
        CancellationToken cancellationToken)
    {
        var entity = await Repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity == null)
        {
            return Result<TDto>.NotFound();
        }

        var violation = await ValidateUpdateAsync([(entity, updateDto)], cancellationToken).ConfigureAwait(false);
        if (violation != null)
        {
            return violation.ToResult<TDto>();
        }

        if (ApplyUpdate(entity, updateDto))
        {
            if (!await Repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false))
            {
                // Deleted by a concurrent request after it was read: answered as if the delete came first.
                Logger.DeletedConcurrently(EntityName, id);
                return Result<TDto>.NotFound();
            }

            Logger.Changed(EntityName, id, change);
        }

        return Result<TDto>.Success(entity.Adapt<TDto>());
    }

    /// <summary>
    /// Business rules for updates, run by update, patch and batch update (a single change is a batch of one) after
    /// every entity is loaded and before any is changed: a violation fails the whole request, null allows it. Null DTO
    /// fields keep the current value. Override for rules such as referenced rows existing.
    /// </summary>
    protected virtual Task<RuleViolation?> ValidateUpdateAsync(
        IReadOnlyList<(TEntity Current, TUpdateDto UpdateDto)> updates,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<RuleViolation?>(null);
    }

    /// <summary>
    /// The one way an update DTO reaches an entity (update, patch, batch update): the Mapster map, null fields kept.
    /// True when any entity value changed, so an update that changes nothing writes nothing and keeps the audit stamps.
    /// </summary>
    private static bool ApplyUpdate(TEntity entity, TUpdateDto updateDto)
    {
        object?[] before = ReadValues(entity);
        updateDto.Adapt(entity);
        object?[] after = ReadValues(entity);

        for (int i = 0; i < before.Length; i++)
        {
            if (!StructuralComparisons.StructuralEqualityComparer.Equals(before[i], after[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static object?[] ReadValues(TEntity entity)
    {
        object?[] values = new object?[EntityProperties.Length];
        for (int i = 0; i < EntityProperties.Length; i++)
        {
            values[i] = EntityProperties[i].GetValue(entity);
        }

        return values;
    }

    /// <summary>
    /// Soft-deletes: Success (the ID) if deleted or already deleted, NotFound if it never existed, or the
    /// <see cref="ValidateDeleteAsync"/> violation.
    /// </summary>
    public async Task<Result<int>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        // One round trip when live; ExistsAsync only on a miss, to tell "already deleted" from "never existed".
        var entity = await Repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity == null)
        {
            bool everExisted = await Repository.ExistsAsync(id, includeSoftDeleted: true, cancellationToken).ConfigureAwait(false);
            if (!everExisted)
            {
                return Result<int>.NotFound();
            }

            Logger.AlreadyDeleted(EntityName, id);
            return Result<int>.Success(id);
        }

        var violation = await ValidateDeleteAsync([entity], cancellationToken).ConfigureAwait(false);
        if (violation != null)
        {
            return violation.ToResult<int>();
        }

        await Repository.DeleteAsync(entity, cancellationToken).ConfigureAwait(false);
        Logger.Deleted(EntityName, id);
        return Result<int>.Success(id);
    }

    /// <summary>
    /// Business rules for deletes, run by single and batch delete (a single delete is a batch of one) on the active
    /// entities, before any is deleted: a violation fails the whole request, null allows it. Already-deleted entities
    /// are not passed (deleting them again is a no-op). Override for rules such as "no active children".
    /// </summary>
    protected virtual Task<RuleViolation?> ValidateDeleteAsync(IReadOnlyList<TEntity> entities, CancellationToken cancellationToken)
    {
        return Task.FromResult<RuleViolation?>(null);
    }

    /// <summary>Creates entities in one transaction (the caller's Unit of Work, if open), or returns the <see cref="ValidateCreateAsync"/> violation.</summary>
    public async Task<Result<IReadOnlyList<TDto>>> CreateBatchAsync(
        IReadOnlyList<TCreateDto> createDtos,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createDtos);

        if (createDtos.Count == 0)
        {
            return Result<IReadOnlyList<TDto>>.Success(Array.Empty<TDto>());
        }

        if (createDtos.Count > BatchLimits.MaxBatchSize)
        {
            return Result<IReadOnlyList<TDto>>.Failure(BatchLimits.TooLarge(createDtos.Count));
        }

        var violation = await ValidateCreateAsync(createDtos, cancellationToken).ConfigureAwait(false);
        if (violation != null)
        {
            return violation.ToResult<IReadOnlyList<TDto>>();
        }


        List<TEntity> entities = createDtos.Select(NewEntity).ToList();

        await Repository.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);

        Logger.BatchCreated(EntityName, entities.Count);
        return Result<IReadOnlyList<TDto>>.Success(entities.Adapt<IReadOnlyList<TDto>>());
    }

    /// <summary>
    /// Updates entities in one transaction (the caller's Unit of Work, if open), all or nothing: Failure for duplicate IDs, NotFound naming every missing
    /// (or soft-deleted) ID, or the <see cref="ValidateUpdateAsync"/> violation.
    /// </summary>
    public async Task<Result<IReadOnlyList<TDto>>> UpdateBatchAsync(
        IReadOnlyList<(int Id, TUpdateDto UpdateDto)> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return Result<IReadOnlyList<TDto>>.Success(Array.Empty<TDto>());
        }

        if (updates.Count > BatchLimits.MaxBatchSize)
        {
            return Result<IReadOnlyList<TDto>>.Failure(BatchLimits.TooLarge(updates.Count));
        }

        int[] duplicateIds = updates.GroupBy(u => u.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicateIds.Length > 0)
        {
            return Result<IReadOnlyList<TDto>>.Failure($"Duplicate {EntityName} IDs in batch: {string.Join(", ", duplicateIds)}.");
        }


        IReadOnlyList<int> ids = updates.Select(u => u.Id).ToList();
        var entities = await Repository.FindByIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        Dictionary<int, TEntity> entityLookup = entities.ToDictionary(e => e.Id);

        int[] missingIds = ids.Where(id => !entityLookup.ContainsKey(id)).ToArray();
        if (missingIds.Length > 0)
        {
            return Result<IReadOnlyList<TDto>>.NotFound($"{EntityNamePlural} not found. IDs: {string.Join(", ", missingIds)}.");
        }

        IReadOnlyList<(TEntity Current, TUpdateDto UpdateDto)> pairs = updates.Select(u => (entityLookup[u.Id], u.UpdateDto)).ToList();
        var violation = await ValidateUpdateAsync(pairs, cancellationToken).ConfigureAwait(false);
        if (violation != null)
        {
            return violation.ToResult<IReadOnlyList<TDto>>();
        }

        List<TEntity> changedEntities = new(pairs.Count);
        foreach ((var entity, var updateDto) in pairs)
        {
            if (ApplyUpdate(entity, updateDto))
            {
                changedEntities.Add(entity);
            }
        }

        if (changedEntities.Count > 0
            && !await Repository.UpdateRangeAsync(changedEntities, cancellationToken).ConfigureAwait(false))
        {
            return await ConcurrentlyDeletedAsync(changedEntities, cancellationToken).ConfigureAwait(false);
        }

        Logger.BatchUpdated(EntityName, pairs.Count, changedEntities.Count);
        return Result<IReadOnlyList<TDto>>.Success(pairs.Select(p => p.Current).ToList().Adapt<IReadOnlyList<TDto>>());
    }

    /// <summary>
    /// A batch update wrote nothing because rows were deleted by a concurrent request after they were read: NotFound
    /// naming them (one ID-only query), as if the delete came first.
    /// </summary>
    private async Task<Result<IReadOnlyList<TDto>>> ConcurrentlyDeletedAsync(
        IReadOnlyList<TEntity> changedEntities,
        CancellationToken cancellationToken)
    {
        int[] changedIds = [.. changedEntities.Select(e => e.Id)];
        var stillActive = await Repository.FindExistingIdsAsync(changedIds, includeSoftDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        int[] goneIds = [.. changedIds.Except(stillActive)];
        Logger.BatchDeletedConcurrently(EntityName, goneIds.Length);

        // None gone: they were deleted and restored meanwhile, which the API can't do; ask for a retry anyway.
        return goneIds.Length > 0
            ? Result<IReadOnlyList<TDto>>.NotFound($"{EntityNamePlural} not found. IDs: {string.Join(", ", goneIds)}.")
            : Result<IReadOnlyList<TDto>>.Conflict($"{EntityNamePlural} were changed by a concurrent request. Please try again.");
    }

    /// <summary>
    /// Soft-deletes entities in one transaction (the caller's Unit of Work, if open), all or nothing: Failure for duplicate IDs, NotFound naming every ID
    /// that never existed, or the <see cref="ValidateDeleteAsync"/> violation. Already-deleted IDs count as deleted (as in <see cref="DeleteAsync"/>), so a retry succeeds.
    /// Returns the requested IDs.
    /// </summary>
    public async Task<Result<IReadOnlyList<int>>> DeleteBatchAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return Result<IReadOnlyList<int>>.Success(Array.Empty<int>());
        }

        if (ids.Count > BatchLimits.MaxBatchSize)
        {
            return Result<IReadOnlyList<int>>.Failure(BatchLimits.TooLarge(ids.Count));
        }

        int[] duplicateIds = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicateIds.Length > 0)
        {
            return Result<IReadOnlyList<int>>.Failure($"Duplicate {EntityName} IDs in batch: {string.Join(", ", duplicateIds)}.");
        }


        var entities = await Repository.FindByIdsAsync(ids, cancellationToken).ConfigureAwait(false);

        // Not active: already deleted (fine) or never existed (404). One query, only when something is missing.
        HashSet<int> activeIds = entities.Select(e => e.Id).ToHashSet();
        int[] inactiveIds = ids.Where(id => !activeIds.Contains(id)).ToArray();
        if (inactiveIds.Length > 0)
        {
            var everExisted = await Repository
                .FindExistingIdsAsync(inactiveIds, includeSoftDeleted: true, cancellationToken)
                .ConfigureAwait(false);
            int[] missingIds = inactiveIds.Except(everExisted).ToArray();
            if (missingIds.Length > 0)
            {
                return Result<IReadOnlyList<int>>.NotFound($"{EntityNamePlural} not found. IDs: {string.Join(", ", missingIds)}.");
            }
        }

        if (entities.Count > 0)
        {
            var violation = await ValidateDeleteAsync(entities, cancellationToken).ConfigureAwait(false);
            if (violation != null)
            {
                return violation.ToResult<IReadOnlyList<int>>();
            }

            await Repository.DeleteRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        }

        Logger.BatchDeleted(EntityName, ids.Count, inactiveIds.Length);
        return Result<IReadOnlyList<int>>.Success(ids.ToArray());
    }

    /// <summary>
    /// The distinct <paramref name="ids"/> with no active row in <paramref name="repository"/> (one query for the IDs
    /// only, never the rows and their personal data; none when <paramref name="ids"/> is empty). For "referenced rows
    /// exist" rules.
    /// </summary>
    protected static async Task<int[]> FindMissingAsync<TReferenced>(
        IRepository<TReferenced> repository,
        IEnumerable<int> ids,
        CancellationToken cancellationToken)
        where TReferenced : EntityBase
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(ids);

        int[] distinct = ids.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            return [];
        }

        var existing = await repository.FindExistingIdsAsync(distinct, includeSoftDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        return distinct.Except(existing).ToArray();
    }
}
