namespace GSS.Business.DTOs;

/// <summary>
/// Data transfer object representing a role type from the MIS service.
/// </summary>
public sealed record RoleTypeDto
{
    /// <summary>
    /// Role type identifier.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Role type name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Division identifier this role type belongs to.
    /// </summary>
    public int DivisionId { get; init; }
}

