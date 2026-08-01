# HCM-003: validate performance management artifacts.
# Usage:
#   powershell -File scripts/hcm003-validate.ps1

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
    "docs/HCM_003_PERFORMANCE.md",
    "src/Shared/HRManagement.SharedKernel/Performance/PerformanceCycle.cs",
    "src/Shared/HRManagement.SharedKernel/Performance/PerformanceReview.cs",
    "scripts/hcm003-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Hcm003PerformanceTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify PerformanceCycle entity has required properties
$cycleFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Performance/PerformanceCycle.cs"
if (Test-Path $cycleFile) {
    $content = Get-Content -Raw -Path $cycleFile
    $requiredProps = @("CycleCode", "CycleName", "Year", "Status",
                       "PlanningStart", "ReviewStart", "RatingScaleMax",
                       "ObjectivesRequired", "CompetenciesIncluded")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "PerformanceCycle missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "PerformanceCycle must extend BaseEntity"
    }
}

# Verify PerformanceReview entity has required properties
$reviewFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Performance/PerformanceReview.cs"
if (Test-Path $reviewFile) {
    $content = Get-Content -Raw -Path $reviewFile
    $requiredProps = @("EmployeeId", "PerformanceCycleId", "ReviewerId",
                       "OverallRating", "ObjectivesScore", "CompetenciesScore",
                       "HasAppeal", "CalibratedRating", "Status")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "PerformanceReview missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "PerformanceReview must extend BaseEntity"
    }
}

$docFile = Resolve-RepoPath "docs/HCM_003_PERFORMANCE.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "HCM-003") { $failures += "doc missing HCM-003 reference" }
    if ($doc -notmatch "PerformanceCycle") { $failures += "doc must describe PerformanceCycle entity" }
    if ($doc -notmatch "PerformanceReview") { $failures += "doc must describe PerformanceReview entity" }
}

$outDir = Resolve-RepoPath "testdata/hcm003/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "HCM-003"
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

Write-Host "PASS: HCM-003 performance management validate"
exit 0
