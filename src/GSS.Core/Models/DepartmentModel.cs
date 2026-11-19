using System.Text.Json.Serialization;

namespace GSS.Core.Models;

/// <summary>MIS department. JSON names are MIS's wire names.</summary>
public class DepartmentModel
{
    [JsonPropertyName("deptId")]
    public int DepartmentId { get; set; }

    [JsonPropertyName("deptUUID")]
    public Guid DepartmentUuid { get; set; }

    [JsonPropertyName("name")]
    public string DepartmentName { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string DepartmentCode { get; set; } = string.Empty;

    [JsonPropertyName("divId")]
    public int DivisionId { get; set; }

    [JsonPropertyName("wing")]
    public string? Wing { get; set; }

    [JsonPropertyName("isSatsangActivityDept")]
    public bool IsSatsangActivityDepartment { get; set; }

    [JsonPropertyName("isAdministrationDept")]
    public bool IsAdministrationDepartment { get; set; }
}
