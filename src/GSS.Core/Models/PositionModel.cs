using System.Text.Json.Serialization;

namespace GSS.Core.Models;

/// <summary>
/// Model representing a position from the MIS service. JSON names are MIS's wire names.
/// </summary>
public class PositionModel
{
    /// <summary>
    /// Position identifier.
    /// </summary>
    [JsonPropertyName("positionId")]
    public int Id { get; set; }

    /// <summary>
    /// Position name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Role identifier this position belongs to.
    /// </summary>
    [JsonPropertyName("roleId")]
    public int RoleId { get; set; }

    /// <summary>
    /// Additional position information.
    /// </summary>
    /// <remarks>Not populated by MIS.</remarks>
    public string? Description { get; set; }
}
