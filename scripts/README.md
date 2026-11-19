# Repository Scripts

This directory contains the PowerShell entry points used for local verification, tests, coverage, security checks, SonarQube analysis, and scaffold validation.

## Prerequisites

- PowerShell 7 (`pwsh`) for the repository commands below.
- The .NET SDK selected by `global.json`.
- Restored private-feed packages where a command builds or tests the solution.
- PostgreSQL for integration tests.

Run commands from the repository root.

## Script Reference

| Script | What it does | Typical command |
| --- | --- | --- |
| `install-pre-commit-hook.ps1` | Installs the repository's formatting hook; `-Force` skips the overwrite prompt. | `pwsh scripts/install-pre-commit-hook.ps1` |
| `pre-commit-hook.ps1` | Restores root solutions and runs `dotnet format --verify-no-changes` for staged C# files. | Installed by the script above. |
| `run-tests.ps1` | Runs unit plus architecture, architecture only, or integration tests. | `pwsh scripts/run-tests.ps1 -TestType Unit` |
| `run-coverage.ps1` | Runs unit and/or integration tests with XPlat coverage; optionally builds an HTML report. | `pwsh scripts/run-coverage.ps1 -TestType Unit -ReportType Html` |
| `security-audit.ps1` | Checks vulnerable NuGet packages; full mode also performs a heuristic credential scan and optional `git-secrets --scan`. | `pwsh scripts/security-audit.ps1 -Fast` |
| `install-security-tools.ps1` | Offers to install the optional `git-secrets` executable. | `pwsh scripts/install-security-tools.ps1` |
| `sonarqube-scan.ps1` | Runs a Release build inside SonarScanner and uploads analysis. It does not run tests or upload coverage. | `pwsh scripts/sonarqube-scan.ps1` |
| `verify-scaffold.ps1` | Enforces the eight-project domain-neutral GSS scaffold and rejects tracked build output, source-template residue, feature SQL, and reachable service credentials. | `pwsh scripts/verify-scaffold.ps1` |

## Pre-commit Formatting Hook

Install the hook with:

```bash
pwsh scripts/install-pre-commit-hook.ps1
```

The installed hook:

1. Lists staged added, copied, modified, or renamed C# files.
2. Exits successfully when no C# file is staged.
3. Restores each solution in the repository root.
4. Runs `dotnet format --verify-no-changes --no-restore --include ...` for the staged C# paths.
5. Blocks the commit if formatting fails or the command reports a warning or error.

The hook reads the working-tree copy of each staged file. Unstaged edits in a partially staged C# file can therefore block the commit. It never formats or stages files, and it does not run the security audit.

## Tests

```bash
pwsh scripts/run-tests.ps1 -TestType Unit
pwsh scripts/run-tests.ps1 -TestType Architecture
pwsh scripts/run-tests.ps1 -TestType Integration
```

`-TestType Unit` runs `GSS.UnitTests` followed by `GSS.ArchitectureTests`. `-TestType Architecture` runs only the architecture project. `-TestType Integration` runs `GSS.IntegrationTests` with `tests/runsettings.integration.xml`; that run is sequential because the tests share the `gss_test` database.

Add `-CollectCoverage` to unit or integration runs to write Cobertura output below the applicable test project's `TestResults` directory. Architecture tests are excluded from coverage.

## Coverage Reports

```bash
pwsh scripts/run-coverage.ps1 -TestType Unit -ReportType Summary
pwsh scripts/run-coverage.ps1 -TestType Integration -ReportType Html
pwsh scripts/run-coverage.ps1 -TestType All -ReportType Html
```

Accepted test types are `Unit`, `Integration`, and `All`. Accepted report types are `Summary` and `Html`. HTML generation requires the `reportgenerator` command:

```bash
dotnet tool install --global dotnet-reportgenerator-globaltool
```

Output directories are `coverage-report-unit`, `coverage-report-integration`, or `coverage-report`, depending on the selected test type.

## Security Audit

Fast mode runs only the NuGet vulnerability query:

```bash
pwsh scripts/security-audit.ps1 -Fast
```

Full mode runs three checks:

1. `dotnet package list --vulnerable --include-transitive` for `GSS.slnx`. A reported vulnerability or an incomplete scan fails the audit.
2. A heuristic scan of selected files under `src/` for credential-like assignments. Findings are warnings and require human review; this check alone does not fail the audit.
3. `git-secrets --scan` of repository files when `git-secrets` is installed. Findings fail the audit; a missing executable is reported as optional.

```bash
pwsh scripts/security-audit.ps1
pwsh scripts/install-security-tools.ps1 -All
```

This is a local aid, not proof that the repository is free of secrets or vulnerabilities. See the [security scanning standard](../.docs/standards/security-scanning.md).

## SonarQube and SonarCloud

The scanner requires `SONAR_TOKEN`. Configuration comes from environment variables first, then `sonar-config.json`, then an interactive prompt where supported.

```bash
export SONAR_TOKEN="<token>"
pwsh scripts/sonarqube-scan.ps1
```

Supported environment overrides are `SONAR_HOST_URL`, `SONAR_PROJECT_KEY`, and `SONAR_ORG_KEY`. The script rejects `sonar.token`, `sonar.login`, or `sonar.password` inside `analysis.beginParameters`. It passes the token through `SONARQUBE_SCANNER_PARAMS`, installs `dotnet-sonarscanner` as a global tool if the command is absent, runs a Release build, and uploads the result. Run `pwsh scripts/sonarqube-scan.ps1 -Help` for the complete configuration order.

## Scaffold Verification

```bash
pwsh scripts/verify-scaffold.ps1
```

The verifier checks the exact five source and three test projects, tracked generated artifacts, source-template and sample-commerce residue, unexpected feature migrations or seeds, and non-placeholder MIS/ASM application credentials reachable in Git history. It is intentionally stricter than a build.

## References

- [Documentation index](../.docs/README.md)
- [Testing guide](../.docs/testing/testing-comprehensive-guide.md)
- [SonarQube setup guide](../.docs/guides/sonarqube-setup-guide.md)
