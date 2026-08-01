# HCM-004: validate benefits and pension artifacts.
# Usage:
#   powershell -File scripts/hcm004-validate.ps1

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
    "docs/HCM_004_BENEFITS.md",
    "src/Shared/HRManagement.SharedKernel/Benefits/BenefitPlan.cs",
    "src/Shared/HRManagement.SharedKernel/Benefits/PensionEnrollment.cs",
    "scripts/hcm004-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Hcm004BenefitsTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify BenefitPlan entity has required properties
$benefitPlanFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Benefits/BenefitPlan.cs"
if (Test-Path $benefitPlanFile) {
    $content = Get-Content -Raw -Path $benefitPlanFile
    $requiredProps = @("PlanCode", "PlanType", "EmployeeContributionRate",
                       "EmployerContributionRate", "EffectiveFrom", "LegalSourceId")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "BenefitPlan missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "BenefitPlan must extend BaseEntity"
    }
}

# Verify PensionEnrollment entity has required properties
$enrollmentFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Benefits/PensionEnrollment.cs"
if (Test-Path $enrollmentFile) {
    $content = Get-Content -Raw -Path $enrollmentFile
    $requiredProps = @("EmployeeId", "BenefitPlanId", "EnrollmentDate",
                       "ServiceCreditMonths", "ContributionBalance", "Status")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "PensionEnrollment missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "PensionEnrollment must extend BaseEntity"
    }
}

$docFile = Resolve-RepoPath "docs/HCM_004_BENEFITS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "HCM-004") { $failures += "doc missing HCM-004 reference" }
    if ($doc -notmatch "BenefitPlan") { $failures += "doc must describe BenefitPlan entity" }
    if ($doc -notmatch "PensionEnrollment") { $failures += "doc must describe PensionEnrollment entity" }
}

$outDir = Resolve-RepoPath "testdata/hcm004/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "HCM-004"
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

Write-Host "PASS: HCM-004 benefits and pension validate"
exit 0
