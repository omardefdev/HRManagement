# HCM-028: validate the selection committee as a decision body (votes, per-candidate COI, decisions, lock, server-side
# quorum/SoD, ranking gate, minutes) and the single governed appeal path (compliance access slice, rejection kinds and
# appeal window, independent employee-identified reviewer, explicit outcomes, hire block) — PLAN-28 sessions 1 and 2.
param(
    [string]$RepoRoot = "",
    # Skips `dotnet ef migrations has-pending-model-changes` (needs the EF tool and a build of HR.Core.Web).
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
    "docs/acceptance-plans/PLAN-28-hcm-committee-appeal-governance.md",
    "docs/acceptance-plans/execution/PLAN-28-EXECUTION.md",
    "scripts/hcm028-validate.ps1",
    "scripts/hcm028-find-duplicate-committee-members.ps1",
    # Session 1 — committee model, rules, gate, minutes
    "src/Services/HRCore/HR.Core.Domain/Services/SelectionCommitteeRules.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/SelectionCommitteeVote.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/SelectionCommitteeDecision.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/SelectionCommitteeMemberCoi.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeLifecycleService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeMinutesService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CommitteeRankingGate.cs",
    "src/Services/HRCore/HR.Core.Web/Services/SelectionCommitteeMinutesDocument.cs",
    # Session 2 — compliance access slice, rejection tracking, appeal path, hire block
    "src/Services/HRCore/HR.Core.Domain/Enums/RecruitmentRejectionKind.cs",
    "src/Services/HRCore/HR.Core.Domain/Enums/RecruitmentAppealOutcome.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/ApplicationRejection.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/RecruitmentAppealWorkflowRules.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealWorkflowHandler.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireAppealGuard.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentAppealsController.cs",
    "src/Services/HRCore/HR.Core.Web/Services/RecruitmentAppealAccess.cs",
    "src/Services/HRCore/HR.Core.Web/Views/RecruitmentAppeals/Index.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/RecruitmentAppeals/Details.cshtml",
    # Tests
    "src/Tests/HRManagement.UnitTests/Hcm028CommitteeRulesTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm028CommitteeServiceTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm028CommitteeApprovalTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm028RankingGateTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm028CommitteeMinutesTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm028RejectionTrackingTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm028AppealLifecycleTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm028HireBlockTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm028AuthorizationTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm028AppealGovernanceMigrationTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm28CommitteeAppealTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

$migrationsDir = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
foreach ($migration in @("CommitteeDecisions", "RankOverride", "AppealGovernance")) {
    $found = Get-ChildItem -Path $migrationsDir -Filter "*_HCM028_$migration.cs" -ErrorAction SilentlyContinue
    if (-not $found) { $failures += "missing migration HCM028_$migration" }
}
$governance = Get-ChildItem -Path $migrationsDir -Filter "*_HCM028_AppealGovernance.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($governance) {
    $sql = Get-Content -Raw $governance.FullName
    if ($sql -notmatch 'Npgsql\.EntityFrameworkCore\.PostgreSQL' -or $sql -notmatch "'LegacyReopen'" -or $sql -match '(?i)\b(UPDATE|DELETE)\s+(FROM\s+)?"RecruitmentApplications"') {
        $failures += "HCM028_AppealGovernance data step must be Postgres-guarded and insert-only (LegacyReopen)"
    }
}

# The legacy direct-reopen path and the free-text reviewer are gone (G-09).
$webRoot = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web"
$legacy = Get-ChildItem -Path $webRoot -Recurse -Include *.cs, *.cshtml |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Select-String -CaseSensitive -Pattern 'ReopenAppeal|ApproveAppealWorkflow|RejectAppealWorkflow|DecideAppeal\b|name="independentReviewerId"' -List
foreach ($hit in $legacy) { $failures += "legacy appeal path still referenced: $($hit.Path)" }
$selection = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentSelectionService.cs")
if ($selection -match 'ReopenAppealAsync') { $failures += "RecruitmentSelectionService.ReopenAppealAsync still exists" }

# Compliance access slice (PLAN-25 slice).
$filter = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs")
foreach ($controller in @("RecruitmentAppeals", "WorkInbox")) {
    if ($filter -notmatch "`"$controller`"") { $failures += "HrModuleAuthorizationFilter does not exempt $controller" }
}
$policies = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs")
foreach ($policy in @("HrCoreAppealReview", "HrCoreWorkInbox")) {
    if ($policies -notmatch "HrManagementPolicies\.$policy") { $failures += "policy $policy is not registered" }
}
$gateway = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs")
if ($gateway -notmatch '/ui/hr-core/RecruitmentAppeals') { $failures += "gateway path rule for /ui/hr-core/RecruitmentAppeals missing" }
$personas = Get-Content -Raw (Resolve-RepoPath "testdata/acceptance/personas.json") | ConvertFrom-Json
$compliance2 = $personas.personas | Where-Object { $_.id -eq "a-compliance-2" }
if (-not $compliance2 -or $compliance2.roles -notcontains "compliance" -or $compliance2.passwordEnv -ne "ACCEPTANCE_A_COMPLIANCE_2_PASSWORD") {
    $failures += "persona a-compliance-2 (compliance, ACCEPTANCE_A_COMPLIANCE_2_PASSWORD) missing from personas.json"
}

# Markers left for later plans.
$appealService = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealService.cs")
foreach ($marker in @("// PLAN-30: candidate files via /careers", "// PLAN-25: always start workflow")) {
    if (-not $appealService.Contains($marker)) { $failures += "marker missing in RecruitmentAppealService: $marker" }
}

if (-not $SkipModelCheck) {
    Write-Host "Checking for pending EF model changes..."
    $ef = dotnet ef migrations has-pending-model-changes `
        -p (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure") `
        -s (Resolve-RepoPath "src/Services/HRCore/HR.Core.Web") 2>&1
    $ef | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not ($ef -match 'No changes have been made')) { $failures += "HR.Core has pending EF model changes" }
}

$unitTests = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj"

Write-Host "Running Hcm028 unit tests..."
dotnet test $unitTests --filter "FullyQualifiedName~Hcm028" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm028 unit tests failed" }

Write-Host "Running Hcm027|Hcm026|Hcm022|Hcm021|Hcm006|Wf023 regression..."
dotnet test $unitTests --filter "FullyQualifiedName~Hcm027|FullyQualifiedName~Hcm026|FullyQualifiedName~Hcm022|FullyQualifiedName~Hcm021|FullyQualifiedName~Hcm006|FullyQualifiedName~Wf023|FullyQualifiedName~AcceptanceSeedTests" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm027/Hcm026/Hcm022/Hcm021/Hcm006/Wf023 regression failed" }

if ($failures.Count -gt 0) {
    Write-Error ("hcm028-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm028-validate: PASS"
