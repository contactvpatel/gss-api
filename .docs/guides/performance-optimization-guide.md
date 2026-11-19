# Performance Investigation Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [What the Repository Proves](#what-the-repository-proves)
- [What the Repository Does Not Prove](#what-the-repository-does-not-prove)
- [Measurement Workflow](#measurement-workflow)
- [Database](#database)
- [HTTP, Caching, and Serialization](#http-caching-and-serialization)
- [Change Checklist](#change-checklist)
- [References](#references)

---

## Overview

Performance work in GSS starts with a measured workload and a stated constraint. This guide documents implementation characteristics and a repeatable investigation process; it does not claim speedups, allocation reductions, throughput, latency targets, or completion status that the repository has not benchmarked.

## What the Repository Proves

Tests and code inspection currently prove structural behavior:

- repository range writes generate set-based PostgreSQL statements from typed arrays rather than one SQL string per row;
- generated repository SQL is cached per concrete repository type and is independent of batch size;
- page-number and ID-cursor reads fetch one extra row to detect a next page, while total counts are opt-in;
- external HTTP clients share pooled handlers through typed `IHttpClientFactory` registration and a single configured resilience pipeline;
- Mapster explicit mappings are compiled at startup;
- System.Text.Json uses a source-generated context for the registered application/external types;
- Caching.NET supports in-memory, Redis, and hybrid modes, with single-flight factory behavior used for security-sensitive lookups;
- response compression and request limits are configured through ApiPipeline.NET.

These facts describe design shape. They are not benchmark results and do not establish that one design is faster for a particular workload.

## What the Repository Does Not Prove

GSS contains no committed load-test suite or benchmark baseline. It therefore does not prove:

- requests per second or percentile latency;
- a percentage improvement from batch SQL, mapping compilation, source-generated JSON, caching, or compression;
- acceptable database pool saturation, cache hit rate, error rate, CPU, memory, or telemetry overhead;
- that Development/Compose settings are appropriate production capacity settings.

Do not turn an architectural expectation into a numeric claim without saving the workload, environment, raw results, and comparison method.

## Measurement Workflow

1. Define one user-visible scenario and a target such as p95 latency under a stated concurrency.
2. Record hardware/container limits, application commit, configuration, database size, cache state, and external-service behavior.
3. Warm the application and connection pools consistently.
4. Capture a baseline with traces, request metrics, process counters, database query plans/statistics, and allocation/CPU profiles.
5. Change one bottleneck at a time and add a behavior/regression test before optimizing.
6. Repeat the same workload enough times to distinguish noise from a useful change.
7. Store the tool command and results with the decision if documentation will quote the measurement.

Use a production-like copy with synthetic or properly protected data. Never load-test shared UAT dependencies without authorization.

## Database

- Prefer `GetPagedAfterAsync` for forward traversal over an indexed ID and `GetPagedAsync` where page numbers are a product requirement.
- Request a total count only when the caller displays it.
- Use existing set-based range methods for one homogeneous batch. Use Unit of Work only for several operations that must commit together.
- Inspect PostgreSQL query plans and indexes for feature-specific predicates. The base repository cannot select indexes for a feature.
- Keep database calls sequential inside one Unit of Work; parallel commands share one connection and are rejected.
- Monitor connection acquisition and pool capacity before increasing `MaxPoolSize`. Include peak replica count in the capacity calculation.

For a slow query, capture `EXPLAIN (ANALYZE, BUFFERS)` in a safe environment with representative data. Do not paste production-sensitive literals into issues or docs.

## HTTP, Caching, and Serialization

- Reuse the registered typed clients; do not construct per-request `HttpClient` instances.
- Tune resilience from observed upstream latency and failure behavior. A larger timeout or retry count can increase queueing and downstream load.
- Cache only when staleness, invalidation, and security semantics are explicit. A high hit rate is not automatically correct if permission data is stale.
- Include every result-changing input in a cache key and choose local/distributed expirations deliberately.
- Add DTOs and external envelopes to the source-generated JSON context rather than falling back silently to reflection.
- Verify response compression with representative payloads; small JSON responses may not benefit enough to justify extra CPU.

## Change Checklist

- [ ] The bottleneck is visible in a trace, profile, query plan, or repeatable benchmark.
- [ ] Correctness tests cover the code being changed.
- [ ] The workload, environment, and configuration are recorded.
- [ ] The comparison uses the same inputs and warm-up rules.
- [ ] Cancellation, timeouts, memory, pool use, and failure behavior were checked—not only the happy-path mean.
- [ ] No credentials, tokens, or personal data appear in captured artifacts.
- [ ] Any numeric documentation claim links to reproducible current evidence.

## References

- [ASP.NET Core performance best practices](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices)
- [.NET diagnostics tools](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/)
- [PostgreSQL EXPLAIN](https://www.postgresql.org/docs/current/using-explain.html)
- [Dapper repository architecture](../architecture/dapper-hybrid-approach.md)
