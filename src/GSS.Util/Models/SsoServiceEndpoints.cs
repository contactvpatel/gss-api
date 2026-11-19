using System.ComponentModel.DataAnnotations;
using GSS.Util.Validation;

namespace GSS.Util.Models;

/// <summary>
/// SSO service endpoint paths.
/// </summary>
public class SsoServiceEndpoints
{
    /// <summary>
    /// Endpoint for token validation.
    /// </summary>
    [Required]
    [RelativePath]
    public string ValidateToken { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint for token renewal.
    /// </summary>
    [Required]
    [RelativePath]
    public string RenewToken { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint for user logout.
    /// </summary>
    [Required]
    [RelativePath]
    public string Logout { get; set; } = string.Empty;
}
