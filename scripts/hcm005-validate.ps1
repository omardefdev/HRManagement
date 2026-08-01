# HCM-005: validate learning and talent artifacts.
# Usage:
#   powershell -File scripts/hcm005-validate.ps1

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
    "docs/HCM_005_LEARNING.md",
    "src/Shared/HRManagement.SharedKernel/Talent/SkillRecord.cs",
    "src/Shared/HRManagement.SharedKernel/Talent/LearningEnrollment.cs",
    "scripts/hcm005-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Hcm005LearningTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify SkillRecord entity has required properties
$skillFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Talent/SkillRecord.cs"
if (Test-Path $skillFile) {
    $content = Get-Content -Raw -Path $skillFile
    $requiredProps = @("EmployeeId", "SkillCode", "SkillName", "Category",
                       "ProficiencyLevel", "RequiredLevel", "CertificateId",
                       "JobCatalogEntryId")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "SkillRecord missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "SkillRecord must extend BaseEntity"
    }
}

# Verify LearningEnrollment entity has required properties
$enrollFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Talent/LearningEnrollment.cs"
if (Test-Path $enrollFile) {
    $content = Get-Content -Raw -Path $enrollFile
    $requiredProps = @("EmployeeId", "CourseCode", "CourseName", "ProviderName",
                       "Status", "BudgetAmount", "ActualCost", "CertificateId",
                       "ApprovedByManagerId")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "LearningEnrollment missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "LearningEnrollment must extend BaseEntity"
    }
}

$docFile = Resolve-RepoPath "docs/HCM_005_LEARNING.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "HCM-005") { $failures += "doc missing HCM-005 reference" }
    if ($doc -notmatch "SkillRecord") { $failures += "doc must describe SkillRecord entity" }
    if ($doc -notmatch "LearningEnrollment") { $failures += "doc must describe LearningEnrollment entity" }
}

$outDir = Resolve-RepoPath "testdata/hcm005/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "HCM-005"
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

Write-Host "PASS: HCM-005 learning and talent validate"
exit 0
