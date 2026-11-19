using System.ComponentModel.DataAnnotations;
using GSS.Util.Validation;

namespace GSS.Util.Models;

/// <summary>
/// MIS client settings.
/// </summary>
public class MisServiceOptions : IValidatableObject
{
    /// <summary>Base URL: HTTPS to an external host, checked at startup.</summary>
    [Required]
    [ExternalHttpsUrl]
    public string Url { get; set; } = string.Empty;

    /// <summary>Authentication headers.</summary>
    [Required]
    public MisServiceHeaders Headers { get; set; } = new();

    /// <summary>Endpoint paths.</summary>
    [Required]
    public MisServiceEndpoints Endpoint { get; set; } = new();

    /// <summary>Selectable divisions, division ID to display name.</summary>
    public Dictionary<int, string> Divisions { get; set; } = [];

    /// <summary>Validates the nested app credentials and endpoint paths, which DataAnnotations would skip.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        return NestedValidation.Validate(Headers, nameof(Headers)).Concat(NestedValidation.Validate(Endpoint, nameof(Endpoint)));
    }
}
