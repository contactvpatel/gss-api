using GSS.Core.Models;

namespace GSS.Core.Interfaces.Services;

/// <summary>
/// MIS HTTP client (departments, roles, positions). Business wraps it with caching.
/// </summary>
public interface IMisService
{
    /// <summary>Gets every MIS department (all divisions).</summary>
    Task<IReadOnlyList<DepartmentModel>> GetDepartmentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets role types for a division.</summary>
    Task<IReadOnlyList<RoleTypeModel>> GetAllRoleTypesAsync(int divisionId, CancellationToken cancellationToken = default);

    /// <summary>Gets roles for a division.</summary>
    Task<IReadOnlyList<RoleModel>> GetAllRolesAsync(int divisionId, CancellationToken cancellationToken = default);

    /// <summary>Gets a role, or null.</summary>
    Task<RoleModel?> GetRoleByIdAsync(int roleId, CancellationToken cancellationToken = default);

    /// <summary>Gets roles for a department.</summary>
    Task<IReadOnlyList<RoleModel>> GetRolesByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default);

    /// <summary>Gets positions for a role.</summary>
    Task<IReadOnlyList<PositionModel>> GetPositionsByRoleIdAsync(int roleId, CancellationToken cancellationToken = default);

    /// <summary>Gets a person's positions.</summary>
    Task<IReadOnlyList<PersonPositionModel>> GetPersonPositionsAsync(string personId, CancellationToken cancellationToken = default);
}
