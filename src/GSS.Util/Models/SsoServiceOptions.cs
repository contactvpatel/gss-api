using System.ComponentModel.DataAnnotations;
using GSS.Util.Validation;

namespace GSS.Util.Models;

/// <summary>
/// SSO client settings.
/// </summary>
public class SsoServiceOptions : IValidatableObject
{
    /// <summary>Base URL: HTTPS to an external host, checked at startup.</summary>
    [Required]
    [ExternalHttpsUrl]
    public string Url { get; set; } = string.Empty;

    /// <summary>Endpoint paths.</summary>
    [Required]
    public SsoServiceEndpoints Endpoint { get; set; } = new();

    /// <summary>Validates the nested endpoint paths, which DataAnnotations would skip.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        return NestedValidation.Validate(Endpoint, nameof(Endpoint));
    }
}
