# GSS Documentation Reconciliation Design

## Intent

Replace the current mixed-quality documentation with a strict port of the current `boilerplate-net` documentation baseline, adapted to the GSS repository and verified against the GSS implementation. The result must contain no source-template commerce names, paths, schemas, endpoints, entities, or claims, while preserving GSS-specific behavior and the useful corrections already present in the working tree.

## Goals

- Reconcile every tracked Markdown document, including root guidance, `.docs/`, specialized READMEs, and historical plan/spec artifacts.
- Use the matching document in `/Users/vishalpatel/Projects/boilerplate-net` as the starting point wherever one exists.
- Replace source-template namespaces, paths, project names, schemas, commands, and terminology with their real GSS equivalents.
- Remove boilerplate feature material when GSS has no corresponding implementation; do not manufacture GSS entities, repositories, endpoints, migrations, benchmarks, or tests.
- Label illustrative domain examples explicitly and keep them consistent with the domain-neutral scaffold.
- Fix every issue found in the 2026-11-19 documentation audit: stale implementation descriptions, nonexistent files and services, unsupported performance claims, UTC and Mapster contradictions, misleading testing claims, template violations, and ambiguous historical documents.
- Add automated documentation checks so broken local links, missing index entries, required structural sections, and source-template residue cannot silently return.

## Non-goals

- No production behavior, API contract, database schema, or package configuration changes merely to make old prose true.
- No import of boilerplate load-test, schema-diagram, domain-feature, or operational documentation for capabilities GSS does not ship.
- No verification of external web links at test time; documentation tests remain deterministic and offline.
- No attempt to preserve document length. Correct, concise documentation is preferred over retaining obsolete tutorial text.

## Anti-goals

- A mechanical global replacement from the source product name to `GSS` that leaves fictional GSS features behind.
- Copying the dirty template-review working-tree file from the reference repository.
- Reverting the user's existing verified edits in the eight modified GSS documents.
- Treating historical plans as current operating instructions.
- Keeping benchmark percentages, “production-ready,” “complete,” or “compliant” labels without repository evidence.

## Constraints

- The GSS source, configuration, tests, and scripts are authoritative when they differ from `boilerplate-net`.
- Reference baseline: `/Users/vishalpatel/Projects/boilerplate-net`, branch `additional-refactor`, current commit `b152ee3`. Its uncommitted template-review change is excluded.
- Preserve the current GSS working-tree changes by reconciling their intent into the ported documents rather than overwriting them blindly.
- Current repository conventions in `CLAUDE.md`, the `.codegraph` index, and architecture tests remain binding.
- Examples may use neutral placeholder types such as `Member` or `Item` only when the surrounding text says they are illustrative and not shipped.
- The final tracked documentation must contain no source-template product-name text or paths.

## Approaches Considered

### 1. Implementation-first rewrite

Rewrite the inaccurate documents directly from GSS code and use boilerplate only for ideas. This minimizes imported assumptions but diverges from the requested base and requires recreating substantial explanatory material.

### 2. Strict boilerplate mirror with GSS reconciliation — selected

Start each matching document from the current boilerplate version, translate repository identity and shared infrastructure, remove domain-only material, then verify every implementation-bearing statement against GSS. This best satisfies the request while preventing a mechanical rename from inventing GSS features.

### 3. Prune and archive

Delete most long-form guides and keep only concise operational documentation. This has the smallest maintenance burden but loses the reference repository's intended documentation coverage.

## Documentation Model

### Current documentation

The root `README.md`, `CLAUDE.md`, `scripts/README.md`, mapping README, and `.docs/` index/topics describe the repository as it exists now. They may explain how to add a future feature, but must distinguish shipped behavior from illustrative extension patterns.

### Historical artifacts

Files under `docs/superpowers/plans/` and `docs/superpowers/specs/` remain historical evidence. Each pre-existing artifact receives a prominent archival/status notice stating that checkboxes and target paths record the plan at authoring time and that current implementation documentation lives under `.docs/`. Source-template commerce terminology is removed or generalized without rewriting the historical decision itself.

### Reference-only boilerplate material

Boilerplate documents without a GSS capability are not copied. In particular, domain schema diagrams, endpoint load-test documentation, and feature-specific repository/controller examples are omitted unless GSS has the corresponding scripts and implementation.

## Reconciliation Rules

1. Port the latest matching boilerplate document.
2. Translate only real shared concepts: namespaces, project paths, schema name, service identity, commands, and test project names.
3. Compare every class, method, file, endpoint, configuration key, and command to the GSS working tree.
4. Remove feature-specific claims when the corresponding GSS symbol or file does not exist.
5. Replace useful feature examples with a short, explicitly illustrative example that follows the real GSS base interfaces.
6. Remove unsupported quantitative claims unless a committed benchmark or test result proves them.
7. Preserve verified GSS divergences, including domain neutrality, the GSS migration inventory, test-database behavior, current external services, idempotency probe usage, and current documentation corrections.
8. Apply `.docs/documentation-standards.md` consistently: title, canonical back link, TOC for four or more `##` sections, overview, related references, correct index entry, and valid local links.

## Automated Enforcement

Add an architecture-test document checker that reads tracked/current documentation without network access and verifies:

- every `.docs` topic is listed exactly once in `.docs/README.md`;
- local Markdown link targets exist;
- `.docs` topic documents use the required back link and structural sections;
- standards include the required precedence statement;
- fenced code blocks are balanced;
- current documentation contains no source-template product-name residue;
- code-like repository paths explicitly presented as current resolve to real files, while fenced illustrative placeholders are exempt.

The existing scaffold verifier remains responsible for repository identity and source-template residue outside the more detailed Markdown checks.

## Verification

- Run the documentation architecture tests first.
- Search all tracked documentation for the source-template product name, case-insensitively, and require no matches.
- Validate local Markdown links and index coverage.
- Parse all PowerShell scripts and validate Docker Compose configuration because the docs instruct users to run them.
- Run formatting, build, unit tests, architecture tests, and scaffold verification.
- Run integration tests when valid PostgreSQL credentials are available; otherwise report the credential failure separately and make no integration-pass claim.
- Run `git diff --check` and inspect the final diff to confirm only documentation and its focused validation tests changed.

## Success Criteria

- Every audit finding is either corrected in current documentation or explicitly resolved by an archival/status notice.
- No current document names a nonexistent GSS file, class, service, endpoint, or measured result as real.
- No tracked documentation contains source-template commerce material.
- The documentation index and required structure are internally consistent.
- The reference repository remains read-only and unchanged.
- All available deterministic verification gates pass, with unavailable integration verification reported exactly.
