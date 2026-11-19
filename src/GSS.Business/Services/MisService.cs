using System.Collections.Immutable;
using Caching.NET;
using Caching.NET.Options;
using GSS.Business.DTOs;
using GSS.Core.Helpers;
using GSS.Core.Models;
using GSS.Util.Security;
using Mapster;

namespace GSS.Business.Services;

/// <summary>
/// Wraps the Core MIS client: maps to DTOs, orders ordinally and caches frozen results.
/// </summary>
public class MisService(
    Core.Interfaces.Services.IMisService coreMisService,
    ICacheService cacheService,
    TimeProvider timeProvider) : Interfaces.IMisService
{

    /// <inheritdoc />
    public async Task<IReadOnlyList<DepartmentDto>> GetAllDepartmentsAsync(int divisionId, CancellationToken cancellationToken = default)
    {
        var departments = await GetDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        return departments.Where(d => d.DivisionId == divisionId).ToImmutableArray();
    }

    /// <inheritdoc />
    public async Task<DepartmentDto?> GetDepartmentByIdAsync(int departmentId, CancellationToken cancellationToken = default)
    {
        var departments = await GetDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        return departments.FirstOrDefault(d => d.DepartmentId == departmentId);
    }

    /// <summary>Every MIS department, cached 24 h, ordered by division then name.</summary>
    private async Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken)
    {
        var departments = await cacheService.GetOrSetAsync(
            CacheKeys.AllDepartments,
            async cancel =>
            {
                var models = await coreMisService.GetDepartmentsAsync(cancel).ConfigureAwait(false);
                // Frozen so the shared L1 entry cannot be mutated through a returned reference.
                return models.Adapt<IReadOnlyList<DepartmentDto>>()
                    .OrderBy(d => d.DivisionId)
                    .ThenBy(d => d.DepartmentName, StringComparer.Ordinal)
                    .ToImmutableArray();
            },
            new CacheEntryOverrides
            {
                DistributedExpiration = TimeSpan.FromHours(24),
                LocalExpiration = TimeSpan.FromHours(24),
                FailSafeMaxDuration = TimeSpan.FromHours(48)
            },
            token: cancellationToken).ConfigureAwait(false);

        return departments.IsDefaultOrEmpty ? Array.Empty<DepartmentDto>() : departments;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleTypeDto>> GetAllRoleTypesAsync(int divisionId, CancellationToken cancellationToken = default)
    {
        var roleTypes = await cacheService.GetOrSetAsync(
            CacheKeys.RoleTypes(divisionId),
            async cancel =>
            {
                var models = await coreMisService.GetAllRoleTypesAsync(divisionId, cancel).ConfigureAwait(false);
                var dtos = models.Adapt<IReadOnlyList<RoleTypeDto>>();
                // Ordinal order, frozen before caching.
                return dtos.OrderBy(x => x.Name, StringComparer.Ordinal).ToImmutableArray();
            },
            new CacheEntryOverrides
            {
                DistributedExpiration = TimeSpan.FromHours(24),
                LocalExpiration = TimeSpan.FromHours(24),
                FailSafeMaxDuration = TimeSpan.FromHours(48)
            },
            token: cancellationToken).ConfigureAwait(false);

        return roleTypes.IsDefaultOrEmpty ? Array.Empty<RoleTypeDto>() : roleTypes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleDto>> GetAllRolesAsync(int divisionId, CancellationToken cancellationToken = default)
    {
        var roles = await cacheService.GetOrSetAsync(
            CacheKeys.Roles(divisionId),
            async cancel =>
            {
                var models = await coreMisService.GetAllRolesAsync(divisionId, cancel).ConfigureAwait(false);
                var dtos = models.Adapt<IReadOnlyList<RoleDto>>();
                // Ordinal order, frozen before caching.
                return dtos.OrderBy(x => x.Name, StringComparer.Ordinal).ToImmutableArray();
            },
            new CacheEntryOverrides
            {
                DistributedExpiration = TimeSpan.FromHours(12),
                LocalExpiration = TimeSpan.FromHours(12),
                FailSafeMaxDuration = TimeSpan.FromHours(24)
            },
            token: cancellationToken).ConfigureAwait(false);

        return roles.IsDefaultOrEmpty ? Array.Empty<RoleDto>() : roles;
    }

    /// <inheritdoc />
    public async Task<RoleDto?> GetRoleByIdAsync(int roleId, CancellationToken cancellationToken = default)
    {
        return await cacheService.GetOrSetAsync<RoleDto?>(
            CacheKeys.RoleById(roleId),
            async (ctx, cancel) =>
            {
                var model = await coreMisService.GetRoleByIdAsync(roleId, cancel).ConfigureAwait(false);
                if (model is null)
                {
                    // A missing role is cached briefly, so a role created in MIS is found within minutes.
                    ctx.Overrides.DistributedExpiration = TimeSpan.FromMinutes(5);
                    ctx.Overrides.LocalExpiration = TimeSpan.FromMinutes(5);
                    ctx.Overrides.FailSafeMaxDuration = TimeSpan.FromMinutes(10);
                    return null;
                }

                return model.Adapt<RoleDto>();
            },
            options: new CacheEntryOverrides
            {
                DistributedExpiration = TimeSpan.FromHours(12),
                LocalExpiration = TimeSpan.FromHours(12),
                FailSafeMaxDuration = TimeSpan.FromHours(24)
            },
            token: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleDto>> GetRolesByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default)
    {
        var roles = await cacheService.GetOrSetAsync(
            CacheKeys.RolesByDepartment(departmentId),
            async cancel =>
            {
                var models = await coreMisService.GetRolesByDepartmentIdAsync(departmentId, cancel).ConfigureAwait(false);
                var dtos = models.Adapt<IReadOnlyList<RoleDto>>();
                // Ordinal order, frozen before caching.
                return dtos.OrderBy(x => x.Name, StringComparer.Ordinal).ToImmutableArray();
            },
            new CacheEntryOverrides
            {
                DistributedExpiration = TimeSpan.FromHours(12),
                LocalExpiration = TimeSpan.FromHours(12),
                FailSafeMaxDuration = TimeSpan.FromHours(24)
            },
            token: cancellationToken).ConfigureAwait(false);

        return roles.IsDefaultOrEmpty ? Array.Empty<RoleDto>() : roles;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PositionDto>> GetPositionsByRoleIdAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var models = await coreMisService.GetPositionsByRoleIdAsync(roleId, cancellationToken).ConfigureAwait(false);
        var dtos = models.Adapt<IReadOnlyList<PositionDto>>();
        return dtos;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonPositionDto>> GetPersonPositionsAsync(
        string personId, DateTimeOffset? tokenExpiresAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personId, nameof(personId));

        // Cached until the caller's token expires; not cached without an expiry.
        var lifetime = JwtTokenHelper.GetRemainingLifetime(tokenExpiresAtUtc, timeProvider.GetUtcNow());
        if (lifetime == null)
        {
            var uncached = await BuildPersonPositionsAsync(personId, cancellationToken).ConfigureAwait(false);
            return uncached.IsDefaultOrEmpty ? Array.Empty<PersonPositionDto>() : uncached;
        }

        var positions = await cacheService.GetOrSetAsync(
            CacheKeys.PersonPositions(personId),
            cancel => BuildPersonPositionsAsync(personId, cancel),
            new CacheEntryOverrides
            {
                DistributedExpiration = lifetime,
                LocalExpiration = lifetime,
                FailSafe = false
            },
            token: cancellationToken).ConfigureAwait(false);

        return positions.IsDefaultOrEmpty ? Array.Empty<PersonPositionDto>() : positions;
    }

    /// <summary>Person positions with departments enriched from the MIS department list.</summary>
    private async Task<ImmutableArray<PersonPositionDto>> BuildPersonPositionsAsync(string personId, CancellationToken cancellationToken)
    {
        var positionsTask = coreMisService.GetPersonPositionsAsync(personId, cancellationToken);
        var departmentsTask = GetDepartmentsAsync(cancellationToken);
        await Task.WhenAll(positionsTask, departmentsTask).ConfigureAwait(false);

        Dictionary<int, DepartmentDto> lookup = (await departmentsTask.ConfigureAwait(false))
            .GroupBy(d => d.DepartmentId)
            .ToDictionary(g => g.Key, g => g.First());

        // MIS "dept" entries are partial (or missing); enrich from the department list.
        var positions = (await positionsTask.ConfigureAwait(false)).Adapt<List<PersonPositionDto>>()
            .Select(position => position with { Department = [.. (position.Department ?? []).Select(d => Enrich(d, lookup))] });

        // Frozen before it enters the cache.
        return positions.OrderBy(p => p.EntityName, StringComparer.Ordinal).ToImmutableArray();
    }

    private static DepartmentDto Enrich(DepartmentDto department, Dictionary<int, DepartmentDto> lookup)
    {
        return lookup.TryGetValue(department.DepartmentId, out var source)
            ? department with
            {
                DepartmentUuid = source.DepartmentUuid,
                DivisionId = source.DivisionId,
                IsSatsangActivityDepartment = source.IsSatsangActivityDepartment,
                IsAdministrationDepartment = source.IsAdministrationDepartment
            }
            : department;
    }
}
