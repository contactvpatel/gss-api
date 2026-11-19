using System.ComponentModel.DataAnnotations;

namespace GSS.Util.Models;

/// <summary>
/// <c>AppSettings</c> section.
/// </summary>
public class AppSettingModel
{
    /// <summary>Development.</summary>
    public const string Development = "Development";

    /// <summary>Testing (integration-test host).</summary>
    public const string Testing = "Testing";

    /// <summary>QA.</summary>
    public const string QA = "QA";

    /// <summary>UAT.</summary>
    public const string UAT = "UAT";

    /// <summary>Production.</summary>
    public const string Production = "Production";

    /// <summary>Allowed <see cref="Environment"/> values.</summary>
    public static readonly IReadOnlyList<string> AllowedEnvironments = [Development, Testing, QA, UAT, Production];

    /// <summary>Application name.</summary>
    [Required]
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>Application version, e.g. "1.0.0".</summary>
    [Required]
    [RegularExpression(@"^\d+\.\d+(\.\d+)?(-[a-zA-Z0-9.-]+)?(\+[a-zA-Z0-9.-]+)?$", ErrorMessage = "ApplicationVersion must be a valid semantic version (e.g., 1.0.0).")]
    public string ApplicationVersion { get; set; } = string.Empty;

    /// <summary>
    /// The environment switch (seeds, CORS, CSP, HSTS, HTTPS enforcement, Swagger UI). Must be an exact
    /// <see cref="AllowedEnvironments"/> value, so a typo like "Prod" fails startup.
    /// </summary>
    [Required]
    [AllowedValues(Development, Testing, QA, UAT, Production,
        ErrorMessage = "AppSettings:Environment must be exactly one of: Development, Testing, QA, UAT, Production.")]
    public string Environment { get; set; } = string.Empty;
}
