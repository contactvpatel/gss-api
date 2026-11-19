using System.ComponentModel.DataAnnotations;
using GSS.Util.Validation;

namespace GSS.Util.Models;

/// <summary>
/// ASM service endpoint paths.
/// </summary>
public class AsmServiceEndpoints
{
    /// <summary>
    /// Endpoint for application security.
    /// </summary>
    [Required]
    [RelativePath]
    public string ApplicationSecurity { get; set; } = string.Empty;
}
