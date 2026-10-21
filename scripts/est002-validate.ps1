# EST-002: validate establishment governance artifacts.
# Usage:
#   powershell -File scripts/est002-validate.ps1

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
    "docs/acceptance-plans/PLAN-15-establishment-governance.md",
    "scripts/est002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Est002EstablishmentGovernanceTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Est002EstablishmentGovernanceIntegrationTests.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/EstablishmentChangeRequest.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/EstablishmentChangeLog.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeApplier.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeLifecycleService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeWorkflowHandler.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/EstablishmentChangesController.cs",
    "src/Services/HRCore/HR.Core.Web/Views/EstablishmentChanges/Index.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/EstablishmentChanges/Details.cshtml",
    "src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260903215913_EST002_EstablishmentGovernance.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$wfFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs"
if (Test-Path $wfFile) {
    $wf = Get-Content -Raw -Path $wfFile
    if ($wf -notmatch "ESTABLISHMENT-CHANGE-APPROVAL") { $failures += "WorkflowPackSeeder must seed ESTABLISHMENT-CHANGE-APPROVAL" }
}

$policyFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs"
if (Test-Path $policyFile) {
    $policy = Get-Content -Raw -Path $policyFile
    if ($policy -notmatch "R-EST-SUBMIT") { $failures += "PolicyEngineDomainSeeds must include R-EST-SUBMIT" }
}

$wfCtrl = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs"
if (Test-Path $wfCtrl) {
    $ctrl = Get-Content -Raw -Path $wfCtrl
    if ($ctrl -notmatch "EstablishmentChangeWorkflowHandler") { $failures += "WorkflowInstancesController must wire EstablishmentChangeWorkflowHandler" }
}

Write-Host "Running Est002 unit tests..."
Push-Location $RepoRoot
try {
    dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj --filter Est002 --no-restore 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { $failures += "Est002 unit tests failed" }
} finally {
    Pop-Location
}

if ($failures.Count -eq 0) {
    Write-Host "EST-002 validation passed." -ForegroundColor Green
    exit 0
}

Write-Host "EST-002 validation failed:" -ForegroundColor Red
$failures | ForEach-Object { Write-Host " - $_" }
exit 1
