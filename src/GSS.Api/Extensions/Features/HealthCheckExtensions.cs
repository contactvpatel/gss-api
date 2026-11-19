using Caching.NET.Options;
using GSS.Api.HealthChecks;
using GSS.Infrastructure.Interfaces;
using HealthChecks.NET;

namespace GSS.Api.Extensions.Features;

/// <summary>
/// HealthChecks.NET probes. Liveness is process-only; database, cache and config checks run on startup/readiness.
/// </summary>
public static class HealthCheckExtensions
{
    private const string DatabaseReadCheckName = "database-read";
    private const string DatabaseWriteCheckName = "database-write";
    private const string CacheOptionsSection = "CacheOptions";

    /// <summary>Prefix of the canonical probe routes.</summary>
    internal const string InternalProbePathPrefix = "/internal/health";

    /// <summary>Probe result cache TTL.</summary>
    private const int DefaultCacheTtlSeconds = 5;

    /// <summary>Registers the health checks.</summary>
    public static void ConfigureHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        // Custom checks: AddPostgreSqlCheck allows only one, and we need read and write.
        var healthChecks = services.AddApiHealthChecks(DefaultCacheTtlSeconds)
            .AddCustomCheck(DatabaseReadCheckName, serviceProvider =>
                new DatabaseConnectionHealthCheck(serviceProvider.GetRequiredService<IDapperConnectionFactory>().CreateReadConnection))
            .AddCustomCheck(DatabaseWriteCheckName, serviceProvider =>
                new DatabaseConnectionHealthCheck(serviceProvider.GetRequiredService<IDapperConnectionFactory>().CreateWriteConnection))
            .AddDiskCheck()
            .AddMemoryCheck();

        // Redis failure is Degraded, so the pod stays in rotation.
        var cacheOptions = configuration.GetSection(CacheOptionsSection).Get<CachingOptions>() ?? new CachingOptions();
        if (cacheOptions.Enabled
            && cacheOptions.Mode is CacheMode.Redis or CacheMode.Hybrid
            && !string.IsNullOrWhiteSpace(cacheOptions.Redis.Configuration))
        {
            healthChecks.AddRedisCheck(
                GetRedisConnectionString(cacheOptions.Redis),
                strictRedisCertificateValidation: cacheOptions.Redis.StrictCertificateValidation);
        }
    }

    /// <summary>Maps the probe routes and marks the app ready once started.</summary>
    public static void ConfigureHealthCheckEndpoints(this WebApplication app)
    {
        // No HTTP metrics; no rate limiting, so a 429 cannot fail readiness.
        app.MapHealthCheckEndpoints(endpoint => endpoint.DisableHttpMetrics().DisableRateLimiting().AllowAnonymous());

        // ApplicationStarted fires after DB validation and migrations succeed; until then startup/readiness are Unhealthy.
        app.Lifetime.ApplicationStarted.Register(
            () => app.Services.GetRequiredService<IStartupReadinessMarker>().MarkReady());
    }

    /// <summary>Applies Caching.NET's UseTls to the connection string.</summary>
    private static string GetRedisConnectionString(RedisOptions redisOptions)
    {
        return redisOptions.UseTls
            ? $"{redisOptions.Configuration},ssl=true"
            : redisOptions.Configuration!;
    }
}
