# OpenTelemetry Integration

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Registration](#registration)
- [Configuration](#configuration)
- [Signals and Enrichment](#signals-and-enrichment)
- [Local Collection](#local-collection)
- [Security and Cardinality](#security-and-cardinality)
- [Verification](#verification)
- [References](#references)

---

## Overview

GSS delegates observability setup to the private `OpenTelemetry.NET` package. The API calls `builder.AddObservability()` before registering application services, and configuration under `OpenTelemetryOptions` controls traces, metrics, logs, Npgsql instrumentation, the OTLP collector address, and additional meters. This repository does not publish benchmarked telemetry overhead or a production sampling policy.

## Registration

`src/GSS.Api/Program.cs` performs the single application-level registration:

```csharp
builder.AddObservability();
```

Do not add a second OpenTelemetry service-provider pipeline beside it. Package-specific behavior belongs in the package configuration or package documentation, not in duplicate GSS registrations.

ApiPipeline.NET's meter is exported through `OpenTelemetryOptions:Meters`, and HttpResilience.NET publishes its own resilience metrics. Npgsql instrumentation is enabled through the OpenTelemetry option described below.

## Configuration

The tracked Development defaults in `src/GSS.Api/appsettings.json` are:

```json
"OpenTelemetryOptions": {
  "EnableTracing": true,
  "EnableMetrics": true,
  "EnableLogging": true,
  "EnableNpgsqlInstrumentation": true,
  "CollectorUrl": "http://localhost:4317",
  "Meters": [ "ApiPipeline.NET" ]
}
```

Override values with normal ASP.NET Core environment-variable syntax, for example `OpenTelemetryOptions__CollectorUrl`. Integration tests disable the three exported signals so test execution does not depend on a collector.

`CollectorUrl` is an OTLP endpoint. Whether a deployed environment should enable each signal and how a collector samples, retains, or exports data is an operational decision outside this repository.

## Signals and Enrichment

- **Tracing:** ASP.NET Core request activities, outbound HTTP client instrumentation from OpenTelemetry.NET, and Npgsql spans when `EnableNpgsqlInstrumentation` is true.
- **Metrics:** the package's built-in meters plus each name in `OpenTelemetryOptions:Meters` (a meter not registered there is dropped). GSS lists `ApiPipeline.NET` so its request-pipeline metrics are exported.
- **Logs:** application logs are exported when logging is enabled. Logging goes through Serilog: the package always registers a console sink, and the `Serilog` section of `appsettings.json` sets the minimum levels.

ApiPipeline.NET puts correlation data on the request activity. After SSO validation succeeds, `SsoAuthenticationHandler` adds `user.id` to the ASP.NET Core request activity because the activity begins before authentication. The bearer token is not added as a claim or telemetry tag.

## Local Collection

Docker Compose runs an Aspire telemetry dashboard. The API container sends OTLP data to `http://otel-dashboard:18889`, and the dashboard UI is published at `http://localhost:18888` by default. The non-container Development collector default is `http://localhost:4317`; a collector must be listening there for export to succeed.

The dashboard is a local-development dependency, not evidence that a production backend, retention policy, alert, or service-level objective exists.

## Security and Cardinality

- Do not attach tokens, credentials, request bodies, or unrestricted user input to logs, spans, or metric labels.
- Prefer stable, bounded attributes such as route templates and service names over raw URLs or error messages.
- `SensitiveDataSanitizer` is used on outbound-service failure details before those details reach the boundary exception/log path. It is not a general license to log arbitrary sensitive content.
- Keep `user.id` usage limited to authorized operational needs and apply the organization's telemetry-access and retention controls.

No fixed CPU overhead, sampling percentage, error-rate target, or pool-utilization threshold is asserted by GSS documentation. Establish those values from workload tests and production requirements.

## Verification

- `ConfigurationFileTests` requires Npgsql instrumentation in tracked configuration.
- `SsoAuthenticationHandlerTests` verifies the request activity receives `user.id` for an accepted token and not for one SSO rejects; `AuthenticationIntegrationTests` verifies the tag on the request span for a valid token.
- `SensitiveDataSanitizerTests` verifies masking of supported sensitive patterns used in outbound failure handling.

## References

- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/)
- [OpenTelemetry semantic conventions](https://opentelemetry.io/docs/specs/semconv/)
- [API pipeline](api-pipeline.md)
