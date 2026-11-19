using GSS.Util;
using GSS.Util.Models;

namespace GSS.Api.Extensions.Core;

/// <summary>
/// Options binding and startup validation. Sources are the framework defaults (appsettings.json, user secrets,
/// env vars <c>Section__Key</c>, command line); keep that chain, rebuilding it drops providers and test overrides.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>Binds each options type and validates it at startup.</summary>
    public static void ConfigureApplicationSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AppSettingModel>()
            .Bind(configuration.GetSection(ConfigurationKeys.AppSettings))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SsoServiceOptions>()
            .Bind(configuration.GetSection(ConfigurationKeys.SsoService))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MisServiceOptions>()
            .Bind(configuration.GetSection(ConfigurationKeys.MisService))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AsmServiceOptions>()
            .Bind(configuration.GetSection(ConfigurationKeys.AsmService))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
