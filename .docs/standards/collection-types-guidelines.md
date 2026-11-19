# Collection Type Guidelines

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Public Contracts](#public-contracts)
- [Materialization](#materialization)
- [Cached Collections](#cached-collections)
- [Lookup Collections](#lookup-collections)
- [Examples](#examples)
- [References](#references)

---

## Overview

Choose the narrowest collection contract that expresses the operation. GSS standardizes read and batch contracts on `IReadOnlyList<T>` because the code needs a stable count/index and does not promise caller mutation.

## Public Contracts

- Repository and Business list results MUST use `IReadOnlyList<T>`.
- Batch inputs MUST use `IReadOnlyList<T>` when count and indexed validation are required.
- DTO collection properties SHOULD be `IReadOnlyList<T>` and initialized to an empty collection when null is not meaningful.
- Use `IEnumerable<T>` only for streaming/composition where the implementation does not require repeated enumeration, count, or indexing.
- Do not expose `List<T>` merely because it is the internal materialization type.

Microsoft's collection design guidelines takes precedence if this standard conflicts with the framework guidance; the GSS rules above choose the repository's public shapes within that guidance.

## Materialization

Dapper queries are buffered before a repository returns them. Materialize a sequence once before repeated enumeration or batch validation. Avoid returning a deferred query that depends on a disposed connection.

For empty results, prefer `[]` or `Array.Empty<T>()`. Do not return null for a list contract. Be explicit when converting from an unordered lookup because repository methods such as `FindByIdsAsync` do not promise input order.

## Cached Collections

Cached L1 values may be shared between callers. GSS converts cached MIS/ASM result lists to `ImmutableArray<T>` so one caller cannot mutate another caller's cached instance. Do not put a mutable `List<T>` into a shared cache unless every read makes a defensive copy and that cost is intentional.

Return the immutable value through the `IReadOnlyList<T>` contract; callers should not depend on the concrete type.

## Lookup Collections

Use dictionaries/sets when key membership is the operation. For static read-only lookup data built once, a frozen collection is appropriate: GSS uses `FrozenDictionary` for request examples and module-code mappings. Do not freeze short-lived per-request data without measurement; construction has a cost.

When converting a sequence to a dictionary, define duplicate-key behavior first. A thrown duplicate may be the correct invariant; silently overwriting is not a default.

## Examples

```csharp
public Task<IReadOnlyList<T>> FindByIdsAsync(
    IReadOnlyList<int> ids,
    CancellationToken cancellationToken = default);
```

```csharp
IReadOnlyList<RoleDto> roles = mapped
    .OrderBy(role => role.Name, StringComparer.Ordinal)
    .ToImmutableArray();
```

Use array/list patterns based on behavior, not presumed performance. Numeric allocation or throughput claims require a benchmark for the actual workload.

## References

- [.NET collection design guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/guidelines-for-collections)
- [Immutable collections](https://learn.microsoft.com/en-us/dotnet/api/system.collections.immutable)
- [Dapper repository architecture](../architecture/dapper-hybrid-approach.md)
