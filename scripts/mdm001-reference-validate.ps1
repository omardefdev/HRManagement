<#
.SYNOPSIS
    MDM-001: validates reference data platform artifacts exist and are consistent.
.DESCRIPTION
    Checks entity, enum, migration, controller, seed, tests, and docs.
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

Write-Host "`n=== MDM-001 Reference Data Validation ===" -ForegroundColor Cyan

Write-Host "`n--- SharedKernel MasterData ---"
Assert-FileExists "$RepoRoot\src\Shared\HRManagement.SharedKernel\MasterData\CatalogKind.cs" "CatalogKind enum"
Assert-FileExists "$RepoRoot\src\Shared\HRManagement.SharedKernel\MasterData\ReferenceCatalogEntry.cs" "ReferenceCatalogEntry entity"
Assert-FileExists "$RepoRoot\src\Shared\HRManagement.SharedKernel\MasterData\IReferenceDataProvider.cs" "IReferenceDataProvider interface"

Write-Host "`n--- CatalogKind values ---"
Assert-FileContains "$RepoRoot\src\Shared\HRManagement.SharedKernel\MasterData\CatalogKind.cs" "JobClassification" "JobClassification kind"
Assert-FileContains "$RepoRoot\src\Shared\HRManagement.SharedKernel\MasterData\CatalogKind.cs" "Grade" "Grade kind"
Assert-FileContains "$RepoRoot\src\Shared\HRManagement.SharedKernel\MasterData\CatalogKind.cs" "Geography" "Geography kind"
Assert-FileContains "$RepoRoot\src\Shared\HRManagement.SharedKernel\MasterData\CatalogKind.cs" "ServiceType" "ServiceType kind"

Write-Host "`n--- HR.Core Infrastructure ---"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\Configurations\MasterData\ReferenceCatalogEntryConfiguration.cs" "EF configuration"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Migrations\20260731160000_MDM001_NationalReferenceCatalogs.cs" "Migration"
Assert-FileContains "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\HRCoreDbContext.cs" "ReferenceCatalogEntries" "DbSet in context"

Write-Host "`n--- HR.Core Web ---"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Web\Controllers\ReferenceDataController.cs" "ReferenceDataController"

Write-Host "`n--- Domain Services ---"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Domain\Services\ReferenceDataLifecycleService.cs" "ReferenceDataLifecycleService"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Domain\Services\ReferenceImpactAnalyzer.cs" "ReferenceImpactAnalyzer"

Write-Host "`n--- Message Contracts ---"
Assert-FileExists "$RepoRoot\src\Shared\HRManagement.MessageContracts\Events\IReferenceDataChanged.cs" "IReferenceDataChanged event"

Write-Host "`n--- Consumer ---"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Consumers\ReferenceDataChangedConsumer.cs" "ReferenceDataChangedConsumer"

Write-Host "`n--- Free-text FK ---"
Assert-FileContains "$RepoRoot\src\Services\HRCore\HR.Core.Domain\Entities\EmploymentRecord.cs" "JobClassificationCode" "EmploymentRecord FK"
Assert-FileContains "$RepoRoot\src\Services\HRCore\HR.Core.Domain\Entities\CareerPathStep.cs" "JobClassificationCode" "CareerPathStep FK"

Write-Host "`n--- Seed & Scripts ---"
Assert-FileExists "$RepoRoot\docker\seed\mdm-reference-seed.sql" "Seed SQL"
Assert-FileExists "$RepoRoot\scripts\mdm001-migrate-freetext-to-codes.sql" "Free-text migration SQL"
Assert-FileExists "$RepoRoot\src\Services\HRCore\HR.Core.Infrastructure\Data\MdmReferenceSeeder.cs" "MdmReferenceSeeder C#"
Assert-FileExists "$RepoRoot\seed-packs\national-mdm-sy-v1\catalog.json" "National MDM JSON pack"

Write-Host "`n--- Docs & Tests ---"
Assert-FileExists "$RepoRoot\docs\MDM_001_REFERENCE_DATA.md" "Documentation"
Assert-FileExists "$RepoRoot\src\Tests\HRManagement.UnitTests\Mdm001ReferenceDataTests.cs" "Unit tests"

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "=== VALIDATION FAILED ($($failures.Count) issues) ===" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
} else {
    Write-Host "=== ALL MDM-001 CHECKS PASSED ===" -ForegroundColor Green
    exit 0
}
