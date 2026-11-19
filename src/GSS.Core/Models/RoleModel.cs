using System.Text.Json.Serialization;

namespace GSS.Core.Models;

/// <summary>
/// Model representing a role from the MIS service. JSON names are MIS's wire names.
/// </summary>
public class RoleModel
{
    /// <summary>
    /// Role identifier.
    /// </summary>
    [JsonPropertyName("roleId")]
    public int Id { get; set; }

    /// <summary>
    /// Role name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Department identifier this role belongs to.
    /// </summary>
    /// <remarks>Not populated by MIS (it sends departments as a <c>dept</c> array).</remarks>
    public int DepartmentId { get; set; }

    /// <summary>
    /// Role type identifier.
    /// </summary>
    /// <remarks>Not populated by MIS.</remarks>
    public int RoleTypeId { get; set; }

    /// <summary>
    /// Division identifier.
    /// </summary>
    [JsonPropertyName("divId")]
    public int DivisionId { get; set; }
}
