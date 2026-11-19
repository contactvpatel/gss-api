using System.ComponentModel.DataAnnotations;
using GSS.Util.Validation;

namespace GSS.Util.Models;

/// <summary>
/// MIS service endpoint paths.
/// </summary>
public class MisServiceEndpoints
{
    /// <summary>
    /// Endpoint for geo level/division.
    /// </summary>
    [Required]
    [RelativePath]
    public string GeoLevel { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint for departments.
    /// </summary>
    [Required]
    [RelativePath]
    public string Department { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint for entities.
    /// </summary>
    [Required]
    [RelativePath]
    public string Entity { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint for role types.
    /// </summary>
    [Required]
    [RelativePath]
    public string RoleType { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint for roles.
    /// </summary>
    [Required]
    [RelativePath]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint for positions.
    /// </summary>
    [Required]
    [RelativePath]
    public string Position { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint for person positions.
    /// </summary>
    [Required]
    [RelativePath]
    public string PersonPosition { get; set; } = string.Empty;
}
