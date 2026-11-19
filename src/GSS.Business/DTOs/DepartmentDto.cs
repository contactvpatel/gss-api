namespace GSS.Business.DTOs;

/// <summary>MIS department.</summary>
public sealed record DepartmentDto
{
    public int DepartmentId { get; init; }

    public Guid DepartmentUuid { get; init; }

    public required string DepartmentName { get; init; }

    public required string DepartmentCode { get; init; }

    public int DivisionId { get; init; }

    public string? Wing { get; init; }

    public bool IsSatsangActivityDepartment { get; init; }

    public bool IsAdministrationDepartment { get; init; }
}
