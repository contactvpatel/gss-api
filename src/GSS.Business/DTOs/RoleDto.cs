namespace GSS.Business.DTOs;

/// <summary>
/// Data transfer object representing a role from the MIS service.
/// </summary>
public sealed record RoleDto
{
    /// <summary>
    /// Role identifier.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Role name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Department identifier this role belongs to.
    /// </summary>
    public int DepartmentId { get; init; }

    /// <summary>
    /// Role type identifier.
    /// </summary>
    public int RoleTypeId { get; init; }

    /// <summary>
    /// Division identifier.
    /// </summary>
    public int DivisionId { get; init; }
}

