# GSS Documentation Reconciliation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the GSS documentation with a verified GSS adaptation of the current `boilerplate-net` baseline, remove all source-template commerce residue, and prevent the same drift from returning.

**Architecture:** Add offline architecture tests for documentation invariants before changing prose. Port each matching boilerplate document by category, translate only real shared infrastructure, remove unsupported feature claims, and use GSS source/tests/configuration as the final authority. Historical plans/specs remain historical but receive explicit status notices and neutralized source-template terminology.

**Tech Stack:** Markdown, .NET 10, xUnit, FluentAssertions, repository architecture tests, PowerShell, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-11-19-gss-documentation-reconciliation-design.md`

## Global Constraints

- Reference checkout: `/Users/vishalpatel/Projects/boilerplate-net` at `b152ee3`; never modify it.
- Do not import its dirty template-review file or any capability GSS does not ship.
- GSS source, configuration, tests, and scripts override boilerplate prose.
- Preserve the intent of the eight pre-existing modified GSS docs; reconcile, do not discard blindly.
- Do not add production behavior to make documentation true.
- Current docs must not claim nonexistent features, files, endpoints, benchmarks, or test results.
- Final tracked Markdown must contain no case-insensitive source-template commerce product name.
- Do not commit: the workspace contains user-owned uncommitted changes and no commit was requested.

## Review Focus

- Mechanical renaming must not turn boilerplate feature code into fictional GSS code; `CurrentDocumentation_RepositoryLinksResolve` and manual symbol checks cover this.
- Existing user corrections must survive the mirror; final diff review compares the eight original hunks with their reconciled replacements.
- `.docs` index and document structure must stay synchronized; focused architecture tests cover index entries, back links, TOCs, references, and fences.
- Historical artifacts can reintroduce forbidden terminology; the repository-wide Markdown residue test includes `docs/superpowers/`.
- Boilerplate-only documents must not appear in GSS; the index/inventory test pins the approved GSS topic set.

---

### Task 1: Add Documentation Integrity Tests

**Files:**
- Create: `tests/GSS.ArchitectureTests/DocumentationTests.cs`
- Reference: `tests/GSS.ArchitectureTests/RepositoryPaths.cs`
- Reference: `.docs/documentation-standards.md`

**Interfaces:**
- Consumes: `RepositoryPaths.Root` and the approved `.docs` organization.
- Produces: deterministic, offline checks used by every subsequent task.

- [ ] **Step 1: Add failing index and inventory tests**

Add `DocsIndex_ListsEveryTopicExactlyOnce` and `DocsInventory_ContainsOnlyApprovedTopics`. Enumerate `.docs/{architecture,guides,standards,testing}/*.md`, require one relative link per file in `.docs/README.md`, and pin the GSS topic set so boilerplate-only schema/load-test documents are rejected.

- [ ] **Step 2: Add failing structure tests**

Add `TopicDocs_FollowTheDocumentTemplate`, checking first-line H1, canonical back link near the top, `Table of Contents` for documents with four or more `##` sections, an `Overview`, and a final `References` or `Related Documentation` section. Add `Standards_StateExternalGuidancePrecedence` and `MarkdownFences_AreBalanced`.

- [ ] **Step 3: Add failing link, path, and residue tests**

Add `LocalMarkdownLinks_Resolve`, `CurrentDocumentation_RepositoryLinksResolve`, and `TrackedMarkdown_HasNoSourceTemplateCommerceResidue`. Ignore HTTP/mail links, heading fragments, template placeholders, and explicitly illustrative fenced examples for repository-path resolution; scan all tracked-scope Markdown for residue.

- [ ] **Step 4: Run the focused tests and verify RED**

Run: `dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore --filter "FullyQualifiedName~DocumentationTests"`

Expected: failures name the current missing template sections, stale paths, and forbidden residue rather than throwing parser errors.

- [ ] **Step 5: Keep the tests minimal and deterministic**

No Markdown package or network dependency. Use small private helpers for heading extraction, local-link normalization, and repository-relative paths. Do not attempt to emulate every GitHub anchor rule.

### Task 2: Reconcile Entry Points and Documentation Policy

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `.docs/README.md`
- Modify: `.docs/documentation-standards.md`
- Modify: `scripts/README.md`
- Modify: `src/GSS.Business/Mappings/README.md`
- Modify: `docs/superpowers/specs/2026-11-19-gss-documentation-reconciliation-design.md`

**Interfaces:**
- Consumes: boilerplate entry-point docs, GSS project inventory, scripts, configuration, and Task 1 rules.
- Produces: the canonical navigation, scope, terminology, and documentation contract used by all topic files.

- [ ] **Step 1: Port and reconcile root guidance**

Use boilerplate `README.md`, `CLAUDE.md`, and `scripts/README.md` as the baseline. Retain GSS's five source projects, three test projects, SSO/MIS/ASM endpoints, domain-neutral status, GSS ports, configuration, and verified commands. Remove feature/domain claims that do not exist in GSS and include `GetPagedAfterAsync` in repository capability summaries.

- [ ] **Step 2: Rebuild the `.docs` index from the approved inventory**

List every GSS topic exactly once with a truthful one-line description. Do not add the boilerplate schema diagram, template review, or load-test docs. State that examples are illustrative and that implementation-bearing claims must match the repository.

- [ ] **Step 3: Make documentation standards enforceable**

Align the written template exactly with Task 1: canonical back link, TOC threshold, Overview, references, local links, precedence for standards, current-vs-illustrative labeling, and prohibition on unsupported measurements/status labels.

- [ ] **Step 4: Reconcile the mapping README and approved design**

Keep strict Mapster behavior (`RequireExplicitMapping`, `RequireDestinationMemberSource`, startup compilation), remove implicit nested-mapping claims, use `TimeProvider` for time-driven examples, and neutralize the source-template commerce name in the approved design without changing its selected approach.

- [ ] **Step 5: Run entry-point documentation tests**

Run the focused Task 1 tests and require failures to be limited to topic documents not yet ported.

### Task 3: Port Architecture Documentation

**Files:**
- Modify: `.docs/architecture/allowed-hosts.md`
- Modify: `.docs/architecture/api-pipeline.md`
- Modify: `.docs/architecture/asm-authorization.md`
- Modify: `.docs/architecture/dapper-hybrid-approach.md`
- Modify: `.docs/architecture/error-contract.md`
- Modify: `.docs/architecture/exception-handling.md`
- Modify: `.docs/architecture/health-checks.md`
- Modify: `.docs/architecture/idempotency-analysis.md`
- Modify: `.docs/architecture/opentelemetry-integration.md`
- Modify: `.docs/architecture/project-structure.md`
- Modify: `.docs/architecture/resilience.md`
- Modify: `.docs/architecture/security-configuration-comparison.md`
- Modify: `.docs/architecture/service-registration-layering.md`
- Modify: `.docs/architecture/sso-authentication.md`
- Modify: `.docs/architecture/validation-filter.md`

**Interfaces:**
- Consumes: matching boilerplate architecture docs plus current GSS source and architecture/integration tests.
- Produces: current-state explanations of GSS cross-cutting architecture.

- [ ] **Step 1: Port shared pipeline and security documents**

Translate the boilerplate versions of allowed hosts, API pipeline, error contract, exception handling, health checks, resilience, security comparison, SSO, ASM authorization, service layering, and validation. Preserve GSS configuration keys, routes, error envelope, external-service interfaces, and test names.

- [ ] **Step 2: Rewrite Dapper architecture from the real base**

Document `SelectSql`, shared reads including cursor paging, `BatchColumns`, set-based `unnest` writes, `TimeProvider`, `IUserContext`, transaction participation, retry boundaries, and repository validation. Remove `DapperQueryBuilder`, nonexistent feature repositories, obsolete constructors, direct wall-clock reads, and unsupported speed claims. Feature repository code remains explicitly illustrative.

- [ ] **Step 3: Rebuild project structure from the tracked tree**

Generate the documented tree from actual GSS folders/files at implementation time. Mark feature extension locations without listing fictional repository/interface files. Correct Unit of Work guidance: ordinary range methods own their set-based write; use Unit of Work only for composed multi-write operations.

- [ ] **Step 4: Reconcile idempotency and observability**

Describe generic `EntityServiceBase` behavior rather than nonexistent domain services. State that the scaffold has no create endpoints and integration coverage uses a probe controller. Remove unproved “production-ready” language and document only configured OpenTelemetry signals/exporters and tested sanitization.

- [ ] **Step 5: Run focused documentation tests and architecture tests**

Run Task 1 tests, then `dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore`.

Expected: documentation checks may still identify guide/standard/testing work; existing architecture tests remain green.

### Task 4: Port Developer Guides

**Files:**
- Modify: `.docs/guides/database-migration-postgresql-to-sqlserver.md`
- Modify: `.docs/guides/dbup-migrations.md`
- Modify: `.docs/guides/docker-compose.md`
- Modify: `.docs/guides/httpclient-factory.md`
- Modify: `.docs/guides/hybrid-caching.md`
- Modify: `.docs/guides/mapster-code-generation-guide.md`
- Modify: `.docs/guides/performance-optimization-guide.md`
- Modify: `.docs/guides/sonarqube-setup-guide.md`
- Modify: `.docs/guides/swagger-ui.md`

**Interfaces:**
- Consumes: boilerplate guides, GSS scripts/configuration, and current implementation APIs.
- Produces: executable setup and extension guidance for GSS developers.

- [ ] **Step 1: Port verified operational guides**

Reconcile DbUp, Compose, HttpClient, caching, SonarQube, and Swagger against real GSS files and commands. Preserve the user's verified constructor, cache-expiry, Compose, and connection-setting corrections.

- [ ] **Step 2: Replace the SQL Server migration guide with a real migration map**

Use the boilerplate guide for coverage, but name the actual GSS replacement points: Npgsql packages, `DatabaseMigrationInitFilter`, `MigrationBatches`, `DapperConnectionFactory`, repository SQL generation, PostgreSQL-specific types/locks/`unnest`, health checks, integration fixtures, and migrations. Remove nonexistent hosted service/query-builder instructions. Clearly state that this is a future porting guide, not a supported second provider.

- [ ] **Step 3: Correct the Mapster guide**

Keep runtime expression-tree compilation and strict explicit pairs. Require nested pair registration, use illustrative feature mappings, and remove direct wall-clock assignment examples.

- [ ] **Step 4: Replace performance marketing with evidence-based guidance**

Remove all unsupported percentages, nonexistent services/files, and universal completion claims. Document measured facts only when a committed test proves shape/round trips; otherwise describe profiling recommendations and current implementation characteristics without speed multipliers.

- [ ] **Step 5: Validate operational commands**

Run `docker compose config --quiet`; parse all `scripts/*.ps1`; run the focused documentation tests.

### Task 5: Port Coding Standards

**Files:**
- Modify: `.docs/standards/api-response-formats.md`
- Modify: `.docs/standards/api-versioning-guidelines.md`
- Modify: `.docs/standards/cancellation-token-guidelines.md`
- Modify: `.docs/standards/collection-types-guidelines.md`
- Modify: `.docs/standards/database-connection-settings-guidelines.md`
- Modify: `.docs/standards/logging-strategy-recommendations.md`
- Modify: `.docs/standards/naming-guidelines.md`
- Modify: `.docs/standards/security-scanning.md`
- Modify: `.docs/standards/utc-time-guidelines.md`
- Modify: `.docs/standards/xml-comments-guidelines.md`

**Interfaces:**
- Consumes: boilerplate standards, GSS architecture tests, public interfaces, logging helpers, and configuration.
- Produces: mandatory rules that match both implementation and enforcement.

- [ ] **Step 1: Port API, versioning, cancellation, and collection rules**

Use GSS signatures and examples. Remove EF-style examples from Dapper sections, distinguish framework overrides that cannot accept a token, and keep `IReadOnlyList<T>`/materialization guidance consistent with actual interfaces.

- [ ] **Step 2: Port database, logging, naming, and security rules**

Preserve the current user corrections and verify every default, EventId range, script message, package command, and configuration key against GSS.

- [ ] **Step 3: Fix UTC rules**

Require injected `TimeProvider` for all source wall-clock reads, matching `ClockUsageTests`; keep converter/model-binder/session normalization behavior. Do not permit `DateTime.UtcNow` or `DateTimeOffset.UtcNow` under `src/`.

- [ ] **Step 4: Replace fabricated XML-comment examples**

Use actual GSS controllers/DTOs/interfaces or label neutral snippets as illustrative. Align public OpenAPI text rules with `OpenApiIntegrationTests`; remove claims that nonexistent Member/Assignment controllers are examples from the codebase.

- [ ] **Step 5: Run standards and full architecture tests**

Run Task 1 tests and the full architecture suite. Require all standards to pass the precedence/reference/template rules.

### Task 6: Port Testing Documentation

**Files:**
- Modify: `.docs/testing/architecture-testing-guide.md`
- Modify: `.docs/testing/testing-comprehensive-guide.md`

**Interfaces:**
- Consumes: boilerplate testing docs, actual GSS test project inventory, scripts, traits, fixtures, and current test output.
- Produces: accurate test-selection, isolation, coverage, and architecture-rule guidance.

- [ ] **Step 1: Rebuild the architecture testing inventory**

List every current architecture-test file and what it enforces, including `DocumentationTests`. Remove hard-coded runtime promises; show commands instead.

- [ ] **Step 2: Correct comprehensive testing claims**

Remove unsupported “compliant” labels and coverage-gate assertions. State that tests which write reset the shared test database, describe the real fixture/collection model, and distinguish the current stack from optional recommendations such as Testcontainers.

- [ ] **Step 3: Reconcile the user's determinism correction**

Ensure the final isolation language consistently says writing tests reset first; remove the contradictory “each test resets” statement.

- [ ] **Step 4: Run documentation and test-script checks**

Run focused documentation tests and `pwsh scripts/run-tests.ps1 -TestType Architecture`.

### Task 7: Mark Historical Plans and Specs as Archival

**Files:**
- Modify: `docs/superpowers/plans/2026-11-06-gss-clean-enterprise-scaffold.md`
- Modify: `docs/superpowers/plans/2026-11-17-mis-multi-division.md`
- Modify: `docs/superpowers/plans/2026-11-17-set-based-dapper-batches.md`
- Modify: `docs/superpowers/plans/2026-11-17-transaction-startup-lifecycle.md`
- Modify: `docs/superpowers/plans/2026-11-19-boilerplate-range-port.md`
- Modify: `docs/superpowers/specs/2026-11-06-gss-clean-enterprise-scaffold-design.md`
- Modify: `docs/superpowers/specs/2026-11-17-set-based-dapper-batches-design.md`
- Modify: `docs/superpowers/specs/2026-11-17-transaction-startup-lifecycle-design.md`

**Interfaces:**
- Consumes: historical content and current `.docs` entry point.
- Produces: clearly archival artifacts that cannot be mistaken for current implementation instructions.

- [ ] **Step 1: Add archival/status notices**

At the top of each pre-existing artifact, state its date, historical purpose, that unchecked boxes are not current status, and that current guidance lives in `.docs/README.md` and `CLAUDE.md`.

- [ ] **Step 2: Neutralize forbidden source-template terminology**

Replace named source-template commerce paths/types with neutral provenance descriptions or GSS equivalents where the plan already describes the resulting GSS artifact. Preserve the historical decision and task intent.

- [ ] **Step 3: Run the repository-wide residue test**

Run `TrackedMarkdown_HasNoSourceTemplateCommerceResidue` and require it to pass across root, `.docs`, specialized READMEs, plans, and specs.

### Task 8: Full Verification and Audit Closure

**Files:**
- Verify all files modified in Tasks 1–7.
- Do not modify unrelated source or configuration.

**Interfaces:**
- Consumes: the complete reconciled documentation set and its automated checks.
- Produces: fresh evidence for every success criterion in the approved design.

- [ ] **Step 1: Run documentation-specific proof**

Run the focused `DocumentationTests`; search tracked Markdown case-insensitively for forbidden residue; inspect all reported repository paths and local links.

- [ ] **Step 2: Run repository gates**

Run:

```bash
dotnet format GSS.slnx --no-restore --verify-no-changes
dotnet build GSS.slnx --no-restore
dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj --no-restore
dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj --no-restore
pwsh scripts/verify-scaffold.ps1
docker compose config --quiet
git diff --check
```

Expected: every deterministic command exits 0 with zero test failures and zero build warnings.

- [ ] **Step 3: Attempt integration verification**

Run `dotnet test tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj --no-restore`. If PostgreSQL rejects the configured credentials before test execution, report that environmental limitation separately and do not claim integration success.

- [ ] **Step 4: Inspect the final diff against both baselines**

Confirm the reference checkout is unchanged; confirm the original eight user edits are preserved in intent; confirm only documentation and `DocumentationTests.cs` changed in GSS; confirm no boilerplate-only documents were added.

- [ ] **Step 5: Report exact closure**

List rewritten documents, automated guardrails added, deterministic verification counts, integration status, and any intentionally omitted boilerplate-only material.
