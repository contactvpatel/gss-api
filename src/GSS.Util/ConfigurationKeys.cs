namespace GSS.Util;

/// <summary>
/// Configuration section and key names.
/// </summary>
public static class ConfigurationKeys
{
    /// <summary>AppSettings section.</summary>
    public const string AppSettings = "AppSettings";

    /// <summary>Application name, e.g. "GSS.Api".</summary>
    public const string AppSettingsApplicationName = "AppSettings:ApplicationName";

    /// <summary>Application version, e.g. "1.0.0".</summary>
    public const string AppSettingsApplicationVersion = "AppSettings:ApplicationVersion";

    /// <summary>Build number env var shown in Swagger UI, e.g. "123".</summary>
    public const string AppBuildNumber = "APP_BUILD_NUMBER";

    /// <summary>Environment: Development, Testing, QA, UAT or Production.</summary>
    public const string AppSettingsEnvironment = "AppSettings:Environment";

    /// <summary>Database connection settings (Read/Write), bound to <c>DbConnectionModel</c>. The only section name.</summary>
    public const string DbConnectionSettings = "DbConnectionSettings";

    /// <summary>SSO service.</summary>
    public const string SsoService = "SsoService";

    /// <summary>MIS service.</summary>
    public const string MisService = "MisService";

    /// <summary>ASM service.</summary>
    public const string AsmService = "AsmService";
}
