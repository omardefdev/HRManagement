# SYR-013: validate branch/governorate UX and accessibility checklist artifacts.
# Usage:
#   powershell -File scripts/syr013-validate.ps1

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
    "docs/SYR_013_BRANCH_UX.md",
    "docs/syr/accessibility-wcag-checklist.json",
    "scripts/syr013-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr013BranchUxTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/syr/accessibility-wcag-checklist.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "SYR-013") { $failures += "checklist card must be SYR-013" }
    if ($check.wcagVersion -ne "2.2") { $failures += "wcagVersion must be 2.2" }
    if ($check.conformanceLevel -ne "AA") { $failures += "conformanceLevel must be AA" }
    if (-not $check.localization.rtlFirst) { $failures += "rtlFirst must be true" }
    if (-not $check.governorates -or $check.governorates.Count -ne 14) {
        $failures += "must list all 14 governorates"
    }
    if (-not $check.criticalJourneys -or $check.criticalJourneys.Count -lt 3) {
        $failures += "must define at least 3 critical journeys"
    }
    if (-not $check.wcagCriteria -or $check.wcagCriteria.Count -lt 5) {
        $failures += "must define at least 5 WCAG criteria"
    }
    if (-not $check.draftPolicy.encryptedSaveRequired) {
        $failures += "encryptedSaveRequired must be true"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_013_BRANCH_UX.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-013") { $failures += "doc missing SYR-013 reference" }
    if ($doc -notmatch "WCAG|accessibility") { $failures += "doc must mention WCAG/accessibility" }
    if ($doc -notmatch "RTL|Arabic") { $failures += "doc must mention Arabic RTL" }
}

$outDir = Resolve-RepoPath "testdata/syr013/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-013"
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

Write-Host "PASS: SYR-013 branch governorate UX validate"
exit 0
