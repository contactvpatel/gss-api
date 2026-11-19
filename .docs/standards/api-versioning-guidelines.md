# API Versioning Guidelines

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Current Contract](#current-contract)
- [Controller Rules](#controller-rules)
- [Deprecation](#deprecation)
- [Adding a Version](#adding-a-version)
- [Testing](#testing)
- [References](#references)

---

## Overview

GSS uses Asp.Versioning with a required major version in the URL segment and an optional matching `api-version` header. The repository currently exposes version 1.

## Current Contract

- Every versioned route MUST contain `api/v{version:apiVersion}/...`.
- Controllers MUST declare `[ApiVersion("1")]` or the applicable major version.
- `AssumeDefaultVersionWhenUnspecified` remains off; an unversioned API route is not silently treated as v1.
- The `api-version` header reader is enabled. When both URL and header are present, they MUST agree.
- `ReportApiVersions` remains on so Asp.Versioning publishes the supported and deprecated version headers (including the deprecation headers configured in `ApiPipeline:VersionDeprecation`).
- API Explorer substitutes the version into the path and groups the current document as `v1`; the OpenAPI operation has no version parameter for Swagger UI to fill.

## Controller Rules

Use integer major versions in public URLs. Do not hide breaking behavior behind an unchanged route version. Keep action names and resource nouns independent of the version where possible; versioning belongs in routing/metadata, not class-name suffixes unless parallel controllers require them.

Every versioned action MUST document its response types. A new version MUST preserve the GSS response envelope unless an approved architecture decision changes the entire API contract.

## Deprecation

Deprecation and sunset metadata comes from `ApiPipeline:VersionDeprecation` through ApiPipeline.NET.Versioning. Do not add a second deprecation middleware.

The cited RFC guidance takes precedence for the meaning and formatting of `Deprecation`, `Sunset`, and related `Link` headers. Before deprecating a version:

1. publish the replacement and migration guidance;
2. configure the date metadata;
3. verify headers with integration tests;
4. keep the old version working until the communicated sunset policy permits removal.

## Adding a Version

1. Define the compatibility/breaking-change boundary.
2. Add `[ApiVersion("2")]` metadata and a `v{version:apiVersion}` route.
3. Add or adapt controller actions without duplicating Business behavior unnecessarily.
4. Configure an OpenAPI document for the new group; the current setup registers only `v1`.
5. Add integration coverage for routing, header agreement, OpenAPI, supported-version headers, and deprecation metadata.
6. Update clients and documentation before enabling a sunset.

## Testing

`VersionDeprecationHeadersTests` and `VersionDeprecationIntegrationTests` cover the deprecation headers, and `OpenApiIntegrationTests` covers the `v1` document and the substituted version in paths. URL-versus-header agreement has no test yet.

## References

- [Asp.Versioning documentation](https://github.com/dotnet/aspnet-api-versioning/wiki)
- [RFC 9745: Deprecation HTTP Response Header](https://www.rfc-editor.org/rfc/rfc9745)
- [RFC 8594: Sunset HTTP Header](https://www.rfc-editor.org/rfc/rfc8594)
