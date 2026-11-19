using GSS.Business.Mappings;
using GSS.Business.Services;
using GSS.Business.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GSS.Business;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        // Configure Mapster mappings for optimized performance
        services.AddMapsterConfiguration();

        // Idempotency-Key support for POST creates
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        // Register SSO service (business layer wraps core service)
        services.AddScoped<ISsoService, SsoService>();

        // Register MIS service (business layer wraps core service)
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMisService, MisService>();

        // Register ASM service (business layer wraps core service)
        services.AddScoped<IAsmService, AsmService>();

        return services;
    }
}
