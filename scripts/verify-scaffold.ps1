[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $RepositoryRoot).Path
$violations = [System.Collections.Generic.List[string]]::new()

function Add-Violation {
    param([string]$Message)

    $violations.Add($Message)
}

$expectedProjects = @(
    'src/GSS.Api/GSS.Api.csproj',
    'src/GSS.Business/GSS.Business.csproj',
    'src/GSS.Core/GSS.Core.csproj',
    'src/GSS.Infrastructure/GSS.Infrastructure.csproj',
    'src/GSS.Util/GSS.Util.csproj',
    'tests/GSS.ArchitectureTests/GSS.ArchitectureTests.csproj',
    'tests/GSS.IntegrationTests/GSS.IntegrationTests.csproj',
    'tests/GSS.UnitTests/GSS.UnitTests.csproj'
)

$solutionPath = Join-Path $root 'GSS.slnx'
if (-not (Test-Path $solutionPath -PathType Leaf)) {
    Add-Violation 'Missing GSS.slnx.'
}
else {
    [xml]$solution = Get-Content $solutionPath -Raw
    $actualProjects = @(
        $solution.SelectNodes('//Project') |
            ForEach-Object { $_.GetAttribute('Path').Replace('\', '/') } |
            Sort-Object
    )
    $projectDifference = Compare-Object ($expectedProjects | Sort-Object) $actualProjects
    if ($projectDifference) {
        Add-Violation "GSS.slnx project inventory differs: $($projectDifference | Out-String)"
    }
}

$trackedFiles = @(& git -C $root ls-files)
if ($LASTEXITCODE -ne 0) {
    Add-Violation 'Unable to inspect tracked files with git ls-files.'
}

$generatedPathPattern = '(^|/)(bin|obj|TestResults)(/|$)|(^|/)\.DS_Store$'
foreach ($path in $trackedFiles | Where-Object { $_ -match $generatedPathPattern }) {
    Add-Violation "Generated or machine-local artifact is tracked: $path"
}

$repositoryFiles = @(
    Get-ChildItem -Path $root -Recurse -File -Force |
        ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } |
        Where-Object {
            $_ -notmatch '(^|/)(\.git|\.superpowers|\.worktrees)(/|$)' -and
            $_ -notmatch $generatedPathPattern
        }
)

foreach ($path in $repositoryFiles | Where-Object { $_ -match '(?i)webshop' }) {
    Add-Violation "Source-template name remains in a repository path: $path"
}

$commercePathPattern = '(?i)(^|/)(Create|Update|I)?(Address|Article|Color|Customer|Label|Order|OrderPosition|Product|Size|Stock)(Controller|Dto|Repository|Service|Validator|Tests?)?\.(cs|sql)$'
foreach ($path in $repositoryFiles | Where-Object { $_ -match '^(src|tests)/' -and $_ -match $commercePathPattern }) {
    Add-Violation "Commerce artifact remains in a production/test path: $path"
}

# The one generic infrastructure script the scaffold ships (Idempotency-Key storage); any other is a feature artifact.
$allowedSqlArtifacts = @('src/GSS.Infrastructure/DbUp/Migrations/20261119-140000-GSS-001-Idempotency-Key.sql')
$sqlArtifacts = $trackedFiles | Where-Object {
    $_ -match '^src/GSS\.Infrastructure/DbUp/(Migrations|Seeds)/.+\.sql$' -and $_ -notin $allowedSqlArtifacts
}
foreach ($path in $sqlArtifacts) {
    Add-Violation "Scaffold must start without committed migrations or seeds: $path"
}

$credentialFields = @(
    @{ Section = 'MisService'; Name = 'AuthAppId' },
    @{ Section = 'MisService'; Name = 'AuthAppSecret' },
    @{ Section = 'AsmService'; Name = 'AuthAppId' },
    @{ Section = 'AsmService'; Name = 'AuthAppSecret' }
)
$historyRevisions = @(& git -C $root rev-list --all)
foreach ($revision in $historyRevisions) {
    $settingsJson = & git -C $root show "${revision}:src/GSS.Api/appsettings.json" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $settingsJson) {
        continue
    }

    try {
        $settings = $settingsJson | ConvertFrom-Json
    }
    catch {
        Add-Violation "Unable to parse src/GSS.Api/appsettings.json from reachable commit $revision."
        continue
    }

    foreach ($field in $credentialFields) {
        $value = $settings.($field.Section).Headers.($field.Name)
        if (-not [string]::IsNullOrWhiteSpace($value) -and $value -ne 'set-via-environment') {
            Add-Violation "Non-placeholder $($field.Section):Headers:$($field.Name) is reachable in commit $revision."
        }
    }
}

$textExtensions = @(
    '.cs', '.csproj', '.config', '.example', '.json', '.md', '.props', '.ps1',
    '.runsettings', '.slnx', '.sql', '.xml', '.yaml', '.yml'
)
$commerceIdentifierPattern = '(?i)\b(Create|Update|I)?(Address|Article|Color|Customer|Label|Order|OrderPosition|Product|Size|Stock)(Controller|Dto|Repository|Service|Validator|MappingConfig|ApiIntegrationTests|RepositoryTests|ServiceTests|ControllerTests)\b|\b(product catalog|customer management|order processing|shopping cart|web shop)\b|\[Description\("(CUST|PROD|ORD|ADDR|ART|STOCK|SIZE|COLOR|LABEL)"\)\]'

foreach ($relativePath in $repositoryFiles) {
    if ($relativePath -like 'docs/superpowers/*' -or $relativePath -eq 'scripts/verify-scaffold.ps1') {
        continue
    }

    $extension = [System.IO.Path]::GetExtension($relativePath)
    if ($textExtensions -notcontains $extension -and $extension -ne '') {
        continue
    }

    $path = Join-Path $root $relativePath
    if (-not (Test-Path $path -PathType Leaf)) {
        continue
    }

    $matches = Select-String -Path $path -Pattern '(?i)webshop' -AllMatches
    foreach ($match in $matches) {
        Add-Violation "Source-template name remains in ${relativePath}:$($match.LineNumber)"
    }

    if ($relativePath -match '^(src|tests|\.docs)/' -or $relativePath -in @('README.md', 'CLAUDE.md')) {
        $commerceMatches = Select-String -Path $path -Pattern $commerceIdentifierPattern -AllMatches
        foreach ($match in $commerceMatches) {
            Add-Violation "Commerce-specific identifier remains in ${relativePath}:$($match.LineNumber)"
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("GSS scaffold verification failed with {0} violation(s):`n- {1}" -f $violations.Count, ($violations -join "`n- "))
    exit 1
}

Write-Host 'GSS scaffold verification passed.'
