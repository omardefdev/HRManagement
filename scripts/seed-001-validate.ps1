<#
.SYNOPSIS
    SEED-001: validates national seed orchestration artifacts.
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$failures = @()

function Assert-FileExists([string]$Path, [string]$Label) {
    if (-not (Test-Path $Path)) {
        $script:failures += "MISSING: $Label -> $Path"
        Write-Host "  FAIL  $Label" -ForegroundColor Red
    } else {
        Write-Host "  OK    $Label" -ForegroundColor Green
    }
}

function Assert-FileContains([string]$Path, [string]$Pattern, [string]$Label) {
    if (-not (Test-Path $Path)) {
        $script:failures += "MISSING: $Label -> $Path"
        Write-Host "  FAIL  $Label (file missing)" -ForegroundColor Red
        return
    }
    $content = Get-Content $Path -Raw
    if ($content -notmatch $Pattern) {
        $script:failures += "MISSING PATTERN: $Label -> '$Pattern' not found in $Path"
        Write-Host "  FAIL  $Label (pattern not found)" -ForegroundColor Red
    } else {
        Write-Host "  OK    $Label" -ForegroundColor Green
    }
}

Write-Host "`n=== SEED-001 Master Data Validation ===" -ForegroundColor Cyan

Write-Host "`n--- Registry ---"
Assert-FileExists "$RepoRoot\src\Shared\HRManagement.SharedKernel\Seeding\SeedPackIds.cs" "SeedPackIds"
Assert-FileContains "$RepoRoot\src\Shared\HRManagement.SharedKernel\Seeding\SeedPackIds.cs" "NATIONAL-MANDATORY-v1" "National mandatory pack id"

Write-Host "`n--- Orchestrator ---"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Seeding\NationalSeedOrchestrator.cs" "NationalSeedOrchestrator"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Seeding\DemoSeedOrchestrator.cs" "DemoSeedOrchestrator"
Assert-FileContains "$RepoRoot\src\Services\HRCore\HR.Core.Web\Program.cs" "NationalSeedOrchestrator" "Program uses orchestrator"

Write-Host "`n--- Seeders ---"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\WorkflowPackSeeder.cs" "WorkflowPackSeeder"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\MdmReferenceSeeder.cs" "MdmReferenceSeeder"
Assert-FileContains "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\WorkflowPackSeeder.cs" "HIRE-WIZARD" "Hire workflow"
Assert-FileContains "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\WorkflowPackSeeder.cs" "IT-TICKET" "IT ticket workflow"
Assert-FileContains "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\PolicyEngineSeeder.cs" "ID-CARD-ISSUANCE" "Workflow binding"

Write-Host "`n--- MDM pack ---"
Assert-FileExists "$RepoRoot\seed-packs\national-mdm-sy-v1\catalog.json" "MDM JSON pack"
$catalog = Get-Content "$RepoRoot\seed-packs\national-mdm-sy-v1\catalog.json" -Raw | ConvertFrom-Json
foreach ($kind in @('Grade','JobClassification','Geography','Agency','Qualification','Bank','Currency','SalaryComponent','DocumentType','ContractType','MovementType','ServiceType')) {
    $count = @($catalog.entries | Where-Object { $_.kind -eq $kind }).Count
    if ($count -lt 1) {
        $script:failures += "MDM kind $kind has no entries"
        Write-Host "  FAIL  MDM kind $kind" -ForegroundColor Red
    } else {
        Write-Host "  OK    MDM kind $kind ($count entries)" -ForegroundColor Green
    }
}

Write-Host "`n--- ONB ---"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Onboarding\AgencyProvisioningService.cs" "AgencyProvisioningService"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Web\Controllers\OnboardingController.cs" "OnboardingController"
Assert-FileExists "$RepoRoot\seed-packs\examples\agency-ministry-finance.json" "Example agency manifest"
Assert-FileExists "$RepoRoot\scripts\onb001-apply-manifest.ps1" "ONB apply script"

Write-Host "`n--- Entity & migration ---"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Domain\Entities\Seeding\SeedPackApplication.cs" "SeedPackApplication entity"
Assert-FileContains "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\HRCoreDbContext.cs" "SeedPackApplications" "DbSet"

Write-Host "`n--- Docs & tests ---"
Assert-FileExists "$RepoRoot\docs\SEED_001_MASTER_DATA.md" "Documentation"
Assert-FileExists "$RepoRoot\src\Tests\HRManagement.UnitTests\NationalSeedTests.cs" "NationalSeedTests"
Assert-FileExists "$RepoRoot\src\Tests\HRManagement.UnitTests\AgencyProvisioningTests.cs" "AgencyProvisioningTests"

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "=== VALIDATION FAILED ($($failures.Count) issues) ===" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
} else {
    Write-Host "=== ALL SEED-001 CHECKS PASSED ===" -ForegroundColor Green
    exit 0
}
