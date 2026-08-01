# ANA-001: validate national analytics artifacts.
# Usage:
#   powershell -File scripts/ana001-validate.ps1

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
    "docs/ANA_001_ANALYTICS.md",
    "docs/ana/semantic-layer.json",
    "scripts/ana001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Ana001AnalyticsTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify semantic layer JSON structure
$semFile = Resolve-RepoPath "docs/ana/semantic-layer.json"
if (Test-Path $semFile) {
    $content = Get-Content -Raw -Path $semFile
    $requiredKeys = @("measures", "dimensions", "snapshot_policy", "permissions")
    foreach ($key in $requiredKeys) {
        if ($content -notmatch $key) {
            $failures += "semantic-layer.json missing key: $key"
        }
    }
    $requiredMeasures = @("headcount", "fte", "vacancy_rate", "payroll_total")
    foreach ($m in $requiredMeasures) {
        if ($content -notmatch $m) {
            $failures += "semantic-layer.json missing measure: $m"
        }
    }
    $requiredDims = @("agency", "governorate", "grade", "legal_regime")
    foreach ($d in $requiredDims) {
        if ($content -notmatch $d) {
            $failures += "semantic-layer.json missing dimension: $d"
        }
    }
    if ($content -notmatch "national_aggregate_separate") {
        $failures += "semantic-layer.json must define national aggregate separation"
    }
}

$docFile = Resolve-RepoPath "docs/ANA_001_ANALYTICS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "ANA-001") { $failures += "doc missing ANA-001 reference" }
    if ($doc -notmatch "semantic") { $failures += "doc must describe semantic layer" }
    if ($doc -notmatch "reconcil") { $failures += "doc must describe reconciliation" }
}

$outDir = Resolve-RepoPath "testdata/ana001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "ANA-001"
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

Write-Host "PASS: ANA-001 national analytics validate"
exit 0
