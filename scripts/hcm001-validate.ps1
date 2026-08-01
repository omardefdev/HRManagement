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
    "scripts/hcm001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Hcm001RecruitmentTests.cs"
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
