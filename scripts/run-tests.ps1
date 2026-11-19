#!/usr/bin/env pwsh

<#
.SYNOPSIS
Run unit, architecture or integration tests, optionally with code coverage.

.DESCRIPTION
Single script to run unit tests, architecture tests or integration tests.
Unit runs both GSS.UnitTests and GSS.ArchitectureTests: the architecture tests carry
[Trait("Category", TestCategories.Unit)] and need no database, so they belong in the default feedback loop;
BuildSettingsTests evaluates each project with MSBuild, which takes a few seconds. Use -TestType Architecture to run them on their own.
Integration tests run sequentially (shared gss_test DB).

.PARAMETER TestType
Unit, Architecture or Integration.

.PARAMETER CollectCoverage
If set, collects XPlat code coverage (writes coverage.cobertura.xml under tests/.../TestResults).
Coverage is never collected from the architecture tests: they only reflect over assembly metadata and would
contribute an all-but-empty report to the merge.

.EXAMPLE
pwsh scripts/run-tests.ps1 -TestType Unit
pwsh scripts/run-tests.ps1 -TestType Architecture
pwsh scripts/run-tests.ps1 -TestType Integration
pwsh scripts/run-tests.ps1 -TestType Unit -CollectCoverage
pwsh scripts/run-tests.ps1 -TestType Integration -CollectCoverage
#>

param(
    [ValidateSet('Unit', 'Architecture', 'Integration')]
    [string]$TestType = 'Unit',

    [switch]$CollectCoverage
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot/..

if ($TestType -eq 'Unit') {
    Write-Host "Running unit tests..."
    if ($CollectCoverage) {
        Write-Host "Coverage: tests/CodeCoverage.runsettings"
        dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj `
            --filter "Category=Unit" `
            --settings tests/CodeCoverage.runsettings `
            --collect:"XPlat Code Coverage" `
            --verbosity normal
    } else {
        dotnet test tests/GSS.UnitTests/GSS.UnitTests.csproj `
            --filter "Category=Unit" `
            --verbosity normal
    }

    if ($LASTEXITCODE -ne 0) {
        exit 1
    }

    Write-Host ""
    Write-Host "Running architecture tests..."
    dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj `
        --filter "Category=Unit" `
        --verbosity normal
} elseif ($TestType -eq 'Architecture') {
    Write-Host "Running architecture tests..."
    dotnet test tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj `
        --filter "Category=Unit" `
        --verbosity normal
} else {
    Write-Host "Running integration tests (sequential)..."
    if ($CollectCoverage) {
        dotnet test tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj `
            --filter "Category=Integration" `
            --settings tests/runsettings.integration.xml `
            --collect:"XPlat Code Coverage" `
            --verbosity normal
    } else {
        dotnet test tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj `
            --filter "Category=Integration" `
            --settings tests/runsettings.integration.xml `
            --verbosity normal
    }
}

if ($LASTEXITCODE -ne 0) {
    exit 1
}

if ($CollectCoverage -and $TestType -ne 'Architecture') {
    $dir = if ($TestType -eq 'Unit') { 'GSS.UnitTests' } else { 'GSS.IntegrationTests' }
    $files = @(Get-ChildItem -Path "tests" -Filter "coverage.cobertura.xml" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match $dir })
    Write-Host ""
    Write-Host "Coverage file(s):"
    $files | Select-Object -First 3 | ForEach-Object { Write-Host "  $($_.FullName)" }
    Write-Host ""
    Write-Host "Generate HTML report: pwsh scripts/run-coverage.ps1 -TestType $TestType -ReportType Html"
}
