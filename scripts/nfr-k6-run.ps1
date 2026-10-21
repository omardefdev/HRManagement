# NFR-001: run a k6 scenario and capture results under perf/results/
# Usage:
#   pwsh scripts/nfr-k6-run.ps1 -Scenario smoke -Profile smoke
#   pwsh scripts/nfr-k6-run.ps1 -Scenario gateway-mixed -Profile load
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('smoke', 'gateway-mixed', 'lookup-list', 'lookup-list-scoped', 'payroll-run', 'export-async', 'org-tree')]
    [string]$Scenario,

    [ValidateSet('smoke', 'load', 'soak', 'spike', 'stress')]
    [string]$Profile = 'load',

    [string]$BaseUrl = $(if ($env:BASE_URL) { $env:BASE_URL } else { 'http://localhost:5000' }),
    [string]$ResultsRoot = 'perf/results',
    [switch]$Docker
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $ResultsRoot "$stamp-$Profile-$Scenario"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$scriptRel = if ($Scenario -eq 'smoke') { 'perf/k6/smoke.js' } else { "perf/k6/$Scenario.js" }
$absScript = (Resolve-Path $scriptRel).Path
$absRun = (Resolve-Path $runDir).Path

$env:BASE_URL = $BaseUrl
$env:NFR_PROFILE = $Profile
if (-not $env:VUS_BASE) { $env:VUS_BASE = '50' }

$meta = [ordered]@{
    startedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    scenario     = $Scenario
    profile      = $Profile
    baseUrl      = $BaseUrl
    scale_factor = $env:NFR_SCALE_FACTOR
    gitSha       = $(try { git rev-parse --short HEAD 2>$null } catch { 'n/a' })
}
$meta | ConvertTo-Json | Set-Content (Join-Path $runDir 'meta.json') -Encoding UTF8

$summaryOut = Join-Path $absRun 'summary.json'

if ($Docker -or -not (Get-Command k6 -ErrorAction SilentlyContinue)) {
    Write-Host "Running via Docker grafana/k6 ..."
    $mount = (Resolve-Path 'perf/k6').Path -replace '\\', '/'
    # Git Bash / Docker Desktop path fix for Windows
    docker run --rm `
        -e BASE_URL=$BaseUrl `
        -e NFR_PROFILE=$Profile `
        -e ACCESS_TOKEN=$env:ACCESS_TOKEN `
        -e VUS_BASE=$env:VUS_BASE `
        -e SOAK_DURATION=$env:SOAK_DURATION `
        -e SPIKE_VUS=$env:SPIKE_VUS `
        -e PAYROLL_RUN_ID=$env:PAYROLL_RUN_ID `
        -e EXPORT_PATH=$env:EXPORT_PATH `
        -v "${mount}:/scripts" `
        -v "${absRun}:/out" `
        grafana/k6:0.54.0 run `
        --summary-export=/out/summary.json `
        "/scripts/$(Split-Path $scriptRel -Leaf)"
}
else {
    Write-Host "Running local k6 ..."
    Push-Location (Split-Path $absScript)
    try {
        k6 run --summary-export=$summaryOut (Split-Path $absScript -Leaf)
    }
    finally {
        Pop-Location
    }
}

# Copy summary if k6 wrote cwd summary.json
if ((Test-Path 'summary.json') -and -not (Test-Path $summaryOut)) {
    Move-Item 'summary.json' $summaryOut -Force
}

@"
# NFR-001 run notes

- Scenario: $Scenario
- Profile: $Profile
- BaseUrl: $BaseUrl
- Results: $runDir

Attach Prometheus screenshots / queue depths for soak runs.
"@ | Set-Content (Join-Path $runDir 'notes.md') -Encoding UTF8

Write-Host "Results -> $runDir"
