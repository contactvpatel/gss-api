using GSS.Core.Interfaces;
using GSS.Core.Interfaces.Base;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Infrastructure.Repositories;
using GSS.Util;
using GSS.Util.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GSS.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering data access infrastructure (Dapper, repositories, unit of work).
/// </summary>
public static class DataAccessExtensions
{
    /// <summary>
    /// Registers the validated <c>DbConnectionSettings</c>, Dapper connection factory, transaction manager and unit of work.
    /// </summary>
    public static IServiceCollection AddInfrastructureDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        // One section; invalid or missing settings fail startup and name the key.
        services.AddOptions<DbConnectionModel>()
            .Bind(configuration.GetSection(ConfigurationKeys.DbConnectionSettings))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DbConnectionModel>, DbConnectionModelValidator>());

        // Clock for repository audit timestamps; Business registers the same instance.
        services.TryAddSingleton(TimeProvider.System);

        // Register Dapper connection factory (for Dapper repositories)
        services.AddSingleton<IDapperConnectionFactory, DapperConnectionFactory>();

        // Register Dapper transaction manager (scoped for request-based transactions)
        services.AddScoped<IDapperTransactionManager, DapperTransactionManager>();

        // Register unit of work for batch operations (shares transaction manager with repositories)
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Stored responses of requests sent with an Idempotency-Key
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        return services;
    }
}
