using System.Text.Json.Serialization;

namespace GSS.Core.Models;

/// <summary>
/// Model representing a role type from the MIS service. JSON names are MIS's wire names.
/// </summary>
public class RoleTypeModel
{
    /// <summary>
    /// Role type identifier.
    /// </summary>
    [JsonPropertyName("roleTypeId")]
    public int Id { get; set; }

    /// <summary>
    /// Role type name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Division identifier this role type belongs to.
    /// </summary>
    /// <remarks>Not populated by MIS.</remarks>
    public int DivisionId { get; set; }
}
