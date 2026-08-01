# FIN-002: validate cost allocation rules artifacts.
# Usage:
#   powershell -File scripts/fin002-validate.ps1

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
    "docs/FIN_002_COST_ALLOCATION.md",
    "docs/fin/cost-allocation-rules.json",
    "scripts/fin002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Fin002CostTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$rulesFile = Resolve-RepoPath "docs/fin/cost-allocation-rules.json"
if (Test-Path $rulesFile) {
    $rules = Get-Content -Raw -Path $rulesFile | ConvertFrom-Json
    if ($rules.card -ne "FIN-002") { $failures += "rules card must be FIN-002" }
    if ($rules.allocationPolicy.requiredCoveragePercent -ne 100) {
        $failures += "requiredCoveragePercent must be 100"
    }
    if (-not $rules.budgets -or $rules.budgets.Count -lt 1) {
        $failures += "must define at least 1 budget"
    }
    $has70 = $false
    $has85 = $false
    $has100 = $false
    foreach ($budget in $rules.budgets) {
        foreach ($threshold in $budget.alertThresholds) {
            if ($threshold.percent -eq 70) { $has70 = $true }
            if ($threshold.percent -eq 85) { $has85 = $true }
            if ($threshold.percent -eq 100) { $has100 = $true }
        }
    }
    if (-not ($has70 -and $has85 -and $has100)) {
        $failures += "budgets must include 70/85/100% alert thresholds"
    }
    if (-not $rules.anomalyDetection -or $rules.anomalyDetection.frequency -ne "daily") {
        $failures += "anomaly detection must be daily"
    }
    if (-not $rules.optimizationGuardrails.sloPreservationRequired) {
        $failures += "sloPreservationRequired must be true"
    }
    if (-not $rules.unitMetrics -or $rules.unitMetrics.Count -lt 2) {
        $failures += "must define at least 2 unit metrics"
    }
}

$docFile = Resolve-RepoPath "docs/FIN_002_COST_ALLOCATION.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "FIN-002") { $failures += "doc missing FIN-002 reference" }
    if ($doc -notmatch "allocation|budget") { $failures += "doc must describe allocation/budget" }
    if ($doc -notmatch "anomaly|forecast") { $failures += "doc must mention anomaly/forecast" }
}

$outDir = Resolve-RepoPath "testdata/fin002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "FIN-002"
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

Write-Host "PASS: FIN-002 cost allocation validate"
exit 0
