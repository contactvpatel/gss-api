#!/usr/bin/env pwsh

<#
.SYNOPSIS
Installs the pre-commit hook (Windows, macOS, Linux).

.DESCRIPTION
Writes pre-commit into the repository's hooks directory (git rev-parse --git-path hooks: core.hooksPath if set,
shared by every worktree). The hook runs scripts/pre-commit-hook.ps1 of the worktree being committed: it verifies
the formatting of the staged C# files and blocks the commit if any need it. It never formats or stages files and runs
no vulnerability check. Works in PowerShell 7 and Windows PowerShell 5.1.

.PARAMETER Force
Overwrite an existing pre-commit hook without asking.

.EXAMPLE
pwsh scripts/install-pre-commit-hook.ps1
pwsh scripts/install-pre-commit-hook.ps1 -Force
#>

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Check if PowerShell Core is available
$PwshCommand = $null
if (Get-Command pwsh -ErrorAction SilentlyContinue) {
    $PwshCommand = "pwsh"
} elseif (Get-Command powershell -ErrorAction SilentlyContinue) {
    # Fallback to Windows PowerShell if pwsh not available
    $PwshCommand = "powershell"
    Write-Warning "PowerShell Core (pwsh) not found. Using Windows PowerShell (powershell)."
    Write-Warning "For cross-platform compatibility, install PowerShell Core: https://aka.ms/powershell"
} else {
    Write-Error "PowerShell not found. Please install PowerShell Core (pwsh) from https://aka.ms/powershell"
    exit 1
}

# Repository root (git is a native command: check its exit code, it does not throw).
$RepoRoot = git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or -not $RepoRoot) {
    Write-Error "Error: Not a git repository or git not found"
    exit 1
}

Set-Location $RepoRoot

# Two-argument Join-Path only: Windows PowerShell 5.1 has no -AdditionalChildPath.
$HookSource = Join-Path (Join-Path $RepoRoot "scripts") "pre-commit-hook.ps1"

# Check if hook source exists
if (-not (Test-Path $HookSource)) {
    Write-Error "Error: Pre-commit hook script not found at $HookSource"
    exit 1
}

# The hooks directory git uses: core.hooksPath if set, else the common .git/hooks (a worktree's .git is a file).
# A relative result is relative to the repository root; an absolute one is kept as is.
$GitHooksPath = git rev-parse --git-path hooks
if ($LASTEXITCODE -ne 0 -or -not $GitHooksPath) {
    Write-Error "Error: Could not resolve the git hooks directory"
    exit 1
}
$HooksDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($RepoRoot, $GitHooksPath))
New-Item -ItemType Directory -Path $HooksDir -Force | Out-Null

$HookTarget = Join-Path $HooksDir "pre-commit"

# Check if hook already exists
if ((Test-Path $HookTarget) -and -not $Force) {
    Write-Warning "Warning: Pre-commit hook already exists at $HookTarget"
    $response = Read-Host "Do you want to overwrite it? (y/N)"
    if ($response -ne "y" -and $response -ne "Y") {
        Write-Host "Installation cancelled."
        exit 0
    }
}

# Git runs hooks from the root of the worktree being committed, so a relative path picks that worktree's script
# (the hooks directory is shared by every worktree).
$HookContent = @"
#!/bin/sh
# Pre-commit hook - calls the PowerShell script (cross-platform)
exec $PwshCommand -File "scripts/pre-commit-hook.ps1"
"@

# Write hook file
[System.IO.File]::WriteAllText($HookTarget, $HookContent)

# Make the hook executable on macOS/Linux.
$isUnixSystem = $false
if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) {
    $isUnixSystem = -not $IsWindows
} else {
    # Fallback for Windows PowerShell 5.1: check if chmod command exists
    $isUnixSystem = (Get-Command chmod -ErrorAction SilentlyContinue) -ne $null
}

if ($isUnixSystem) {
    try {
        # Use native chmod command if available
        $chmodResult = & chmod +x $HookTarget 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Could not set executable permissions on hook file. You may need to run: chmod +x $HookTarget"
        }
    } catch {
        # Ignore if chmod fails - hook might still work
        Write-Warning "Could not set executable permissions on hook file: $_"
    }
}

Write-Host "✓ Pre-commit hook installed at $HookTarget" -ForegroundColor Green
Write-Host ""
Write-Host "The hook will now run on every commit and will:"
Write-Host "  1. Verify the formatting of the staged C# files (dotnet format --verify-no-changes)"
Write-Host "  2. Block the commit if any file needs formatting or the check reports a warning"
Write-Host ""
Write-Host "It never formats or stages files: run 'dotnet format GSS.slnx' and re-stage."
Write-Host "Vulnerable packages are not checked here: run 'pwsh scripts/security-audit.ps1 -Fast'."
Write-Host ""
Write-Host "PowerShell command used: $PwshCommand"
Write-Host ""
Write-Host "To bypass the hook (not recommended), use:"
Write-Host "  git commit --no-verify"
