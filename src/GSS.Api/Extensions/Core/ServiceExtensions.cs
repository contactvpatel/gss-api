using GSS.Api.Extensions.Features;
using GSS.Api.Extensions.Utilities;
using GSS.Api.Middleware;
using GSS.Api.Models;
using GSS.Api.RateLimiting;
using GSS.Business;
using GSS.Infrastructure;
using GSS.Util;
using GSS.Util.Models;

namespace GSS.Api.Extensions.Core;

/// <summary>
/// Top-level service registration; delegates to the feature extensions.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>Registers all services.</summary>
    public static void ConfigureApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureApplicationSettings(configuration);
        services.ConfigureApiLayerServices(configuration);
        services.ConfigureInfrastructureServices(configuration);
        services.ConfigureBusinessServices();
    }

    /// <summary>Api-layer services: request pipeline, authentication, versioning, controllers, OpenAPI, health checks.</summary>
    private static void ConfigureApiLayerServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Forwarded headers, correlation IDs, security headers, CORS, rate limiting, compression, request limits.
        services.AddApiPipeline(configuration);
        services.AddApiPipelineRateLimitPartitionKeyResolver<RenewTokenPartitionKeyResolver>();

        // 429 and other framework errors use the Response<ApiError> envelope, not problem+json.
        services.AddSingleton<IProblemDetailsService, ApiErrorProblemDetailsService>();

        services.ConfigureAuthentication();
        services.ConfigureApiVersioning(configuration);
        services.ConfigureControllers();
        services.ConfigureOpenApi();
        services.AddHttpContextAccessor();
        services.ConfigureHealthChecks(configuration);
        services.AddExceptionHandler<ApiExceptionHandler>();
    }

    /// <summary>Infrastructure: database, repositories, migrations.</summary>
    private static void ConfigureInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Registered first so the DB connection is validated before other hosted services.
        services.AddHostedService<HostedServices.DatabaseConnectionValidationHostedService>();
        services.AddHostedService<HostedServices.IdempotencyCleanupHostedService>();

        services.AddInfrastructure(configuration);
    }

    /// <summary>Business services and mappings.</summary>
    private static void ConfigureBusinessServices(this IServiceCollection services)
    {
        services.AddBusinessServices();
    }

}
