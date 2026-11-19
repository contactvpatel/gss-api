using System.ComponentModel.DataAnnotations;

namespace GSS.Util.Validation;

/// <summary>
/// An absolute HTTPS URL to an external host (no loopback, private or link-local IP), so a bad service URL fails
/// startup instead of every request. Blank passes; pair with [Required].
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ExternalHttpsUrlAttribute : ValidationAttribute
{
    /// <summary>Default message.</summary>
    public ExternalHttpsUrlAttribute()
        : base("{0} must be an HTTPS URL to an external host (not localhost or a private IP).")
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value is not string url || url.Length == 0 || UrlValidator.IsValidExternalUrl(url, out _);
    }
}
