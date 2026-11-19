using GSS.Core.Interfaces.Base;
using GSS.Core.Interfaces.Services;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Services.External;
using GSS.Infrastructure.Services.Internal;
using GSS.Util;
using GSS.Util.Models;
using GSS.Util.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GSS.Infrastructure.Extensions;

/// <summary>
/// Registers the SSO, MIS and ASM typed HTTP clients with the HttpResilience.NET pipeline.
/// </summary>
public static class HttpClientRegistrationExtensions
{
    private const string AsmServiceName = "AsmService";
    private const string MisServiceName = "MisService";
    private const string SsoServiceName = "SsoService";

    // Responses are buffered (ResponseContentRead), so this caps memory per response.
    private const long MaxResponseBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Registers the SSO/MIS/ASM clients and services, and UserContext.
    /// Timeouts, retries, circuit breaker and connection pool come from the <c>HttpResilience</c> section.
    /// </summary>
    public static IServiceCollection AddInfrastructureHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpResilience(configuration);
        services.AddHttpResilienceTelemetry();

        RegisterHttpClient<ISsoService, SsoService, SsoServiceOptions>(services, SsoServiceName,
            (client, options, sp) => ValidateAndSetBaseAddress(client, options.Url, SsoServiceName, sp));

        RegisterHttpClient<IMisService, MisService, MisServiceOptions>(services, MisServiceName,
            (client, options, sp) =>
            {
                ValidateAndSetBaseAddress(client, options.Url, MisServiceName, sp);
                ConfigureAuthHeaders(client, MisServiceName, HttpHeaderNames.MisAuthAppId, HttpHeaderNames.MisAuthAppSecret, options.Headers.AuthAppId, options.Headers.AuthAppSecret);
            });

        RegisterHttpClient<IAsmService, AsmService, AsmServiceOptions>(services, AsmServiceName,
            (client, options, sp) =>
            {
                ValidateAndSetBaseAddress(client, options.Url, AsmServiceName, sp);
                ConfigureAuthHeaders(client, AsmServiceName, HttpHeaderNames.AsmAuthAppId, HttpHeaderNames.AsmAuthAppSecret, options.Headers.AuthAppId, options.Headers.AuthAppSecret);
            });

        services.AddScoped<IUserContext, UserContext>();

        return services;
    }

    /// <summary>
    /// Typed client (TImplementation gets the HttpClient injected) under an explicit name, so the
    /// HttpResilience:Clients:{clientName} section, log categories and metric tags use the client name (e.g. SsoService),
    /// not the interface name. Registers TClient as transient: never inject it into a singleton.
    /// </summary>
    private static void RegisterHttpClient<TClient, TImplementation, TOptions>(
        IServiceCollection services,
        string clientName,
        Action<HttpClient, TOptions, IServiceProvider> configureClient)
        where TClient : class
        where TImplementation : class, TClient
        where TOptions : class
    {
        // Resolve validated options from DI; re-binding config would skip validation.
        services.AddHttpClient<TClient, TImplementation>(clientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TOptions>>().Value;
            client.MaxResponseContentBufferSize = MaxResponseBytes;
            configureClient(client, options, serviceProvider);
        })
            .AddHttpResilienceHandler();
    }

    /// <summary>
    /// App credentials sent on every call. Startup validation already requires both; a blank one here is a bug, so it
    /// throws (naming the key, never the value) instead of sending requests the upstream rejects with 401, i.e. a 502 per call.
    /// </summary>
    private static void ConfigureAuthHeaders(HttpClient client, string serviceName, string appIdHeaderName, string appSecretHeaderName, string? appId, string? appSecret)
    {
        client.DefaultRequestHeaders.Add(appIdHeaderName, Required(appId, serviceName, nameof(MisServiceHeaders.AuthAppId)));
        client.DefaultRequestHeaders.Add(appSecretHeaderName, Required(appSecret, serviceName, nameof(MisServiceHeaders.AuthAppSecret)));
    }

    private static string Required(string? value, string serviceName, string key)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{serviceName}:Headers:{key} is not configured.")
            : value;
    }

    private static void ValidateAndSetBaseAddress(
        HttpClient client,
        string url,
        string serviceName,
        IServiceProvider serviceProvider)
    {
        if (!UrlValidator.IsValidExternalUrl(url, out var uri))
        {
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("GSS.Infrastructure.Extensions.HttpClientRegistrationExtensions");
            logger.LogError(
                "Invalid {ServiceName} service URL: {Url}. URL must be HTTPS and cannot be localhost or private IP.",
                serviceName, url);
            throw new InvalidOperationException(
                $"Invalid {serviceName} service URL: {url}. URL must be HTTPS and cannot be localhost or private IP.");
        }

        client.BaseAddress = WithTrailingSlash(uri);
    }

    /// <summary>
    /// Relative endpoints resolve against the base's last "/": without it, ".../sso/api" + "user/logout" would call
    /// ".../sso/user/logout" and every request would 404.
    /// </summary>
    private static Uri WithTrailingSlash(Uri uri)
    {
        return uri.AbsolutePath.EndsWith('/') ? uri : new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;
    }
}
