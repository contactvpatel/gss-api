#!/usr/bin/env pwsh
# Pre-commit hook: verifies formatting of the staged C# files and blocks the commit if any need it.
# Read-only: never formats or stages files, so `git add -p` partial stages stay intact.
# Vulnerability checks are separate: pwsh scripts/security-audit.ps1

$ErrorActionPreference = "Stop"

# Colors for output (cross-platform compatible)
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )

    try {
        Write-Host $Message -ForegroundColor $Color
    } catch {
        Write-Host $Message
    }
}

# Get the repository root directory
try {
    $RepoRoot = (git rev-parse --show-toplevel)
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        Write-Error "Error: Could not determine repository root directory"
        exit 1
    }
    Set-Location $RepoRoot
} catch {
    Write-Error "Error: Not a git repository or git not found: $_"
    exit 1
}

Write-ColorOutput "Running pre-commit checks..." "Green"
Write-ColorOutput "Step 1: Verifying formatting of staged C# files..." "Yellow"

# Staged C# files (added, copied, modified, renamed), relative to the repo root.
$stagedFiles = @(git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR)
if ($LASTEXITCODE -ne 0) {
    Write-ColorOutput "✗ Could not list staged files" "Red"
    exit 1
}
$stagedCsFiles = @($stagedFiles | Where-Object { $_ -match '\.cs$' })

if ($stagedCsFiles.Count -eq 0) {
    Write-ColorOutput "  No staged C# files; skipping format check" "Green"
    exit 0
}
Write-Host "  $($stagedCsFiles.Count) staged C# file(s)"

# Find all solution files in the repository root (.sln and .slnx)
$SolutionFiles = @(Get-ChildItem -Path $RepoRoot -File | Where-Object {
    $_.Extension -match '^\.(sln|slnx)$'
} | Sort-Object Name)

if ($SolutionFiles.Count -eq 0) {
    Write-ColorOutput "⚠ No solution files found in repository root; skipping format check" "Yellow"
    exit 0
}

$formattingFailed = $false
$allFormatResults = @()

foreach ($SolutionFile in $SolutionFiles) {
    $SolutionName = $SolutionFile.Name
    Write-Host "  Checking: $SolutionName"

    try {
        # dotnet format needs restored packages.
        $restoreResult = dotnet restore $SolutionFile.FullName --verbosity quiet 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            Write-ColorOutput "    ✗ Package restore failed with exit code: $LASTEXITCODE" "Red"
            $formattingFailed = $true
            $allFormatResults += "Solution: $SolutionName`nRestore failed:`n$restoreResult"
            continue
        }

        # Exit code 2 = files need formatting. Files outside this solution are ignored.
        $formatResult = dotnet format $SolutionFile.FullName --verify-no-changes --no-restore --verbosity minimal --include @stagedCsFiles 2>&1 | Out-String
        $formatExitCode = $LASTEXITCODE

        # Exit code 0 can still report unfixable diagnostics.
        $hasWarnings = $formatResult -match '(warning\s+[A-Z]+\d+|error\s+[A-Z]+\d+|Failed to|Exception|Unable to|Formatting failed|Build failed)'

        if ($formatExitCode -ne 0 -or $hasWarnings) {
            Write-ColorOutput "    ✗ Formatting issues detected (exit code $formatExitCode)" "Red"
            $formattingFailed = $true
            $allFormatResults += "Solution: $SolutionName`n$formatResult"
        } else {
            Write-ColorOutput "    ✓ Formatting OK" "Green"
        }
    } catch {
        Write-ColorOutput "    ✗ Format check failed: $_" "Red"
        $formattingFailed = $true
        $allFormatResults += "Solution: $SolutionName`nError: $_"
    }
}

if ($formattingFailed) {
    Write-ColorOutput "" "Red"
    Write-ColorOutput "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" "Red"
    Write-ColorOutput "✗ COMMIT BLOCKED: Code formatting issues detected" "Red"
    Write-ColorOutput "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" "Red"
    Write-Host ""
    Write-Host "Formatting results:"
    Write-Host ($allFormatResults -join "`n`n")
    Write-Host ""
    Write-Host "Nothing was changed or staged. Format the files, review, stage and commit again:"
    Write-Host "  dotnet format $($SolutionFiles[0].Name) --include $($stagedCsFiles -join ' ')"
    Write-Host "The check reads the working-tree copy, so unstaged edits in a staged file count too."
    exit 1
}

Write-ColorOutput "" "Green"
Write-ColorOutput "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" "Green"
Write-ColorOutput "✓ All pre-commit checks passed" "Green"
Write-ColorOutput "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" "Green"
Write-Host ""

exit 0
