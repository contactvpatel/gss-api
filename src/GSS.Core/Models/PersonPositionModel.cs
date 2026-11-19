using System.Text.Json.Serialization;

namespace GSS.Core.Models;

/// <summary>A person's MIS position. JSON names are MIS's wire names.</summary>
public class PersonPositionModel
{
    [JsonPropertyName("positionId")]
    public int PositionId { get; set; }

    [JsonPropertyName("positionUUID")]
    public Guid PositionUuid { get; set; }

    [JsonPropertyName("roleId")]
    public int RoleId { get; set; }

    [JsonPropertyName("roleUUID")]
    public Guid RoleUuid { get; set; }

    [JsonPropertyName("name")]
    public string RoleName { get; set; } = string.Empty;

    [JsonPropertyName("shortName")]
    public string? RoleShortName { get; set; }

    [JsonPropertyName("roleType")]
    public string? RoleTypeCode { get; set; }

    /// <summary>Departments of the position. MIS may omit it.</summary>
    [JsonPropertyName("dept")]
    public IReadOnlyList<DepartmentModel>? Department { get; set; }

    [JsonPropertyName("entityId")]
    public int EntityId { get; set; }

    [JsonPropertyName("entityName")]
    public string? EntityName { get; set; }

    [JsonPropertyName("geoLevelId")]
    public int GeoLevelId { get; set; }

    [JsonPropertyName("personId")]
    public int PersonId { get; set; }

    [JsonPropertyName("personUUID")]
    public Guid PersonUuid { get; set; }

    [JsonPropertyName("personFName")]
    public string? PersonFirstName { get; set; }

    [JsonPropertyName("personMName")]
    public string? PersonMiddleName { get; set; }

    [JsonPropertyName("personLName")]
    public string? PersonLastName { get; set; }

    [JsonPropertyName("personOName")]
    public string? PersonOtherName { get; set; }
}
