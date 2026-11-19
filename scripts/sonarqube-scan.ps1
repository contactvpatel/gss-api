#!/usr/bin/env pwsh

<#
.SYNOPSIS
Local SonarQube SAST (Static Application Security Testing) scan: SonarCloud or a self-hosted SonarQube

.DESCRIPTION
Runs SonarScanner around a Release build and uploads the analysis. Settings come from sonar-config.json, overridden
by environment variables. The token is passed through the scanner's SONARQUBE_SCANNER_PARAMS environment variable,
never on a command line.

.PARAMETER Help
Show help message

.EXAMPLE
./scripts/sonarqube-scan.ps1                 # Run analysis
pwsh scripts/sonarqube-scan.ps1 -Help        # Show help

.NOTES
Requires:
- SONAR_TOKEN environment variable set (the script exits without it)
- Project key (and, for SonarCloud, organization key): environment, sonar-config.json, or a prompt
- dotnet-sonarscanner (installed as a global tool if missing)
- Network access to the SonarQube server
#>

param(
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# Colors for output
$RED = "`e[0;31m"
$GREEN = "`e[0;32m"
$YELLOW = "`e[1;33m"
$BLUE = "`e[0;34m"
$NC = "`e[0m"  # No Color

# Get script and project root directories
$ScriptDir = Split-Path -Parent $PSCommandPath
$ProjectRoot = Split-Path -Parent $ScriptDir
$SonarConfigPath = Join-Path $ProjectRoot "sonar-config.json"

# Show help
if ($Help) {
    @"
SonarQube SAST Analysis - SonarCloud or self-hosted SonarQube

USAGE:
  ./scripts/sonarqube-scan.ps1
  pwsh scripts/sonarqube-scan.ps1

OPTIONS:
  -Help              Show this help message

DESCRIPTION:
  Builds the solution (Release) under SonarScanner and uploads the analysis to SonarQube:
    ✓ Bugs and security vulnerabilities
    ✓ Security hotspots
    ✓ Code smells and duplicate code
  No tests run, so SonarCloud gets no coverage.

REQUIREMENTS:
  1. SONAR_TOKEN environment variable (required; the script exits without it). It reaches the scanner through
     SONARQUBE_SCANNER_PARAMS, so it never appears on a command line (ps, shell history)
  2. Project key; for SonarCloud also the organization key
  3. dotnet-sonarscanner (installed as a global tool if missing)

SETTINGS (environment variable, else sonar-config.json, else default):
  Server URL     SONAR_HOST_URL, else selfHosted.hostUrl when selfHosted.enabled, else sonarCloud.hostUrl
                 (default https://sonarcloud.io)
  Project key    SONAR_PROJECT_KEY, else projectKey, else a prompt
  Organization   SONAR_ORG_KEY, else sonarCloud.organizationKey, else a prompt (SonarCloud only)
  Name, version  projectName, projectVersion
  Sources        source.encoding, source.inclusions, source.exclusions
  Extra          analysis.beginParameters (each passed as /d:key=value to the begin step)

SETUP (SonarCloud):
  1. Create an account: https://sonarcloud.io
  2. Create the organization and project
  3. Generate a token: https://sonarcloud.io/account/security
  4. Set projectKey and sonarCloud.organizationKey in sonar-config.json (or the environment variables);
     for a self-hosted server set selfHosted.enabled and selfHosted.hostUrl instead
  5. Set SONAR_TOKEN in the environment
  6. Run: pwsh scripts/sonarqube-scan.ps1

"@
    exit 0
}

Write-Host "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
Write-Host "${BLUE}SonarQube SAST Analysis${NC}"
Write-Host "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
Write-Host ""

# ============================================================================
# 1. Check if SonarScanner is installed
# ============================================================================
Write-Host "${YELLOW}[1/4] Checking SonarScanner installation...${NC}"
Write-Host ""

try {
    $sonarScannerVersion = dotnet-sonarscanner --version 2>$null
    Write-Host "${GREEN}✓ SonarScanner found: $sonarScannerVersion${NC}"
} catch {
    Write-Host "${YELLOW}ℹ SonarScanner not found. Installing...${NC}"
    Write-Host ""

    try {
        Write-Host "Running: dotnet tool install --global dotnet-sonarscanner"
        dotnet tool install --global dotnet-sonarscanner
        Write-Host "${GREEN}✓ SonarScanner installed successfully${NC}"
        Write-Host ""
    } catch {
        Write-Host "${RED}✗ Failed to install SonarScanner${NC}"
        Write-Host ""
        Write-Host "Manual installation:"
        Write-Host "  dotnet tool install --global dotnet-sonarscanner"
        Write-Host ""
        exit 1
    }
}

# ============================================================================
# 2. Collect Configuration from sonar-config.json, Environment, or User Input
# ============================================================================
Write-Host ""
Write-Host "${YELLOW}[2/4] Collecting SonarQube configuration...${NC}"
Write-Host ""

# Load configuration from sonar-config.json if it exists
$configData = $null
if (Test-Path $SonarConfigPath) {
    try {
        $configData = Get-Content $SonarConfigPath -Raw | ConvertFrom-Json
        Write-Host "${GREEN}✓ Configuration file loaded: sonar-config.json${NC}"
    } catch {
        Write-Host "${YELLOW}⚠ Warning: Could not parse sonar-config.json, using environment variables${NC}"
    }
} else {
    Write-Host "${YELLOW}ℹ No sonar-config.json found, using environment variables${NC}"
}

# Priority: environment variables, then config file, then default or prompt.
$selfHosted = $configData -and $configData.selfHosted -and $configData.selfHosted.enabled -eq $true

$hostUrl = $env:SONAR_HOST_URL
if (-not $hostUrl -and $configData) {
    $hostUrl = if ($selfHosted) { $configData.selfHosted.hostUrl } else { $configData.sonarCloud.hostUrl }
}
if (-not $hostUrl) {
    $hostUrl = "https://sonarcloud.io"
}
$hostUrl = $hostUrl.TrimEnd('/')
$isSonarCloud = $hostUrl -match '^https://(www\.)?sonarcloud\.io$'

$projectKey = $env:SONAR_PROJECT_KEY
if (-not $projectKey -and $configData) {
    $projectKey = $configData.projectKey
}
if (-not $projectKey) {
    $projectKey = Read-Host "Enter Project Key (e.g., com.gss:api)"
}

$orgKey = $null
if ($isSonarCloud) {
    $orgKey = $env:SONAR_ORG_KEY
    if (-not $orgKey -and $configData) {
        $orgKey = $configData.sonarCloud.organizationKey
    }
    if (-not $orgKey) {
        $orgKey = Read-Host "Enter SonarCloud Organization Key (e.g., my-organization)"
    }
}

if (-not $projectKey -or ($isSonarCloud -and -not $orgKey)) {
    Write-Host "${RED}✗ Project key$(if ($isSonarCloud) { ' and organization key are' } else { ' is' }) required${NC}"
    exit 1
}

# Optional analysis properties from sonar-config.json (passed to the begin step as /d:key=value).
$analysisProperties = [ordered]@{}
if ($configData) {
    if ($configData.source.encoding) { $analysisProperties['sonar.sourceEncoding'] = $configData.source.encoding }
    if ($configData.source.inclusions) { $analysisProperties['sonar.inclusions'] = @($configData.source.inclusions) -join ',' }
    if ($configData.source.exclusions) { $analysisProperties['sonar.exclusions'] = @($configData.source.exclusions) -join ',' }
    if ($configData.analysis.beginParameters) {
        foreach ($property in $configData.analysis.beginParameters.PSObject.Properties) {
            $analysisProperties[$property.Name] = [string]$property.Value
        }
    }
}
foreach ($secret in @('sonar.token', 'sonar.login', 'sonar.password')) {
    if ($analysisProperties.Contains($secret)) {
        Write-Host "${RED}✗ sonar-config.json must not hold $secret; set SONAR_TOKEN instead${NC}"
        exit 1
    }
}

Write-Host "Server: $hostUrl$(if ($isSonarCloud) { ' (SonarCloud)' } else { ' (self-hosted)' })"
Write-Host "Project Key: $projectKey"
if ($isSonarCloud) { Write-Host "Organization: $orgKey" }
foreach ($key in $analysisProperties.Keys) { Write-Host "$key = $($analysisProperties[$key])" }
Write-Host "${GREEN}✓ Configuration valid${NC}"

# ============================================================================
# 3. Check authentication
# ============================================================================
Write-Host ""
Write-Host "${YELLOW}[3/4] Checking authentication...${NC}"
Write-Host ""

$sonarToken = $env:SONAR_TOKEN

if (-not $sonarToken) {
    Write-Host "${RED}✗ SONAR_TOKEN environment variable not set${NC}"
    Write-Host ""
    Write-Host "To authenticate:"
    Write-Host "  1. Sign in to $hostUrl"
    Write-Host "  2. Generate a token (SonarCloud: https://sonarcloud.io/account/security)"
    Write-Host "  3. Set environment variable:"
    Write-Host ""
    Write-Host "     Windows (PowerShell):"
    Write-Host '       $env:SONAR_TOKEN = "your-token-here"'
    Write-Host ""
    Write-Host "     macOS/Linux (bash):"
    Write-Host '       export SONAR_TOKEN="your-token-here"'
    Write-Host ""
    Write-Host "  4. Re-run this script"
    Write-Host ""
    exit 1
}

Write-Host "${GREEN}✓ SONAR_TOKEN is set${NC}"

# ============================================================================
# 4. Run SonarQube analysis
# ============================================================================
Write-Host ""
Write-Host "${YELLOW}[4/4] Running SonarQube analysis...${NC}"
Write-Host ""

Push-Location $ProjectRoot

try {
    # The scanner reads SONARQUBE_SCANNER_PARAMS in both steps, so the token never appears in a process's arguments.
    $env:SONARQUBE_SCANNER_PARAMS = (@{ 'sonar.token' = $sonarToken } | ConvertTo-Json -Compress)

    $beginArgs = @("begin", "/k:$projectKey", "/d:sonar.host.url=$hostUrl")
    if ($isSonarCloud) { $beginArgs += "/o:$orgKey" }
    if ($configData -and $configData.projectName) { $beginArgs += "/n:$($configData.projectName)" }
    if ($configData -and $configData.projectVersion) { $beginArgs += "/v:$($configData.projectVersion)" }
    foreach ($key in $analysisProperties.Keys) { $beginArgs += "/d:$key=$($analysisProperties[$key])" }

    Write-Host "${YELLOW}Initializing SonarScanner...${NC}"
    Write-Host "Command: dotnet-sonarscanner $($beginArgs -join ' ')  (token via SONARQUBE_SCANNER_PARAMS)"
    Write-Host ""

    & dotnet-sonarscanner @beginArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "${RED}✗ SonarScanner initialization failed (exit code: $LASTEXITCODE)${NC}"
        Write-Host ""
        Write-Host "Troubleshooting tips:"
        Write-Host "  1. Verify the server URL and project key (and organization for SonarCloud)"
        Write-Host "  2. Ensure SONAR_TOKEN is set and valid for $hostUrl"
        Write-Host "  3. Check that the project (and organization) exist on the server"
        Write-Host "  4. Run: pwsh scripts/sonarqube-scan.ps1 -Help for configuration options"
        Write-Host ""
        Remove-Item Env:SONARQUBE_SCANNER_PARAMS -ErrorAction SilentlyContinue
        Pop-Location
        exit 1
    }

    Write-Host ""
    Write-Host "${YELLOW}Building project...${NC}"
    dotnet build --configuration Release

    if ($LASTEXITCODE -ne 0) {
        Write-Host "${RED}✗ Build failed${NC}"
        Remove-Item Env:SONARQUBE_SCANNER_PARAMS -ErrorAction SilentlyContinue
        Pop-Location
        exit 1
    }

    Write-Host ""
    Write-Host "${YELLOW}Uploading analysis results...${NC}"

    dotnet-sonarscanner end
    $endExitCode = $LASTEXITCODE
    Remove-Item Env:SONARQUBE_SCANNER_PARAMS -ErrorAction SilentlyContinue

    if ($endExitCode -ne 0) {
        Write-Host "${RED}✗ Analysis upload failed${NC}"
        Pop-Location
        exit 1
    }

    Write-Host ""
    Write-Host "${GREEN}✓ Analysis complete${NC}"

} catch {
    Write-Host "${RED}✗ Analysis failed: $_${NC}"
    Remove-Item Env:SONARQUBE_SCANNER_PARAMS -ErrorAction SilentlyContinue
    Pop-Location
    exit 1
}

Pop-Location

# ============================================================================
# Summary and Next Steps
# ============================================================================
Write-Host ""
Write-Host "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
Write-Host ""

Write-Host "${GREEN}✓ SonarQube analysis complete!${NC}"
Write-Host ""
Write-Host "View results:"
Write-Host "  1. Visit: $hostUrl"
Write-Host "  2. Open the project ($projectKey)"
Write-Host "  3. Review issues and quality metrics"
Write-Host ""
Write-Host "Key metrics:"
Write-Host "  • Bugs & Vulnerabilities - Critical issues"
Write-Host "  • Code Smells - Maintainability issues"
Write-Host "  • Security Hotspots - Security-sensitive code"
Write-Host "  • Duplicates - Duplicate code blocks"
Write-Host "  (No coverage: this scan runs no tests)"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  • Review any issues flagged"
Write-Host "  • Fix high-priority security issues"
Write-Host "  • Run regularly to track improvements"
Write-Host ""

exit 0
