# CAP-001 — compute headroom % from k6 summary JSON (+ optional resource snapshot)
# Usage:
#   pwsh scripts/cap001-headroom-calculator.ps1 -K6Summary perf/results/run/summary.json
#   pwsh scripts/cap001-headroom-calculator.ps1 -K6Summary summary.json -ResourceSnapshot metrics.json -ScenarioId soak-100

param(
    [Parameter(Mandatory = $true)]
    [string]$K6Summary,

    [string]$ScenarioId = "soak-100",
    [string]$MatrixPath = "perf/scenarios/cap001-national-envelope.json",
    [string]$ResourceSnapshot = "",
    [double]$VusConfigured = 0,
    [string]$OutPath = "",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$summaryFull = if ([IO.Path]::IsPathRooted($K6Summary)) { $K6Summary } else { Resolve-RepoPath $K6Summary }
if (-not (Test-Path $summaryFull)) { Write-Error "k6 summary not found: $summaryFull" }

$matrixFull = Resolve-RepoPath $MatrixPath
$matrix = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$scenario = $matrix.scenarios | Where-Object { $_.id -eq $ScenarioId } | Select-Object -First 1
if ($null -eq $scenario) { Write-Error "unknown scenario: $ScenarioId" }

$headroomTarget = [double]$matrix.headroomTargetPct
$k6 = Get-Content -Raw -Path $summaryFull | ConvertFrom-Json

function Get-MetricValue($root, [string]$name, [string]$stat) {
    if ($null -eq $root.metrics) { return $null }
    $m = $root.metrics.$name
    if ($null -eq $m) { return $null }
    if ($m.values -and $null -ne $m.values.$stat) { return [double]$m.values.$stat }
    if ($m.$stat) { return [double]$m.$stat }
    return $null
}

$p50 = Get-MetricValue $k6 "http_req_duration" "p(50)"
$p95 = Get-MetricValue $k6 "http_req_duration" "p(95)"
$p99 = Get-MetricValue $k6 "http_req_duration" "p(99)"
$maxMs = Get-MetricValue $k6 "http_req_duration" "max"
$failedRate = Get-MetricValue $k6 "http_req_failed" "rate"
$checksRate = Get-MetricValue $k6 "checks" "rate"

$sloFailedMax = [double]$scenario.slo.http_req_failed_rate_max
$p95Max = [double]$scenario.slo.http_req_duration_p95_ms_max

$sloChecks = @()
$sloPass = $true

if ($null -ne $failedRate) {
    $failOk = $failedRate -le $sloFailedMax
    $sloPass = $sloPass -and $failOk
    $sloChecks += [ordered]@{
        id = "http_req_failed_rate"
        pass = $failOk
        target = $sloFailedMax
        observed = $failedRate
    }
}

if ($null -ne $p95) {
    $p95Ok = $p95 -le $p95Max
    $sloPass = $sloPass -and $p95Ok
    $sloChecks += [ordered]@{
        id = "http_req_duration_p95_ms"
        pass = $p95Ok
        target = $p95Max
        observed = $p95
    }
}

$resourcePeaks = @{}
$headroomMethod = "slo_latency_margin"
$computedHeadroom = $null
$headroomNotes = ""

if ($ResourceSnapshot -and (Test-Path (Resolve-RepoPath $ResourceSnapshot))) {
    $snap = Get-Content -Raw -Path (Resolve-RepoPath $ResourceSnapshot) | ConvertFrom-Json
    foreach ($key in @("cpu_utilization_pct", "memory_utilization_pct", "db_connections_pct")) {
        if ($null -ne $snap.$key) { $resourcePeaks[$key] = [double]$snap.$key }
    }
    if ($snap.rabbit_queue_depth_max) { $resourcePeaks["rabbit_queue_depth_max"] = [double]$snap.rabbit_queue_depth_max }

    $peakUtil = 0.0
    foreach ($k in @("cpu_utilization_pct", "memory_utilization_pct", "db_connections_pct")) {
        if ($resourcePeaks.ContainsKey($k) -and $resourcePeaks[$k] -gt $peakUtil) {
            $peakUtil = $resourcePeaks[$k]
        }
    }
    if ($peakUtil -gt 0) {
        $headroomMethod = "resource_peak"
        $computedHeadroom = [math]::Round(100.0 - $peakUtil, 2)
        $headroomNotes = "Headroom from max critical resource peak $peakUtil%"
    }
}

if ($null -eq $computedHeadroom) {
    if ($null -ne $p95 -and $p95Max -gt 0) {
        $utilRatio = [math]::Min(1.0, $p95 / $p95Max)
        $computedHeadroom = [math]::Round((1.0 - $utilRatio) * 100.0, 2)
        $headroomNotes = "Estimated from p95 latency margin vs SLO ceiling"
    } elseif ($VusConfigured -gt 0) {
        $baseVus = [double]$matrix.labScaling.vus_base_100pct
        $headroomMethod = "vus_linear_estimate"
        $utilRatio = [math]::Min(1.0, $VusConfigured / ($baseVus * 1.3))
        $computedHeadroom = [math]::Round((1.0 - $utilRatio) * 100.0, 2)
        $headroomNotes = "Linear VU estimate; attach resource snapshot for authoritative headroom"
    } else {
        $computedHeadroom = 0.0
        $headroomNotes = "Insufficient data; provide ResourceSnapshot or VusConfigured"
    }
}

$headroomPass = $computedHeadroom -ge $headroomTarget -and $sloPass
$now = (Get-Date).ToUniversalTime().ToString("o")

$result = [ordered]@{
    card = "CAP-001"
    scenarioId = $ScenarioId
    kind = $scenario.kind
    loadPct = $scenario.loadPct
    runFinishedAtUtc = $now
    k6SummaryPath = $summaryFull
    slo = [ordered]@{
        pass = $sloPass
        checks = $sloChecks
    }
    latencies = [ordered]@{
        http_req_duration_p50_ms = $p50
        http_req_duration_p95_ms = $p95
        http_req_duration_p99_ms = $p99
        http_req_duration_max_ms = $maxMs
    }
    errorRates = [ordered]@{
        http_req_failed_rate = $failedRate
        checks_pass_rate = $checksRate
    }
    headroom = [ordered]@{
        targetPct = $headroomTarget
        computedPct = $computedHeadroom
        pass = $headroomPass
        method = $headroomMethod
        resourcePeaks = $resourcePeaks
        notes = $headroomNotes
    }
}

if (-not $OutPath) {
    $OutPath = Join-Path (Split-Path $summaryFull -Parent) "cap001-results.json"
} elseif (-not [IO.Path]::IsPathRooted($OutPath)) {
    $OutPath = Resolve-RepoPath $OutPath
}

($result | ConvertTo-Json -Depth 8) | Set-Content -Path $OutPath -Encoding utf8
Write-Host "Wrote $OutPath"
Write-Host "Headroom: $($computedHeadroom)% (target $($headroomTarget)%) pass=$headroomPass sloPass=$sloPass"

if (-not $headroomPass) { exit 2 }
exit 0
