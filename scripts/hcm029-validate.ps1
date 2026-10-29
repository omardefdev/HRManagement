# HCM-029 / FUN-029: validate hire closure safety (person matching, national-id mismatch, safe errors), the employment
# type domain value, ICandidateAccepted v4 (offer terms; none for internal mobility), the offer letter PDF with its
# verification hash (HR.Core — session 1), and the HR.Contracts / Payroll side: explicit lineage and offer terms on the
# contract, contract-type resolution by service regime with intervention, IContractActivated salary/grade, the pay
# profile fix, the appointment decision / medical steps and the commencement watch (session 2).
param(
    [string]$RepoRoot = "",
    # Skips `dotnet ef migrations has-pending-model-changes` (needs the EF tool and builds of the Web projects).
    [switch]$SkipModelCheck
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
    "docs/acceptance-plans/PLAN-29-hcm-hire-contract-documents.md",
    "docs/acceptance-plans/execution/PLAN-29-EXECUTION.md",
    "reports/plan29-session1-handoff.md",
    "scripts/hcm029-validate.ps1",
    # Session 1 — HR.Core
    "src/Services/HRCore/HR.Core.Domain/Services/HireNationalIdRules.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/HireEmploymentTypeRules.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/HirePersonMatchService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/HireClosureException.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/OfferLetterDocumentService.cs",
    "src/Services/HRCore/HR.Core.Web/Services/OfferLetterDocument.cs",
    # Session 2 — HR.Contracts / Payroll
    "src/Services/HRContracts/HR.Contracts.Domain/Entities/ContractTypeRegimeMap.cs",
    "src/Services/HRContracts/HR.Contracts.Domain/Services/HireContractMessages.cs",
    "src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/ContractTypeResolver.cs",
    "src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/CreatePendingHireContract.cs",
    "src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/IssueAppointmentDecision.cs",
    "src/Services/HRContracts/HR.Contracts.Infrastructure/Services/HireCommencementWatcher.cs",
    "src/Services/HRContracts/HR.Contracts.Web/Controllers/ContractTypeRegimeMapsController.cs",
    "src/Services/HRContracts/HR.Contracts.Web/Views/ContractTypeRegimeMaps/Index.cshtml",
    "src/Services/HRContracts/HR.Contracts.Web/Views/ContractTypeRegimeMaps/Form.cshtml",
    "src/Services/Payroll/Payroll.Application/Options/PayrollActivationOptions.cs",
    # Tests
    "src/Tests/HRManagement.UnitTests/Hcm029PersonMatchTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm029HireErrorsTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm029EmploymentTypeTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm029CandidateAcceptedV4Tests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm029OfferDocumentTests.cs",
    "src/Tests/HRManagement.UnitTests/Fun029CandidateAcceptedV4HandlerTests.cs",
    "src/Tests/HRManagement.UnitTests/Fun029ContractTypeResolverTests.cs",
    "src/Tests/HRManagement.UnitTests/Fun029ContractActivatedTests.cs",
    "src/Tests/HRManagement.UnitTests/Fun029ChecklistTests.cs",
    "src/Tests/HRManagement.UnitTests/Fun029CommencementWatchTests.cs",
    "src/Tests/HRManagement.UnitTests/Pay029PayProfileTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm029HireDuplicatePersonIntegrationTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Fun029LineageBackfillTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm29HireContractTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

# Migrations: HR.Core (session 1) and HR.Contracts (session 2).
$coreMigrations = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
if (-not (Get-ChildItem -Path $coreMigrations -Filter "*_HCM029_OfferDocument.cs" -ErrorAction SilentlyContinue)) {
    $failures += "missing migration HCM029_OfferDocument"
}
$contractsMigrations = Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Infrastructure/Migrations"
foreach ($migration in @("ContractOfferTerms", "ContractTypeRegimeMap")) {
    if (-not (Get-ChildItem -Path $contractsMigrations -Filter "*_FUN029_$migration.cs" -ErrorAction SilentlyContinue)) {
        $failures += "missing migration FUN029_$migration"
    }
}
$offerTerms = Get-ChildItem -Path $contractsMigrations -Filter "*_FUN029_ContractOfferTerms.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($offerTerms) {
    $sql = Get-Content -Raw $offerTerms.FullName
    if ($sql -notmatch 'Npgsql\.EntityFrameworkCore\.PostgreSQL' -or $sql -notmatch '"RecruitmentApplicationId" IS NULL' -or $sql -match '(?i)\bDELETE\s+FROM\b') {
        $failures += "FUN029_ContractOfferTerms backfill must be Postgres-guarded and only fill the empty lineage column"
    }
}

# ICandidateAccepted v4 is additive, and internal mobility no longer publishes it.
$contract = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.MessageContracts/Events/ICandidateAccepted.cs")
if ($contract -notmatch 'SchemaVersion => 4;') { $failures += "ICandidateAccepted must be schema version 4" }
foreach ($member in @("OfferedSalary", "GradeCode", "LegalRegimeId", "PayrollRegimeCode", "EmploymentType", "PositionCode", "OrgUnitId", "IsInternalMobility")) {
    if ($contract -notmatch "\b$member\b[^;]*=>") { $failures += "ICandidateAccepted.$member must have a default implementation" }
}
$closure = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs")
if ($closure -match 'EmploymentType\.Permanent\.ToString\(\)') { $failures += "IEmployeeCreated.EmploymentType is still hard-coded to Permanent" }
if (-not $closure.Contains("// PLAN-29 session 2: HR.Contracts also ignores IsInternalMobility (defence in depth)")) {
    $failures += "internal-mobility marker missing in RecruitmentHireClosureService"
}
$handler = Get-Content -Raw (Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs")
if ($handler -notmatch 'request\.IsInternalMobility') { $failures += "ProcessCandidateAcceptedHandler does not ignore internal mobility" }
if ($handler -match 'Code == "DEFAULT"') { $failures += "ProcessCandidateAcceptedHandler still looks up DEFAULT directly (use ContractTypeResolver)" }

# Lineage is read from columns only.
$hireCases = Get-Content -Raw (Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/HireCaseHandlers.cs")
if ($hireCases -match 'ParseRecruitmentApplicationId') { $failures += "PublishHireCaseHandler still parses lineage from Notes" }

# IContractActivated carries the contract's salary and grade.
$publisher = Get-Content -Raw (Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Infrastructure/Messaging/MassTransitEventPublisher.cs")
if ($publisher -notmatch 'BaseSalary = contract\.AgreedBaseSalary' -or $publisher -notmatch 'PayGradeCode = contract\.GradeCode') {
    $failures += "IContractActivated must publish Contract.AgreedBaseSalary / GradeCode"
}

# Configuration defaults per environment.
$contractsSettings = Get-Content -Raw (Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Web/appsettings.json") | ConvertFrom-Json
if ($contractsSettings.Contracts.AllowDefaultContractTypeFallback -ne $true) { $failures += "Contracts:AllowDefaultContractTypeFallback must default to true" }
if ($contractsSettings.HireChecklist.RequireAppointmentDecision -ne $true) { $failures += "HireChecklist:RequireAppointmentDecision must default to true" }
if ($contractsSettings.Recruitment.CommencementDeadlineDays -ne 30) { $failures += "Recruitment:CommencementDeadlineDays must default to 30" }
foreach ($env in @("Production", "Staging")) {
    $path = Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Web/appsettings.$env.json"
    if (-not (Test-Path $path) -or (Get-Content -Raw $path | ConvertFrom-Json).Contracts.AllowDefaultContractTypeFallback -ne $false) {
        $failures += "Contracts:AllowDefaultContractTypeFallback must be false in $env"
    }
}

# No new legal contract types in production seeding (test types live in the demo/acceptance seeder only).
$dataSeeder = Get-Content -Raw (Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDataSeeder.cs")
if ($dataSeeder -match 'ACC-|ContractTypeRegimeMap') { $failures += "ContractsDataSeeder (production) must not seed test types or regime maps" }

# Markers left for follow-ups.
$markers = @{
    "src/Services/HRContracts/HR.Contracts.Infrastructure/Services/HireCommencementWatcher.cs" = "PLAN-29 follow-up: cancel hire for non-commencement"
    "src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/IssueAppointmentDecision.cs" = "PLAN-29 follow-up: register in Diwan / sign"
    "src/Services/HRContracts/HR.Contracts.Web/Controllers/HireWizardController.cs" = "PLAN-29 follow-up: consume HR.Health fitness result"
    "src/Services/HRCore/HR.Core.Web/Controllers/OfferLettersController.cs" = "PLAN-29 follow-up: archive in File.Storage"
    # HCM-030 (PLAN-30 I) resolved "PLAN-30: candidate download from /careers": the portal renders the same PDF.
    "src/Services/HRCore/HR.Core.Web/Services/CandidatePortalService.cs" = "OfferLetterDocument.Generate"
}
foreach ($entry in $markers.GetEnumerator()) {
    if (-not (Get-Content -Raw (Resolve-RepoPath $entry.Key)).Contains($entry.Value)) { $failures += "marker missing in $($entry.Key): $($entry.Value)" }
}

if (-not $SkipModelCheck) {
    foreach ($svc in @(
            @("HR.Core", "src/Services/HRCore/HR.Core.Infrastructure", "src/Services/HRCore/HR.Core.Web"),
            @("HR.Contracts", "src/Services/HRContracts/HR.Contracts.Infrastructure", "src/Services/HRContracts/HR.Contracts.Web"),
            @("Payroll", "src/Services/Payroll/Payroll.Infrastructure", "src/Services/Payroll/Payroll.Web"))) {
        Write-Host "Checking for pending EF model changes ($($svc[0]))..."
        $ef = dotnet ef migrations has-pending-model-changes -p (Resolve-RepoPath $svc[1]) -s (Resolve-RepoPath $svc[2]) 2>&1
        $ef | Out-Host
        if ($LASTEXITCODE -ne 0 -or -not ($ef -match 'No changes have been made')) { $failures += "$($svc[0]) has pending EF model changes" }
    }
}

$unitTests = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj"

Write-Host "Running Hcm029|Fun029|Pay029 unit tests..."
dotnet test $unitTests --filter "FullyQualifiedName~Hcm029|FullyQualifiedName~Fun029|FullyQualifiedName~Pay029" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm029/Fun029/Pay029 unit tests failed" }

Write-Host "Running hire regression (Fun001|HireWorkflowRules|HireWizard|Hcm028|Hcm027|Hcm026|Hcm001)..."
dotnet test $unitTests --filter "FullyQualifiedName~Fun001|FullyQualifiedName~HireWorkflowRules|FullyQualifiedName~HireWizard|FullyQualifiedName~Hcm028|FullyQualifiedName~Hcm027|FullyQualifiedName~Hcm026|FullyQualifiedName~Hcm001" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "hire regression failed" }

if ($failures.Count -gt 0) {
    Write-Error ("hcm029-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm029-validate: PASS"
