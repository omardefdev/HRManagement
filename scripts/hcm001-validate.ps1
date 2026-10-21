# HCM-001: validate recruitment artifacts.
# Usage:
#   powershell -File scripts/hcm001-validate.ps1

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
    "docs/HCM_001_RECRUITMENT.md",
    "src/Shared/HRManagement.SharedKernel/Recruitment/RecruitmentRequisition.cs",
    "src/Shared/HRManagement.SharedKernel/Recruitment/CandidateApplication.cs",
    "src/Shared/HRManagement.SharedKernel/Recruitment/OfferLetter.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentRequisition.cs",
    "src/Services/HRCore/HR.Core.Domain/Enums/RequisitionStatus.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/RequisitionWorkflowRules.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/RecruitmentRequisitionService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentRequisitionsController.cs",
    "scripts/hcm001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Hcm001RecruitmentTests.cs",
    "src/Tests/HRManagement.UnitTests/RequisitionWorkflowRulesTests.cs",
    "src/Tests/HRManagement.UnitTests/RecruitmentRequisitionServiceTests.cs",
    "src/Tests/HRManagement.IntegrationTests/RecruitmentRequisitionIntegrationTests.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/RequisitionCloseService.cs",
    "scripts/hcm001-migrate-legacy-applications.ps1",
    "src/Tests/HRManagement.IntegrationTests/RecruitmentHireClosureIntegrationTests.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/CandidateApplicationLegalLifecycleService.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentAppeal.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/SelectionCommitteeSession.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Middleware/PublicCareersRateLimitExtensions.cs",
    "src/Services/Compliance/Compliance.Infrastructure/Privacy/HttpHrCoreCandidateDsarSource.cs",
    "src/Tests/HRManagement.UnitTests/Hcm006ComplianceExperienceTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm06CandidateDsarTests.cs",
    "scripts/hcm006-a11y-scan.ps1"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify RecruitmentRequisition entity
$reqFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Recruitment/RecruitmentRequisition.cs"
if (Test-Path $reqFile) {
    $content = Get-Content -Raw -Path $reqFile
    $requiredProps = @("RequisitionNumber", "PositionId", "JobTitle", "Status", "RequestedDate", "VacancyCount")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "RecruitmentRequisition missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "RecruitmentRequisition must extend BaseEntity"
    }
}

# Verify HR.Core persisted RecruitmentRequisition (PLAN-HCM-01)
$coreReqFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentRequisition.cs"
if (Test-Path $coreReqFile) {
    $content = Get-Content -Raw -Path $coreReqFile
    $requiredProps = @("RequisitionNumber", "FundedPositionId", "JobTitle", "Status", "RequestedDate", "VacancyCount")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "HR.Core RecruitmentRequisition missing required property: $prop"
        }
    }
    if ($content -notmatch "AuditableEntity") {
        $failures += "HR.Core RecruitmentRequisition must extend AuditableEntity"
    }
}

$closureFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs"
if (Test-Path $closureFile) {
    $content = Get-Content -Raw -Path $closureFile
    if ($content -notmatch "RecruitmentHireClosureService") { $failures += "missing RecruitmentHireClosureService" }
    if ($content -notmatch "ICandidateAccepted") { $failures += "closure service must publish ICandidateAccepted v3" }
    if ($content -notmatch "AssignEmployee") { $failures += "closure service must assign funded position" }
}

$candidateAcceptedFile = Resolve-RepoPath "src/Shared/HRManagement.MessageContracts/Events/ICandidateAccepted.cs"
if (Test-Path $candidateAcceptedFile) {
    $content = Get-Content -Raw -Path $candidateAcceptedFile
    if ($content -notmatch "RecruitmentApplicationId") { $failures += "ICandidateAccepted missing RecruitmentApplicationId (v3)" }
    if ($content -notmatch "SchemaVersion => 3") { $failures += "ICandidateAccepted must be schema version 3" }
}

$migrateScript = Resolve-RepoPath "scripts/hcm001-migrate-legacy-applications.ps1"
if (-not (Test-Path $migrateScript)) {
    $failures += "missing scripts/hcm001-migrate-legacy-applications.ps1"
}

$coreAppFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentApplication.cs"
if (Test-Path $coreAppFile) {
    $content = Get-Content -Raw -Path $coreAppFile
    if ($content -notmatch "IsLegacyUnlinked") { $failures += "RecruitmentApplication missing IsLegacyUnlinked" }
}

$dbContextFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs"
if (Test-Path $dbContextFile) {
    $content = Get-Content -Raw -Path $dbContextFile
    if ($content -notmatch "DbSet<RecruitmentRequisition>") {
        $failures += "HRCoreDbContext missing DbSet<RecruitmentRequisition>"
    }
}

$migration = Get-ChildItem -Path (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations") -Filter "*RecruitmentRequisition*.cs" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notmatch "Designer" } |
    Select-Object -First 1
if (-not $migration) {
    $failures += "missing RecruitmentRequisitions EF migration"
}

$controllerFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentRequisitionsController.cs"
if (Test-Path $controllerFile) {
    $content = Get-Content -Raw -Path $controllerFile
    if ($content -notmatch "vacant-positions") {
        $failures += "RecruitmentRequisitionsController missing vacant-positions API"
    }
}

# Verify CandidateApplication entity
$appFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Recruitment/CandidateApplication.cs"
if (Test-Path $appFile) {
    $content = Get-Content -Raw -Path $appFile
    $requiredProps = @("RequisitionId", "CandidateName", "Status", "SubmittedAt", "AssessmentScore", "ConflictOfInterestDeclared")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "CandidateApplication missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "CandidateApplication must extend BaseEntity"
    }
}

# Verify OfferLetter entity
$offerFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Recruitment/OfferLetter.cs"
if (Test-Path $offerFile) {
    $content = Get-Content -Raw -Path $offerFile
    $requiredProps = @("ApplicationId", "PositionId", "OfferNumber", "Status", "OfferedSalary", "IsConvertedToEmployee")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "OfferLetter missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "OfferLetter must extend BaseEntity"
    }
}

# Verify doc references
$docFile = Resolve-RepoPath "docs/HCM_001_RECRUITMENT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "HCM-001") { $failures += "doc missing HCM-001 reference" }
    if ($doc -notmatch "RecruitmentRequisition") { $failures += "doc must describe RecruitmentRequisition" }
    if ($doc -notmatch "CandidateApplication") { $failures += "doc must describe CandidateApplication" }
    if ($doc -notmatch "OfferLetter") { $failures += "doc must describe OfferLetter" }
    if ($doc -notmatch "Plan map") { $failures += "doc must include Plan map 1-6" }
}

$outDir = Resolve-RepoPath "testdata/hcm001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "HCM-001"
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

Write-Host "PASS: HCM-001 recruitment validate"
exit 0
