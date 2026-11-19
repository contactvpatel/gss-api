# Mapster Mappings

This folder contains Mapster mapping configurations for feature entities. None ship with the GSS scaffold; add one `IRegister` class per feature.

## How It Works

Mapster uses **compiled expression trees** (runtime; Mapster.Tool code generation is not used):

1. **Configuration**: One `IRegister` class per entity (`{Entity}MappingConfig.cs`), set up by `MapsterConfiguration.cs`
2. **Compilation**: At startup, `TypeAdapterConfig.Compile()` builds every registered mapping and fails on gaps
3. **Execution**: `.Adapt<>()` calls reuse the compiled mappings

## Files

| File | Description |
|------|-------------|
| `MapsterConfiguration.cs` | Setup: strict mode, assembly scan, compile; `IgnoreEntityManagedMembers()` helper |
| `ExternalServiceMappingConfig.cs` | SSO, MIS and ASM models (Core) → Business DTOs |
| `{Entity}MappingConfig.cs` | One `IRegister` per feature entity: Entity → DTO, Create DTO → Entity, Update DTO → Entity |

## Available Mappings

SSO/MIS/ASM model → DTO pairs only (`ExternalServiceMappingConfig`). Each feature adds a `<Entity>MappingConfig : IRegister` class (see [Adding New Mappings](#adding-new-mappings)).

## Usage

### In Services

```csharp
using Mapster;

// Entity to DTO
ResourceDto dto = resource.Adapt<ResourceDto>();

// DTO to Entity
Resource entity = createDto.Adapt<Resource>();

// Update DTO onto an existing entity (null fields keep current values)
updateDto.Adapt(resource);

// Collections
IReadOnlyList<ResourceDto> dtos = resources.Adapt<IReadOnlyList<ResourceDto>>();

// Null-safe
ResourceDto? dto = resource?.Adapt<ResourceDto>();
```

### Configuration

All mappings are automatically registered at startup:

```csharp
// In DependencyInjection.cs
services.AddMapsterConfiguration();
```

This applies strict mode (`MapsterConfiguration.Configure`: `RequireExplicitMapping` and `RequireDestinationMemberSource`), scans for all `IRegister` implementations and compiles them, so an unmapped property fails startup instead of mapping to default. SSO/MIS/ASM model → DTO pairs are in `ExternalServiceMappingConfig`.

Strict mode applies to nested types too. Register every nested source/destination pair explicitly; do not rely on an unconfigured complex member being mapped implicitly.

Mapping configuration must not read the wall clock or create audit timestamps. Time-driven values belong in a service or repository that receives the application's injected `TimeProvider`.

## Adding New Mappings

1. **Add configuration** in a new file in this folder (e.g. `NewEntityMappingConfig.cs`):

```csharp
public class NewEntityMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Entity -> DTO
        config.NewConfig<NewEntity, NewEntityDto>();

        // CreateDto -> Entity: the id and audit stamps are set by the repository and service
        config.NewConfig<CreateNewEntityDto, NewEntity>()
            .Map(dest => dest.IsActive, src => true)
            .IgnoreEntityManagedMembers();

        // UpdateDto -> Entity: merge non-null values; never touch the id, audit stamps or IsActive
        config.NewConfig<UpdateNewEntityDto, NewEntity>()
            .IgnoreNullValues(true)
            .IgnoreEntityManagedMembers()
            .Ignore(e => e.IsActive);
    }
}
```

2. **Run `MapsterConfigurationTests` and `UpdateDtoMappingTests`** - Mapster discovers the class; strict mode fails on unregistered pairs or unmapped destination members, while the update test checks that every update field reaches the same-named entity member and nulls preserve current values.

3. **Use** in services. A service extending `EntityServiceBase` needs no custom mapping code:

```csharp
NewEntityDto dto = entity.Adapt<NewEntityDto>();
```

## Documentation

- **Complete Guide**: `.docs/guides/mapster-code-generation-guide.md`
- **Mapster Docs**: <https://github.com/MapsterMapper/Mapster>

## Testing

`MapsterConfigurationTests` compiles the whole configuration under strict mode. `UpdateDtoMappingTests` checks update-merge semantics. Unit tests initialize the production global Mapster configuration plus domain-neutral test mappings through `MapsterGlobalSettings` before service tests run.
