# Security Scanning Standard

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Local Commands](#local-commands)
- [What the Audit Does](#what-the-audit-does)
- [Pre-commit and CI Boundaries](#pre-commit-and-ci-boundaries)
- [Handling Findings](#handling-findings)
- [Limitations](#limitations)
- [References](#references)

---

## Overview

Developers MUST review dependency and secret-scanning results before release. The repository provides local helper scripts, but a passing helper is not proof that the code is secure. Organization security policy and the cited OWASP guidance takes precedence if this document conflicts with them.

## Local Commands

Fast dependency audit:

```bash
pwsh scripts/security-audit.ps1 -Fast
# Successful final line: Fast security scan PASSED
```

Full local audit:

```bash
pwsh scripts/security-audit.ps1
```

Optional tool installer:

```bash
pwsh scripts/install-security-tools.ps1
pwsh scripts/install-security-tools.ps1 -All
```

## What the Audit Does

Fast mode runs:

```bash
dotnet package list --project GSS.slnx --vulnerable --include-transitive --format json
```

A reported vulnerable package or an incomplete scan fails. Feed/authentication/network failure means the result is unknown and also fails; it is not a clean scan.

Full mode adds:

1. a heuristic pattern scan of selected JSON/config/C# files under `src/`. Matches are warnings requiring human review and do not alone fail the script;
2. `git-secrets --scan` of repository files when the optional executable is installed. A finding fails; absence is reported and skipped.

The full script does not claim to scan all Git history. `scripts/verify-scaffold.ps1` separately inspects reachable `src/GSS.Api/appsettings.json` revisions for non-placeholder MIS/ASM application credentials.

## Pre-commit and CI Boundaries

The installed pre-commit hook checks formatting of staged C# files only. It never runs the vulnerability audit and never formats/stages files. Do not document or rely on security checks running during `git commit` or `git push`.

Jenkins/shared infrastructure may run additional controls. Treat the active pipeline configuration and organization security requirements as authoritative; do not infer a CI gate from the local script.

## Handling Findings

- Verify the advisory/finding and affected package path.
- Prefer an upgraded direct/transitive dependency version approved for the repository.
- Run restore, build, unit, architecture, and relevant integration tests after changing dependencies.
- Never suppress an advisory solely to make the script pass. Record a risk acceptance with owner, scope, compensating controls, and expiry when remediation is not immediate.
- If a secret was committed, revoke/rotate it first, then remove it from current files and follow the approved history-remediation process. Deleting the current line does not invalidate an exposed credential.
- Treat heuristic credential matches as review prompts; confirm before changing legitimate placeholders/test data.

## Limitations

The local audit does not provide SAST, DAST, container/image scanning, infrastructure scanning, license compliance, malware detection, or comprehensive secret/history detection. SonarQube is a separate optional SAST workflow and the repository scanner uploads no test coverage by itself.

Never place real tokens in command history, tracked configuration, docs, test snapshots, or scan output attached to tickets.

## References

- [NuGet audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)
- [OWASP Software Component Verification Standard](https://owasp.org/www-project-software-component-verification-standard/)
- [Repository scripts](../../scripts/README.md)
