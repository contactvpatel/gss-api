# Naming Guidelines

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Types and Files](#types-and-files)
- [Members and Variables](#members-and-variables)
- [Async and Cancellation](#async-and-cancellation)
- [Repository Conventions](#repository-conventions)
- [Abbreviations and Domain Terms](#abbreviations-and-domain-terms)
- [Enforcement](#enforcement)
- [References](#references)

---

## Overview

GSS follows Microsoft C# naming conventions and adds repository-specific suffixes and layer rules. Microsoft's naming guidance takes precedence if this document conflicts with the language/framework convention.

## Types and Files

- Classes, records, structs, enums, delegates, and public members use `PascalCase`.
- Interfaces use `I` plus `PascalCase` (`IRepository<T>`, `IMisService`).
- One primary public type per file; the file name matches that type.
- DTOs end in `Dto`; request DTOs may use `Create`, `Update`, or the operation name before `Dto`.
- Validators end in `Validator`; repositories/services/controllers end in `Repository`, `Service`, or `Controller`.
- Extension containers end in `Extensions` and remain static.
- Enum type names are singular. A flags enum may be plural; none currently exists in GSS. Existing examples include `ModuleCode` and `AccessType`.

## Members and Variables

- Methods use verbs (`GetByIdAsync`, `ValidateAsync`, `BuildErrors`).
- Public properties use nouns; boolean properties use a clear predicate such as `IsActive`, `HasMore`, or `Enabled` where the containing type supplies context.
- Parameters and locals use `camelCase`.
- Private fields use `_camelCase` where a field is necessary.
- Field constants use `PascalCase`, matching .NET convention; local constants may be `camelCase`.
- Avoid meaningless names (`data`, `obj`, `temp`) when a domain/role name is available. Short loop indexes and conventional cancellation variable `ct` are acceptable in narrow scopes.

## Async and Cancellation

Methods returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` end in `Async`, except framework implementations whose contract fixes the name and controller actions, which omit the suffix (`RenewToken`, `Logout`). Controller actions take `CancellationToken cancellationToken` without a default. Put `CancellationToken cancellationToken = default` last on GSS-owned public async methods.

Do not create an `Async` suffix for a synchronous wrapper and do not omit it from a genuinely asynchronous repository/service method.

## Repository Conventions

- Core repository interface: `I{Entity}Repository`.
- Infrastructure implementation: `{Entity}Repository`.
- Common active-row methods keep the shared names from `IRepository<T>`.
- Page-number reads use `GetPagedAsync`; ID-cursor reads use `GetPagedAfterAsync`.
- SQL identifiers remain lower case where required by the repository base and PostgreSQL schema convention.
- DbUp files use `yyyyMMdd-HHmmss-TICKET-NNN-Description.sql`.

Do not introduce EF-style `DbContext`, `SaveChangesAsync`, or entity-set naming into this Dapper architecture.

## Abbreviations and Domain Terms

Use established project abbreviations consistently: `GSS`, `SSO`, `MIS`, `ASM`, `DTO`, `API`, `UTC`, and `ID`. In ordinary compound identifiers follow existing .NET casing (`HttpClient`, `ApiError`, `UserId`) rather than all-caps interior words.

Use the upstream wire/domain term where a public contract requires it; otherwise prefer a clear GSS term and map at the boundary.

## Enforcement

CodeStyle.NET analyzers, compiler warnings-as-errors, formatting checks, and architecture tests enforce many of these rules. `NamingConventionTests`, DTO/structure tests, and project-specific tests are the authoritative executable constraints. When adding a new pattern, update the tests and this standard together.

## References

- [C# identifier naming rules](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names)
- [.NET coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [Project structure](../architecture/project-structure.md)
