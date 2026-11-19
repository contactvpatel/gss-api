# Mapster Mapping Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Runtime Model](#runtime-model)
- [Strict Configuration](#strict-configuration)
- [Adding Feature Mappings](#adding-feature-mappings)
- [Nested Types and Collections](#nested-types-and-collections)
- [Time and Server-Owned Values](#time-and-server-owned-values)
- [Testing](#testing)
- [References](#references)

---

## Overview

GSS uses Mapster configuration classes and runtime-compiled expression trees. Despite this file's historical name, the repository does not use Mapster.Tool source-code generation. Mapping is strict and is compiled during startup so missing pairs or destination members fail early.

## Runtime Model

`src/GSS.Business/Mappings/MapsterConfiguration.cs`:

1. enables `RequireExplicitMapping`;
2. enables `RequireDestinationMemberSource`;
3. scans the Business assembly for `IRegister` implementations;
4. compiles the resulting global configuration.

The scaffold ships `ExternalServiceMappingConfig` for SSO, MIS, and ASM model-to-DTO pairs. It ships no sample business-entity mapping.

## Strict Configuration

Every source/destination pair must be registered explicitly. Every destination member must have a source or an explicit ignore. `IgnoreEntityManagedMembers()` ignores the repository-managed entity fields on create/update maps: `Id`, `CreatedAt`, `UpdatedAt`, `CreatedBy`, and `UpdatedBy`.

For update DTOs, GSS uses `IgnoreNullValues(true)` so an omitted/null property keeps the current entity value. The same mapping path is used for PUT, PATCH, and batch update in `EntityServiceBase`; do not create a second manual merge that can drift.

## Adding Feature Mappings

The following example is illustrative; `Item` is not a shipped GSS feature:

```csharp
public sealed class ItemMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Item, ItemDto>();

        config.NewConfig<CreateItemDto, Item>()
            .Map(destination => destination.IsActive, _ => true)
            .IgnoreEntityManagedMembers();

        config.NewConfig<UpdateItemDto, Item>()
            .IgnoreNullValues(true)
            .IgnoreEntityManagedMembers()
            .Ignore(destination => destination.IsActive);
    }
}
```

Place one feature configuration in `src/GSS.Business/Mappings`, then run the mapping tests. A service derived from `EntityServiceBase` uses these pairs automatically for entity/DTO conversion and create/update mapping.

Only ignore a non-managed destination member when the omission is intentional; add a comment explaining which layer owns it. Strict mode is meant to make new properties a deliberate change.

## Nested Types and Collections

Register every nested complex pair explicitly. A top-level `Item → ItemDto` mapping does not exempt `ItemDetail → ItemDetailDto`; strict mode must be able to compile the nested pair too.

After element pairs exist, Mapster can adapt supported collection shapes, for example `IReadOnlyList<Item>` to `IReadOnlyList<ItemDto>`. GSS service contracts use read-only list results. Do not expose a mutable cached list merely because Mapster can create one.

## Time and Server-Owned Values

Mapping configuration must not read `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, or another static clock. The repository owns audit fields and reads the injected `TimeProvider`. A feature's server-owned timestamp belongs in a service hook such as `OnCreating`, with the current value supplied by an injected `TimeProvider`.

Keeping time and user identity out of mapping expressions makes mappings deterministic and keeps repository-managed fields under one owner.

## Testing

- `MapsterConfigurationTests` compiles the production configuration in strict mode.
- `UpdateDtoMappingTests` checks that update DTO fields reach the matching entity fields and null values preserve current values.
- Service tests initialize the production global configuration plus explicit domain-neutral test mappings through `MapsterGlobalSettings`.
- `ClockUsageTests` rejects direct static wall-clock reads under `src/`.

## References

- [Mapster repository](https://github.com/MapsterMapper/Mapster)
- [Mappings folder README](../../src/GSS.Business/Mappings/README.md)
- [UTC time standard](../standards/utc-time-guidelines.md)
