# EST-004: validate establishment planning artifacts.
# Usage:
#   powershell -File scripts/est004-validate.ps1

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
    "docs/acceptance-plans/PLAN-17-establishment-planning.md",
    "docs/acceptance-plans/execution/PLAN-17-EXECUTION.md",
    "scripts/est004-validate.ps1",
    "scripts/acceptance-establishment-planning.ps1",
    "src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptancePlanningPatch.cs",
    "src/Tests/HRManagement.UnitTests/Est004EstablishmentPlanningTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Est004EstablishmentPlanningIntegrationTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst10EstablishmentGapTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst11BranchGapScopeTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst12FinanceReconciliationTests.cs",
    "testdata/acceptance/establishment/br1-budget-sample.csv",
    "src/Services/HRCore/HR.Core.Domain/Entities/WorkforcePlanLine.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/FinanceBudgetImportBatch.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentMetricsService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentGapAnalysisService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/EstablishmentPlanningApiController.cs"
)

$migration = Get-ChildItem -Path (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations") -Filter "*EST004*" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $migration) { $failures += "missing EST004 migration" }

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

Write-Host "Running Est004 unit tests..."
Push-Location $RepoRoot
try {
    dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj --filter Est004 --no-restore 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { $failures += "Est004 unit tests failed" }

    Write-Host "Running Est004 integration tests..."
    dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj --filter Est004 --no-restore 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { $failures += "Est004 integration tests failed" }
} finally {
    Pop-Location
}

if ($failures.Count -eq 0) {
    Write-Host "EST-004 validation passed." -ForegroundColor Green
    exit 0
}

Write-Host "EST-004 validation failed:" -ForegroundColor Red
$failures | ForEach-Object { Write-Host " - $_" }
exit 1
