# SYR-001: validate Syrian legislation repository artifacts.
# Usage:
#   powershell -File scripts/syr001-validate.ps1

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
    "docs/SYR_001_LEGISLATION_REPOSITORY.md",
    "docs/syr/legislation-register.json",
    "docs/syr/legal-source-binding.md",
    "scripts/syr001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr001LegislationTests.cs",
    "src/Tests/HRManagement.UnitTests/Syr001LegislationSchemaTests.cs",
    "src/Tests/HRManagement.UnitTests/Syr001LegislationSeedTests.cs",
    "src/Tests/HRManagement.UnitTests/Syr001LegislationProviderTests.cs",
    "src/Tests/HRManagement.UnitTests/Syr001ProductionBindingTests.cs",
    "src/Shared/HRManagement.SharedKernel/Legal/ILegislationRegisterProvider.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentLegalSourceValidator.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/RecruitmentLegalSourceRules.cs",
    "src/Tests/HRManagement.UnitTests/Syr001RecruitmentLegalSourceTests.cs",
    "src/Tests/HRManagement.UnitTests/Syr001OfferLegalSourceTests.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/LegislationRegisterDependencyInjection.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Data/LegislationRegisterSeeder.cs",
    "src/Shared/HRManagement.SharedKernel/Legal/LegislationSource.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Data/Configurations/Legal/LegislationRegisterConfigurations.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Validate legislation register
$regFile = Resolve-RepoPath "docs/syr/legislation-register.json"
if (Test-Path $regFile) {
    $reg = Get-Content -Raw -Path $regFile | ConvertFrom-Json
    if ($reg.card -ne "SYR-001") { $failures += "register card must be SYR-001" }
    if (-not $reg.register -or $reg.register.Count -lt 2) {
        $failures += "register must have at least 2 legislation entries"
    }

    $hasLaw50 = $false
    $hasLaw53 = $false
    foreach ($entry in $reg.register) {
        if (-not $entry.legalSourceId) { $failures += "entry missing legalSourceId" }
        if (-not $entry.type) { $failures += "entry $($entry.legalSourceId) missing type" }
        if (-not $entry.number) { $failures += "entry $($entry.legalSourceId) missing number" }
        if (-not $entry.effectiveDate) { $failures += "entry $($entry.legalSourceId) missing effectiveDate" }
        if ($entry.number -eq "50/2004") { $hasLaw50 = $true }
        if ($entry.number -eq "53/2021") { $hasLaw53 = $true }
    }
    if (-not $hasLaw50) { $failures += "register must include Law 50/2004" }
    if (-not $hasLaw53) { $failures += "register must include Law 53/2021" }
}

# Validate binding doc
$bindFile = Resolve-RepoPath "docs/syr/legal-source-binding.md"
if (Test-Path $bindFile) {
    $bind = Get-Content -Raw -Path $bindFile
    if ($bind -notmatch "legal_source_id") { $failures += "binding doc must mention legal_source_id" }
    if ($bind -notmatch "article") { $failures += "binding doc must mention article references" }
    if ($bind -notmatch "effective") { $failures += "binding doc must mention effective dates" }
}

# Validate main doc
$docFile = Resolve-RepoPath "docs/SYR_001_LEGISLATION_REPOSITORY.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-001") { $failures += "doc missing SYR-001 reference" }
    if ($doc -notmatch "50/2004") { $failures += "doc must mention Law 50/2004" }
    if ($doc -notmatch "53/2021") { $failures += "doc must mention Law 53/2021" }
    if ($doc -notmatch "PLAN-SYR-08") { $failures += "doc must document PLAN-SYR-08 movement regime binding" }
    if ($doc -notmatch "PLAN-SYR-09") { $failures += "doc must document PLAN-SYR-09 Article 159 + separation lineage" }
}

# Validate SYR-001 DB migration (PLAN-SYR-01)
$migrationDir = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
$migration = Get-ChildItem -Path $migrationDir -Filter "*SYR001_LegislationRegister.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $migration) {
    $failures += "missing SYR001_LegislationRegister migration"
} else {
    $migrationText = Get-Content -Raw -Path $migration.FullName
    if ($migrationText -notmatch "LegislationSources") { $failures += "SYR001 migration must create LegislationSources" }
    if ($migrationText -notmatch "FK_RecruitmentRequisitions_LegislationSources") {
        $failures += "SYR001 migration must FK RecruitmentRequisitions.LegalSourceId"
    }
}

$snapshot = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations/HRCoreDbContextModelSnapshot.cs"
if (Test-Path $snapshot) {
    $snap = Get-Content -Raw -Path $snapshot
    if ($snap -notmatch "LegislationSources") { $failures += "model snapshot must include LegislationSources" }
}

$csproj = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/HR.Core.Infrastructure.csproj"
if (Test-Path $csproj) {
    $proj = Get-Content -Raw -Path $csproj
    if ($proj -notmatch "syr001-legislation-v1\\register\.json") {
        $failures += "csproj must link docs/syr/legislation-register.json to seed-packs/syr001-legislation-v1/register.json"
    }
}

$orchestrator = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Seeding/NationalSeedOrchestrator.cs"
if (Test-Path $orchestrator) {
    $orch = Get-Content -Raw -Path $orchestrator
    if ($orch -notmatch "LegislationRegisterSeeder\.SeedAsync") {
        $failures += "NationalSeedOrchestrator must call LegislationRegisterSeeder.SeedAsync"
    }
}

$program = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Program.cs"
if (Test-Path $program) {
    $prog = Get-Content -Raw -Path $program
    if ($prog -notmatch "AddHrLegislationRegister") {
        $failures += "Program.cs must call AddHrLegislationRegister"
    }
}

$createView = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Views/RecruitmentRequisitions/Create.cshtml"
if (Test-Path $createView) {
    $view = Get-Content -Raw -Path $createView
    if ($view -notmatch 'asp-items="ViewBag\.LegalSources"') {
        $failures += "Create view must use LegalSources dropdown from SYR-001 register"
    }
}

$lifecycle = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionLifecycleService.cs"
if (Test-Path $lifecycle) {
    $life = Get-Content -Raw -Path $lifecycle
    if ($life -notmatch "ValidateRequiredAsync") {
        $failures += "RecruitmentRequisitionLifecycleService must validate legal source on submit"
    }
}

$offerService = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferService.cs"
if (Test-Path $offerService) {
    $offer = Get-Content -Raw -Path $offerService
    if ($offer -notmatch "LegalSourceId\s*=") {
        $failures += "RecruitmentOfferService must copy LegalSourceId from requisition"
    }
    if ($offer -notmatch "ValidateRequiredAsync") {
        $failures += "RecruitmentOfferService must validate legal source on offer submit"
    }
}

$hireClosure = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs"
if (Test-Path $hireClosure) {
    $hire = Get-Content -Raw -Path $hireClosure
    if ($hire -notmatch "ValidateRequiredAsync|RequiredOnHireMessage") {
        $failures += "RecruitmentHireClosureService must validate legal source before hire"
    }
    if ($hire -notmatch "LegalRegimeId") {
        $failures += "RecruitmentHireClosureService must set EmploymentRecord.LegalRegimeId"
    }
}

# Validate SYR-006 runtime binding (PLAN-SYR-06)
$serviceRegimeSeeder = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Data/ServiceRegimeSeeder.cs"
if (-not (Test-Path $serviceRegimeSeeder)) {
    $failures += "missing ServiceRegimeSeeder.cs"
}

$employmentRecord = Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Entities/EmploymentRecord.cs"
if (Test-Path $employmentRecord) {
    $er = Get-Content -Raw -Path $employmentRecord
    if ($er -notmatch "LegalRegimeId") {
        $failures += "EmploymentRecord must contain LegalRegimeId"
    }
}

if (Test-Path $orchestrator) {
    $orch = Get-Content -Raw -Path $orchestrator
    if ($orch -notmatch "ServiceRegimeSeeder\.SeedAsync") {
        $failures += "NationalSeedOrchestrator must call ServiceRegimeSeeder.SeedAsync"
    }
}

if (Test-Path $snapshot) {
    $snap = Get-Content -Raw -Path $snapshot
    if ($snap -notmatch "LegalRegimes") { $failures += "model snapshot must include LegalRegimes" }
    if ($snap -notmatch "BenefitPlans") { $failures += "model snapshot must include BenefitPlans" }
}

$syr006Migration = Get-ChildItem -Path $migrationDir -Filter "*SYR006*" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $syr006Migration) {
    $failures += "missing SYR006_ServiceRegimeAndBenefits migration"
}

if (Test-Path $csproj) {
    $proj = Get-Content -Raw -Path $csproj
    if ($proj -notmatch "syr003-service-regimes-v1\\regimes\.json") {
        $failures += "csproj must link docs/syr/service-regimes.json to seed-packs/syr003-service-regimes-v1/regimes.json"
    }
}

$appDetailsView = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Views/RecruitmentApplications/Details.cshtml"
if (Test-Path $appDetailsView) {
    $appView = Get-Content -Raw -Path $appDetailsView
    if ($appView -notmatch "RequisitionLegalSourceLabel|LegalSource") {
        $failures += "RecruitmentApplications Details must show inherited legal source label"
    }
}

# Validate SYR-007 exception queue + payroll bridge (PLAN-SYR-07)
$syr007Files = @(
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimeExceptionService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimeTransitionService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimePayrollBlockService.cs",
    "src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeToPayrollMapper.cs",
    "src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreServiceRegimeClient.cs",
    "src/Services/Payroll/Payroll.Application/Services/PayrollRegimeGovernance.cs",
    "src/Tests/HRManagement.UnitTests/Syr007RegimeGovernanceTests.cs"
)
foreach ($rel in $syr007Files) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$syr007Migration = Get-ChildItem -Path $migrationDir -Filter "*SYR007_RegimeGovernance.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $syr007Migration) {
    $failures += "missing SYR007_RegimeGovernance migration"
}

if (Test-Path $snapshot) {
    $snap = Get-Content -Raw -Path $snapshot
    if ($snap -notmatch "ServiceRegimeExceptions") { $failures += "model snapshot must include ServiceRegimeExceptions" }
    if ($snap -notmatch "EmploymentRegimeTransitions") { $failures += "model snapshot must include EmploymentRegimeTransitions" }
}

if (Test-Path $hireClosure) {
    $hire = Get-Content -Raw -Path $hireClosure
    if ($hire -notmatch "EnqueueIfUnclassifiedAsync") {
        $failures += "RecruitmentHireClosureService must enqueue service regime exceptions on unmapped hire"
    }
    if ($hire -notmatch "PayrollRegimeCode") {
        $failures += "RecruitmentHireClosureService must publish PayrollRegimeCode on IEmployeeCreated"
    }
}

if (Test-Path $orchestrator) {
    $orch = Get-Content -Raw -Path $orchestrator
    if ($orch -notmatch "ServiceRegimeBackfillService\.SeedAsync") {
        $failures += "NationalSeedOrchestrator must call ServiceRegimeBackfillService.SeedAsync"
    }
}

$employeeCreated = Resolve-RepoPath "src/Shared/HRManagement.MessageContracts/Events/IEmployeeCreated.cs"
if (Test-Path $employeeCreated) {
    $evt = Get-Content -Raw -Path $employeeCreated
    if ($evt -notmatch "SchemaVersion => 6") {
        $failures += "IEmployeeCreated must be schema version 6"
    }
}

# Validate SYR-008 movement regime binding (PLAN-SYR-08)
$syr008Lineage = @(
    "src/Shared/HRManagement.SharedKernel/Legal/IHrCoreActiveRegimeClient.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimeActiveRegimeService.cs",
    "src/Services/HRMovements/HR.Movements.Infrastructure/Services/MovementRegimeValidationService.cs"
)
foreach ($rel in $syr008Lineage) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$internalController = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Controllers/ServiceRegimeInternalController.cs"
if (Test-Path $internalController) {
    $ctrl = Get-Content -Raw -Path $internalController
    if ($ctrl -notmatch "active-regimes") {
        $failures += "ServiceRegimeInternalController must expose active-regimes S2S endpoint"
    }
}

$transferHandler = Resolve-RepoPath "src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs"
if (Test-Path $transferHandler) {
    $handler = Get-Content -Raw -Path $transferHandler
    if ($handler -notmatch "PayrollRegimeCode") {
        $failures += "HandleTransferExecutedHandler must update RegimeCode from PayrollRegimeCode"
    }
}

# Validate SYR-009 regime lifecycle (PLAN-SYR-09)
$separationRecord = Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Domain/Entities/SeparationRecord.cs"
if (Test-Path $separationRecord) {
    $sep = Get-Content -Raw -Path $separationRecord
    if ($sep -notmatch "SourceLegalRegimeId") {
        $failures += "SeparationRecord must snapshot SourceLegalRegimeId"
    }
    if ($sep -notmatch "PayrollRegimeCode") {
        $failures += "SeparationRecord must map PayrollRegimeCode"
    }
}

$article159Service = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimeExceptionService.cs"
if (Test-Path $article159Service) {
    $svc = Get-Content -Raw -Path $article159Service
    if ($svc -notmatch "ResolveArticle159Async") {
        $failures += "ServiceRegimeExceptionService must resolve Article 159 with LegalDecisionId"
    }
}

$separationHandler = Resolve-RepoPath "src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs"
if (Test-Path $separationHandler) {
    $handler = Get-Content -Raw -Path $separationHandler
    if ($handler -notmatch "GetConfigurationByRegimeCodeAsync") {
        $failures += "HandleSeparationCompletedHandler must resolve EOS config by PayrollRegimeCode"
    }
}

$outDir = Resolve-RepoPath "testdata/syr001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-001"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outDir "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: SYR-001 Syrian legislation repository validate"
exit 0
