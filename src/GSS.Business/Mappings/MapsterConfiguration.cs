using System.Reflection;
using GSS.Core.Entities;
using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace GSS.Business.Mappings;

/// <summary>
/// Mapster setup: scans this assembly for <see cref="IRegister"/> configs. Update DTOs ignore nulls (merge).
/// Every type pair must be registered and every destination member must have a source or an explicit Ignore,
/// so a renamed or missing property fails <see cref="TypeAdapterConfig.Compile(bool)"/> at startup instead of mapping to default.
/// </summary>
public static class MapsterConfiguration
{
    /// <summary>Registers and compiles all mappings on <see cref="TypeAdapterConfig.GlobalSettings"/> (used by <c>Adapt</c>).</summary>
    public static IServiceCollection AddMapsterConfiguration(this IServiceCollection services)
    {
        var config = Configure(TypeAdapterConfig.GlobalSettings);
        config.Compile();

        return services;
    }

    /// <summary>Applies the strict settings and every mapping to <paramref name="config"/> (not compiled).</summary>
    public static TypeAdapterConfig Configure(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.RequireExplicitMapping = true;
        config.RequireDestinationMemberSource = true;
        config.Scan(Assembly.GetExecutingAssembly());

        return config;
    }

    /// <summary>Members a create/update DTO never supplies: the id and audit stamps, set by the repository and service.</summary>
    internal static TypeAdapterSetter<TSource, TEntity> IgnoreEntityManagedMembers<TSource, TEntity>(this TypeAdapterSetter<TSource, TEntity> setter)
        where TEntity : EntityBase
    {
        return setter.Ignore(e => e.Id, e => e.CreatedAt, e => e.UpdatedAt!, e => e.CreatedBy, e => e.UpdatedBy);
    }
}
