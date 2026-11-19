using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using GSS.Business.Mappings;
using GSS.UnitTests.Common.Models;
using Mapster;

namespace GSS.UnitTests.Common;

/// <summary>Configures the production global Mapster instance plus the domain-neutral test entity mappings.</summary>
internal static class MapsterGlobalSettings
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Test assembly global setup.")]
    internal static void Initialize()
    {
        TypeAdapterConfig config = MapsterConfiguration.Configure(TypeAdapterConfig.GlobalSettings);
        config.NewConfig<TestEntity, TestEntityDto>();
        config.NewConfig<CreateTestEntityDto, TestEntity>()
            .Ignore(entity => entity.Id, entity => entity.CreatedAt, entity => entity.UpdatedAt,
                entity => entity.IsActive, entity => entity.CreatedBy, entity => entity.UpdatedBy);
        config.NewConfig<UpdateTestEntityDto, TestEntity>()
            .IgnoreNullValues(true)
            .Ignore(entity => entity.Id, entity => entity.CreatedAt, entity => entity.UpdatedAt,
                entity => entity.IsActive, entity => entity.CreatedBy, entity => entity.UpdatedBy);
        config.Compile();
    }
}
