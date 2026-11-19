namespace GSS.Business.DTOs;

/// <summary>
/// Data transfer object representing application security information from the ASM service.
/// </summary>
public sealed record AsmResponseDto
{
    /// <summary>Identifier of the role in the organization.</summary>
    public int RoleId { get; init; }

    /// <summary>Identifier of the position in the organization.</summary>
    public int PositionId { get; init; }

    /// <summary>Applications and actions the user can use for this role/position (e.g. view, create per application).</summary>
    public IReadOnlyList<ApplicationAccessDto> ApplicationAccess { get; init; } = new List<ApplicationAccessDto>();
}
