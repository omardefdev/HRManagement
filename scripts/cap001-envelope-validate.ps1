# CAP-001: validate national envelope harness (matrix, k6 scripts, schema, docs).
# Usage:
#   powershell -File scripts/cap001-envelope-validate.ps1

param(
    [string]$MatrixPath = "perf/scenarios/cap001-national-envelope.json",
    [string]$SchemaPath = "perf/cap001-results-schema.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/cap001/out"
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
    "perf/scenarios/cap001-national-envelope.json",
    "perf/k6/cap001-soak-72h.js",
    "perf/k6/cap001-stress-150.js",
    "perf/k6/cap001-spike-200.js",
    "perf/k6/cap001-failure-during-load.js",
    "perf/k6/lib/cap001.js",
    "perf/cap001-results-schema.json",
    "scripts/cap001-headroom-calculator.ps1",
    "scripts/cap001-envelope-validate.ps1",
    "docs/CAP_001_NATIONAL_ENVELOPE.md",
    "perf/docker-compose.cap001.yml",
    "src/Tests/HRManagement.UnitTests/Cap001EnvelopeTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) {
    $failures += "matrix not found"
} else {
    $matrix = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
    if ($matrix.card -ne "CAP-001") { $failures += "matrix card must be CAP-001" }
    if ([double]$matrix.headroomTargetPct -lt 30) {
        $failures += "headroomTargetPct must be >= 30"
    }

    $requiredScenarioIds = @("soak-100", "stress-150", "spike-200", "failure-during-load", "regional-mix", "payroll-window")
    $ids = @($matrix.scenarios | ForEach-Object { $_.id })
    foreach ($sid in $requiredScenarioIds) {
        if ($ids -notcontains $sid) { $failures += "matrix missing scenario $sid" }
    }

    foreach ($sc in $matrix.scenarios) {
        if ($sc.script -and -not (Test-Path (Resolve-RepoPath ($sc.script -replace '\\', '/')))) {
            if ($sc.script -notmatch 'payroll-run') {
                $failures += "scenario $($sc.id) script missing: $($sc.script)"
            }
        }
    }

    $soak = $matrix.scenarios | Where-Object { $_.id -eq "soak-100" } | Select-Object -First 1
    if ($soak -and $soak.duration -ne "72h") { $failures += "soak-100 duration must be 72h" }
    $stress = $matrix.scenarios | Where-Object { $_.id -eq "stress-150" } | Select-Object -First 1
    if ($stress -and $stress.duration -ne "4h") { $failures += "stress-150 duration must be 4h" }
    $spike = $matrix.scenarios | Where-Object { $_.id -eq "spike-200" } | Select-Object -First 1
    if ($spike -and $spike.duration -ne "15m") { $failures += "spike-200 duration must be 15m" }
}

$schemaFull = Resolve-RepoPath $SchemaPath
if (-not (Test-Path $schemaFull)) {
    $failures += "schema not found"
} else {
    $schema = Get-Content -Raw -Path $schemaFull | ConvertFrom-Json
    $requiredRoot = @("card", "scenarioId", "slo", "latencies", "errorRates", "headroom")
    foreach ($f in $requiredRoot) {
        if (-not $schema.required -contains $f) {
            $failures += "schema missing required property $f"
        }
    }
    if ($schema.properties.headroom.properties.targetPct.const -ne 30) {
        $failures += "schema headroom target must be 30"
    }
}

$docFull = Resolve-RepoPath "docs/CAP_001_NATIONAL_ENVELOPE.md"
if (Test-Path $docFull) {
    $doc = Get-Content -Raw -Path $docFull
    if ($doc -notmatch "72h") { $failures += "doc must mention 72h soak" }
    if ($doc -notmatch "150%") { $failures += "doc must mention 150% stress" }
    if ($doc -notmatch "200%") { $failures += "doc must mention 200% spike" }
    if ($doc -notmatch "30%") { $failures += "doc must mention 30% headroom" }
}

$outFull = Resolve-RepoPath $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "CAP-001"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: CAP-001 envelope validate"
exit 0
