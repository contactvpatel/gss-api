namespace GSS.Business.DTOs;

/// <summary>
/// Data transfer object representing a position from the MIS service.
/// </summary>
public sealed record PositionDto
{
    /// <summary>
    /// Position identifier.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Position name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Role identifier this position belongs to.
    /// </summary>
    public int RoleId { get; init; }

    /// <summary>
    /// Additional position information.
    /// </summary>
    public string? Description { get; init; }
}

