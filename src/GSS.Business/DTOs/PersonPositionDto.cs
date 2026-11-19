namespace GSS.Business.DTOs;

/// <summary>A person's MIS position, departments enriched from the MIS department list.</summary>
public sealed record PersonPositionDto
{
    public int PositionId { get; init; }

    public Guid PositionUuid { get; init; }

    public int RoleId { get; init; }

    public Guid RoleUuid { get; init; }

    public required string RoleName { get; init; }

    public string? RoleShortName { get; init; }

    public string? RoleTypeCode { get; init; }

    public IReadOnlyList<DepartmentDto> Department { get; init; } = [];

    public int EntityId { get; init; }

    public string? EntityName { get; init; }

    public int GeoLevelId { get; init; }

    public int PersonId { get; init; }

    public Guid PersonUuid { get; init; }

    public string? PersonFirstName { get; init; }

    public string? PersonMiddleName { get; init; }

    public string? PersonLastName { get; init; }

    public string? PersonOtherName { get; init; }
}
