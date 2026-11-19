using System.ComponentModel.DataAnnotations;
using GSS.Util.Validation;

namespace GSS.Util.Models;

/// <summary>
/// ASM client settings.
/// </summary>
public class AsmServiceOptions : IValidatableObject
{
    /// <summary>Base URL: HTTPS to an external host, checked at startup.</summary>
    [Required]
    [ExternalHttpsUrl]
    public string Url { get; set; } = string.Empty;

    /// <summary>Authentication headers.</summary>
    [Required]
    public AsmServiceHeaders Headers { get; set; } = new();

    /// <summary>Endpoint paths.</summary>
    [Required]
    public AsmServiceEndpoints Endpoint { get; set; } = new();

    /// <summary>Validates the nested app credentials and endpoint paths, which DataAnnotations would skip.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        return NestedValidation.Validate(Headers, nameof(Headers)).Concat(NestedValidation.Validate(Endpoint, nameof(Endpoint)));
    }
}
