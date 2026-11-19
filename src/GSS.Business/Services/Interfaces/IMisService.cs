using GSS.Business.DTOs;

namespace GSS.Business.Services.Interfaces;

/// <summary>
/// MIS organization data: departments, roles, positions.
/// </summary>
public interface IMisService
{
    /// <summary>
    /// Gets departments for a division.
    /// </summary>
    Task<IReadOnlyList<DepartmentDto>> GetAllDepartmentsAsync(int divisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a department by ID.
    /// </summary>
    Task<DepartmentDto?> GetDepartmentByIdAsync(int departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets role types for a division.
    /// </summary>
    Task<IReadOnlyList<RoleTypeDto>> GetAllRoleTypesAsync(int divisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles for a division.
    /// </summary>
    Task<IReadOnlyList<RoleDto>> GetAllRolesAsync(int divisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a role by ID.
    /// </summary>
    Task<RoleDto?> GetRoleByIdAsync(int roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles for a department.
    /// </summary>
    Task<IReadOnlyList<RoleDto>> GetRolesByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets positions for a role.
    /// </summary>
    Task<IReadOnlyList<PositionDto>> GetPositionsByRoleIdAsync(int roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a person's positions, cached until <paramref name="tokenExpiresAtUtc"/>.
    /// </summary>
    Task<IReadOnlyList<PersonPositionDto>> GetPersonPositionsAsync(string personId, DateTimeOffset? tokenExpiresAtUtc, CancellationToken cancellationToken = default);
}
