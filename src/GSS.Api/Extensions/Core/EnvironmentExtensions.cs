using GSS.Util;
using GSS.Util.Models;

namespace GSS.Api.Extensions.Core;

/// <summary>
/// Environment checks on <c>AppSettings:Environment</c>, the only environment switch (never ASPNETCORE_ENVIRONMENT).
/// </summary>
public static class EnvironmentExtensions
{
    /// <summary>Development.</summary>
    public const string Development = AppSettingModel.Development;

    /// <summary>Production.</summary>
    public const string Production = AppSettingModel.Production;

    /// <summary>Testing (integration-test host).</summary>
    public const string Testing = AppSettingModel.Testing;

    /// <summary><c>AppSettings:Environment</c>, or empty when unset.</summary>
    public static string GetConfiguredEnvironment(this IConfiguration configuration)
    {
        return configuration.GetValue<string>(ConfigurationKeys.AppSettingsEnvironment) ?? string.Empty;
    }

    /// <summary>True when the environment matches (case-insensitive).</summary>
    public static bool IsConfiguredEnvironment(this IConfiguration configuration, string environmentName)
    {
        return string.Equals(configuration.GetConfiguredEnvironment(), environmentName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True in Development.</summary>
    public static bool IsDevelopmentEnvironment(this IConfiguration configuration)
    {
        return configuration.IsConfiguredEnvironment(Development);
    }

    /// <summary>True in Production.</summary>
    public static bool IsProductionEnvironment(this IConfiguration configuration)
    {
        return configuration.IsConfiguredEnvironment(Production);
    }

    /// <summary>True in Testing.</summary>
    public static bool IsTestingEnvironment(this IConfiguration configuration)
    {
        return configuration.IsConfiguredEnvironment(Testing);
    }

    /// <summary>
    /// Sets the host environment name to <c>AppSettings:Environment</c>, for libraries that read
    /// <see cref="IHostEnvironment"/> (ApiPipeline.NET is lenient only in Development). Call right after Build.
    /// App code still reads configuration, never the host environment.
    /// </summary>
    public static void UseConfiguredEnvironment(this WebApplication app)
    {
        string environment = app.Configuration.GetConfiguredEnvironment();
        if (string.IsNullOrWhiteSpace(environment))
        {
            return;
        }

        // IHostEnvironment and IWebHostEnvironment are separate instances.
        app.Services.GetRequiredService<IHostEnvironment>().EnvironmentName = environment;
        app.Environment.EnvironmentName = environment;
    }
}
