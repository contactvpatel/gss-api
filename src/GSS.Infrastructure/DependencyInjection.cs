using Caching.NET.Extensions;
using GSS.Infrastructure.DbUp.Core;
using GSS.Infrastructure.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSS.Infrastructure;

/// <summary>
/// Infrastructure registration.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers caching, data access, DbUp and the external HTTP clients.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Caching.NET: InMemory, Redis or Hybrid, from CacheOptions.
        services.AddCaching(configuration);

        services.AddInfrastructureDataAccess(configuration);

        services.AddOptions<DatabaseMigrationOptions>()
            .Bind(configuration.GetSection(DatabaseMigrationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IStartupFilter, DatabaseMigrationInitFilter>();

        services.AddInfrastructureHttpClients(configuration);

        return services;
    }
}
