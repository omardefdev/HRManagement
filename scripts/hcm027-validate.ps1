# HCM-027: validate offer merit order, exception approval, offer outcomes/expiry, offer terms (Payroll grade lookup)
# and the reserve list (PLAN-27).
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
    "docs/acceptance-plans/PLAN-27-hcm-offer-merit-reserve.md",
    "docs/acceptance-plans/execution/PLAN-27-EXECUTION.md",
    "scripts/hcm027-validate.ps1",
    # A — eligibility and merit order
    "src/Services/HRCore/HR.Core.Domain/Services/OfferEligibilityRules.cs",
    # B — exception approval workflow
    "src/Services/HRCore/HR.Core.Domain/Services/OfferOverrideApprovalRules.cs",
    # C/D — offer outcomes and expiry
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferExpirer.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferExpiryJob.cs",
    # E — offer terms and the Payroll grade lookup
    "src/Services/HRCore/HR.Core.Domain/Services/OfferTermsRules.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Integration/PayrollGradeLookup.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PayGradeLookupInternalController.cs",
    # F — reserve list
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/ReserveListService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentReserveExpirer.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentReserveExpiryJob.cs",
    "src/Services/HRCore/HR.Core.Web/Views/Shared/_NextCandidateBanner.cshtml",
    # Tests
    "src/Tests/HRManagement.UnitTests/Hcm027OfferEligibilityRulesTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm027OfferServiceEligibilityTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm027OverrideWorkflowTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm027OfferOutcomeTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm027OfferExpirerTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm027SelectionTransitionsTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm027OfferTermsTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm027ReserveListTests.cs",
    "src/Tests/HRManagement.UnitTests/HttpPayrollGradeLookupTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm027PayGradeLookupIntegrationTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm27OfferMeritTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

$migrationsDir = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
foreach ($migration in @("OfferOutcome", "OfferTerms", "ReserveList")) {
    $found = Get-ChildItem -Path $migrationsDir -Filter "*_HCM027_$migration.cs" -ErrorAction SilentlyContinue
    if (-not $found) { $failures += "missing migration HCM027_$migration" }
}

$serviceIdentity = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/ServiceIdentity.cs")
if ($serviceIdentity -notmatch '"payroll\.grades\.read"') { $failures += "ServiceScopes.PayrollGradesRead missing" }
$keycloakSync = Get-Content -Raw (Resolve-RepoPath "scripts/sync-keycloak-dev-realm.ps1")
if ($keycloakSync -notmatch 'svc-hr-core"\s+-OptionalScopes\s+@\([^)]*"payroll\.grades\.read"') {
    $failures += "svc-hr-core is not granted payroll.grades.read in sync-keycloak-dev-realm.ps1"
}

$unitTests = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj"

Write-Host "Running Hcm027 unit tests..."
dotnet test $unitTests --filter "FullyQualifiedName~Hcm027|FullyQualifiedName~HttpPayrollGradeLookup" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm027 unit tests failed" }

Write-Host "Running Hcm026|Hcm022 regression..."
dotnet test $unitTests --filter "FullyQualifiedName~Hcm026|FullyQualifiedName~Hcm022" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm026/Hcm022 regression failed" }

if ($failures.Count -gt 0) {
    Write-Error ("hcm027-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm027-validate: PASS"
