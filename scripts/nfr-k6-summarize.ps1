# PLAN-13 AC-35: extract p50/p95/p99 + error rate from k6 summary.json
param(
    [Parameter(Mandatory = $true)][string]$SummaryPath,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $SummaryPath)) { Write-Error "Summary not found: $SummaryPath" }

$raw = Get-Content -Raw $SummaryPath | ConvertFrom-Json
$dur = $raw.metrics.http_req_duration.values
$failed = $raw.metrics.http_req_failed.values

$p50 = if ($null -ne $dur.'p(50)') { $dur.'p(50)' } elseif ($null -ne $dur.med) { $dur.med } else { $null }
$p95 = $dur.'p(95)'
$p99 = $dur.'p(99)'

$result = [ordered]@{
    p50_ms = $p50
    p95_ms = $p95
    p99_ms = $p99
    http_req_failed_rate = $failed.rate
    http_req_duration_avg_ms = $dur.avg
    slo_claimed = $false
    note = "Measured lab numbers; CAP-PERF-01 targets remain Deferred per acceptance plan."
    sourceSummary = (Resolve-Path $SummaryPath).Path
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}

if (-not $OutputPath) {
    $OutputPath = Join-Path (Split-Path $SummaryPath -Parent) "summarized.json"
}
($result | ConvertTo-Json -Depth 4) | Set-Content $OutputPath -Encoding UTF8
Write-Host "Summarized -> $OutputPath"
$result | ConvertTo-Json -Depth 4
