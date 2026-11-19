#!/usr/bin/env pwsh

<#
.SYNOPSIS
Security Audit Script for Local Development

.DESCRIPTION
Optional local security checks before pushing to Git: vulnerable NuGet packages, hardcoded credentials and, if
installed, git-secrets. CI runs the shared Jenkins pipeline's own checks.

.PARAMETER Fast
Vulnerable NuGet packages only (skips the credential and git-secrets checks)

.PARAMETER Help
Show help message

.EXAMPLE
./scripts/security-audit.ps1              # Full audit
./scripts/security-audit.ps1 -Fast        # Quick scan
./scripts/security-audit.ps1 -Help        # Show help

.NOTES
Catches common issues locally; it does not replace the CI checks.
#>

param(
    [switch]$Fast,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# Colors for output (ANSI codes work in PowerShell Core on all platforms)
$RED = "`e[0;31m"
$GREEN = "`e[0;32m"
$YELLOW = "`e[1;33m"
$BLUE = "`e[0;34m"
$NC = "`e[0m"  # No Color

# Get script and project root directories
$ScriptDir = Split-Path -Parent $PSCommandPath
$ProjectRoot = Split-Path -Parent $ScriptDir

# Show help
if ($Help) {
    @"
Security Audit Script - GSS.Api

USAGE:
  ./scripts/security-audit.ps1 [OPTIONS]
  pwsh scripts/security-audit.ps1 [OPTIONS]

OPTIONS:
  -Fast              Vulnerable NuGet packages only
  -Help              Show this help message

DESCRIPTION:
  Local security checks before pushing to Git. They do not replace CI, which runs the shared
  Jenkins pipeline (jenkins-shared-pipelines); see its output for what it checks.

CHECKS PERFORMED:
  Default:
    ✓ Vulnerable NuGet packages, direct and transitive (fails the audit)
    ✓ Hardcoded credentials in src/ (*.json, *.config, *.cs; a warning only)
    ✓ git-secrets --scan of the repository's files (only if git-secrets is installed; fails the audit)

  Fast Mode (-Fast):
    ✓ Vulnerable NuGet packages only

  Local SAST: pwsh scripts/sonarqube-scan.ps1 (SonarCloud)

SECURITY SCANNING DOCUMENTATION:
  See .docs/standards/security-scanning.md for full details

"@
    exit 0
}

Write-Host ""
Write-Host "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
Write-Host "${BLUE}Security Audit - GSS.Api${NC}"
Write-Host "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"

# Change to project root
Push-Location $ProjectRoot

$Failed = $false

# ============================================================================
# 1. Dependency Vulnerability Scanning (All Projects)
# ============================================================================
Write-Host ""
Write-Host "${YELLOW}[1/3] Checking NuGet package vulnerabilities in all projects...${NC}"

$Solution = Join-Path $ProjectRoot "GSS.slnx"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "${RED}✗ dotnet CLI not found${NC}"
    $Failed = $true
} else {
    # Exit code is non-zero when restore or a source fails (e.g. a 401 from the private feed),
    # but stays 0 when vulnerabilities are found, so the JSON is checked as well.
    $stderrFile = New-TemporaryFile
    try {
        $json = dotnet package list --project $Solution --vulnerable --include-transitive --format json 2>$stderrFile.FullName | Out-String
        $exitCode = $LASTEXITCODE
        $stderr = (Get-Content -Raw $stderrFile.FullName) -as [string]
    } finally {
        Remove-Item $stderrFile.FullName -ErrorAction SilentlyContinue
    }

    $report = $null
    try {
        $report = $json | ConvertFrom-Json
    } catch {
        $report = $null
    }

    $problems = @()
    if ($report) {
        $problems = @(@($report.problems) + @($report.projects | ForEach-Object { $_.problems }) | Where-Object { $_ })
    }
    $errorProblems = @($problems | Where-Object { $_.level -eq "error" })

    if ($exitCode -ne 0 -or -not $report -or -not $report.projects -or $errorProblems.Count -gt 0) {
        Write-Host "${RED}✗ Vulnerability scan did not complete (exit code $exitCode): results unknown${NC}"
        $problems | ForEach-Object { Write-Host "    [$($_.level)] $($_.text)" }
        if (-not $report -and $json.Trim()) { Write-Host $json.Trim() }
        if ($stderr -and $stderr.Trim()) { Write-Host $stderr.Trim() }
        Write-Host "${YELLOW}Action: run 'dotnet restore GSS.slnx'. The private feed needs NUGET_AUTH_TOKEN with read:packages.${NC}"
        $Failed = $true
    } else {
        $problems | ForEach-Object { Write-Host "${YELLOW}  ⚠ [$($_.level)] $($_.text)${NC}" }

        $vulnerable = @(foreach ($project in $report.projects) {
            foreach ($framework in @($project.frameworks | Where-Object { $_ })) {
                foreach ($package in @(@($framework.topLevelPackages) + @($framework.transitivePackages) | Where-Object { $_ })) {
                    if ($package.vulnerabilities) {
                        [pscustomobject]@{
                            Project    = [System.IO.Path]::GetFileNameWithoutExtension($project.path)
                            Framework  = $framework.framework
                            Package    = $package.id
                            Version    = $package.resolvedVersion
                            Severity   = (@($package.vulnerabilities | ForEach-Object { $_.severity }) -join ", ")
                            Advisories = (@($package.vulnerabilities | ForEach-Object { $_.advisoryurl }) -join " ")
                        }
                    }
                }
            }
        })

        Write-Host "Scanned $(@($report.projects).Count) project(s)"
        Write-Host ""
        if ($vulnerable.Count -gt 0) {
            Write-Host "${RED}✗ Vulnerable packages detected:${NC}"
            $vulnerable | ForEach-Object {
                Write-Host "    $($_.Project) [$($_.Framework)]: $($_.Package) $($_.Version) ($($_.Severity)) $($_.Advisories)"
            }
            Write-Host "${YELLOW}Action: bump the version in Directory.Packages.props (or pin the transitive package there)${NC}"
            $Failed = $true
        } else {
            Write-Host "${GREEN}✓ All projects: No vulnerable packages found${NC}"
        }
    }
}

# If fast mode, skip remaining checks
if ($Fast) {
    Write-Host ""
    Write-Host "${YELLOW}Fast mode enabled - skipping the credential and git-secrets checks${NC}"
    Write-Host ""

    Pop-Location

    if ($Failed) {
        Write-Host "${RED}❌ Security audit FAILED${NC}"
        exit 1
    } else {
        Write-Host "${GREEN}✓ Fast security scan PASSED${NC}"
        exit 0
    }
}

# ============================================================================
# 2. Hardcoded Credentials Check (Warning - not blocking)
# ============================================================================
Write-Host ""
Write-Host "${YELLOW}[2/3] Scanning for hardcoded credentials...${NC}"

$credPatterns = @(
    'password.*=.*[' + "'" + '"].+[' + "'" + '"]',
    'apikey.*=.*[' + "'" + '"].+[' + "'" + '"]',
    'secret.*=.*[' + "'" + '"].+[' + "'" + '"]',
    'token.*=.*[' + "'" + '"].+[' + "'" + '"]',
    'aws_access_key',
    'aws_secret_key'
)

$foundCreds = $false

foreach ($pattern in $credPatterns) {
    try {
        $matches = @(Get-ChildItem -Path "$ProjectRoot/src" -Recurse -Include "*.json", "*.config", "*.cs" -File |
            Select-String -Pattern $pattern |
            Where-Object {
                $_.Path -notmatch "\.example" -and
                $_.Path -notmatch "\.template"
            })

        if ($matches.Count -gt 0) {
            Write-Host "${RED}⚠ Possible hardcoded credential found:${NC}"
            $matches | ForEach-Object { Write-Host "  $_" }
            $foundCreds = $true
        }
    } catch {
        # Continue if grep fails
    }
}

if ($foundCreds) {
    Write-Host "${YELLOW}⚠ Review: Use Vault/Key Vault for production credentials${NC}"
} else {
    Write-Host "${GREEN}✓ No obvious hardcoded credentials detected${NC}"
}

# ============================================================================
# 3. Git Secrets Check (if tool installed)
# ============================================================================
Write-Host ""
Write-Host "${YELLOW}[3/3] Scanning repository files with git-secrets...${NC}"

try {
    $gitSecretsPath = Get-Command git-secrets -ErrorAction SilentlyContinue
    if ($gitSecretsPath) {
        Write-Host "Running: git-secrets --scan"
        $gitSecretsOutput = git-secrets --scan 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "${GREEN}✓ git-secrets found no secrets${NC}"
        } else {
            Write-Host "${RED}✗ git-secrets found possible secrets${NC}"
            Write-Host $gitSecretsOutput
            $Failed = $true
        }
    } else {
        Write-Host "${YELLOW}ℹ git-secrets tool not installed (optional)${NC}"
        Write-Host "  Install: brew install git-secrets (macOS) or apt-get install git-secrets (Linux)"
    }
} catch {
    Write-Host "${YELLOW}ℹ git-secrets tool not installed (optional)${NC}"
    Write-Host "  Install: brew install git-secrets (macOS) or apt-get install git-secrets (Linux)"
}


# ============================================================================
# Summary
# ============================================================================
Write-Host ""
Write-Host "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"

Pop-Location

if ($Failed) {
    Write-Host "${RED}❌ Security audit FAILED${NC}"
    Write-Host ""
    Write-Host "Fix issues before committing to Git."
    exit 1
} else {
    Write-Host "${GREEN}✓ Local security checks PASSED${NC}"
    Write-Host ""
    exit 0
}
