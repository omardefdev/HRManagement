# HCM-026: validate assessment criteria, scoring, interview lifecycle, requisition integrity and intake window (PLAN-26).
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
    "docs/acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md",
    "docs/acceptance-plans/execution/PLAN-26-EXECUTION.md",
    "scripts/hcm026-validate.ps1",
    "scripts/hcm026-find-duplicate-requisitions.ps1",
    # A — assessment criteria
    "src/Services/HRCore/HR.Core.Domain/Recruitment/AssessmentCriteriaItem.cs",
    "src/Services/HRCore/HR.Core.Domain/Recruitment/AssessmentCriteriaSerializer.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/AssessmentCriteriaRules.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAssessmentCriteriaService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOptions.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentAssessmentCriteriaController.cs",
    "src/Services/HRCore/HR.Core.Web/Views/RecruitmentRequisitions/_AssessmentCriteriaPanel.cshtml",
    "src/Services/HRCore/HR.Core.Web/wwwroot/js/recruitment-criteria.js",
    # B — structured scoring
    "src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentAssessmentScore.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/AssessmentScoringRules.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentScoringService.cs",
    "src/Services/HRCore/HR.Core.Web/Views/RecruitmentApplications/_AssessmentScoringPanel.cshtml",
    # C — interview lifecycle
    "src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentInterviewPanelMember.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentInterviewService.cs",
    "src/Services/HRCore/HR.Core.Web/Views/RecruitmentApplications/_InterviewsPanel.cshtml",
    # D — requisition integrity
    "src/Services/HRCore/HR.Core.Domain/Services/EstablishmentRecruitmentCascadeRules.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionCascade.cs",
    # E — intake window
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentIntakeCloser.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentIntakeCloseJob.cs",
    # Tests
    "src/Tests/HRManagement.UnitTests/Hcm026AssessmentCriteriaRulesTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm026CriteriaServiceTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm026ScoringServiceTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm026InterviewLifecycleTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm026RequisitionIntegrityTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm026ClosingDateTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm026IntakeCloserTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm026RequisitionIndexIntegrationTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm026EstablishmentCascadeIntegrationTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm26AssessmentInterviewTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

$migrationsDir = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
foreach ($migration in @("CriteriaGovernance", "AssessmentScores", "InterviewLifecycle", "RequisitionIntegrity")) {
    $found = Get-ChildItem -Path $migrationsDir -Filter "*_HCM026_$migration.cs" -ErrorAction SilentlyContinue
    if (-not $found) { $failures += "missing migration HCM026_$migration" }
}

$unitTests = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj"

Write-Host "Running Hcm026 unit tests..."
dotnet test $unitTests --filter "FullyQualifiedName~Hcm026" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm026 unit tests failed" }

Write-Host "Running Hcm021|Hcm022 regression..."
dotnet test $unitTests --filter "FullyQualifiedName~Hcm021|FullyQualifiedName~Hcm022" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm021/Hcm022 regression failed" }

if ($failures.Count -gt 0) {
    Write-Error ("hcm026-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm026-validate: PASS"
