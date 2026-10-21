# EST-003: validate establishment temporal artifacts.
# Usage:
#   powershell -File scripts/est003-validate.ps1

param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()

$requiredFiles = @(
    "docs/acceptance-plans/PLAN-16-establishment-temporal.md",
    "scripts/est003-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Est003EstablishmentTemporalTests.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/JobCatalogVersion.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/FundedPositionVersion.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/JobCatalogVersionService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/FundedPositionVersionService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentAsOfService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/EstablishmentTemporalApiController.cs"
)

$migration = Get-ChildItem -Path (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations") -Filter "*EST003*" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $migration) { $failures += "missing EST003 migration" }

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$orgSvc = Get-Content -Raw -Path (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/OrgStructure/OrgUnitVersionService.cs")
if ($orgSvc -notmatch "ListPositionsAsOfAsync") { $failures += "OrgUnitVersionService must use temporal position as-of" }

Write-Host "Running Est003 unit tests..."
Push-Location $RepoRoot
try {
    dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj --filter Est003 --no-restore 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { $failures += "Est003 unit tests failed" }
} finally {
    Pop-Location
}

if ($failures.Count -eq 0) {
    Write-Host "EST-003 validation passed." -ForegroundColor Green
    exit 0
}

Write-Host "EST-003 validation failed:" -ForegroundColor Red
$failures | ForEach-Object { Write-Host " - $_" }
exit 1
