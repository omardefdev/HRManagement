# PLAN-13 AC-35: scoped lookup k6 run + percentile summary for evidence.
param(
    [string]$BaseUrl = $(if ($env:BASE_URL) { $env:BASE_URL } else { "http://localhost:5200" }),
    [string]$KeycloakBase = $(if ($env:KEYCLOAK_BASE_URL) { $env:KEYCLOAK_BASE_URL } else { "http://localhost:8088" }),
    [string]$BranchUser = "branch-mgr-br1",
    [string]$Password = $(if ($env:ACCEPTANCE_PASSWORD) { $env:ACCEPTANCE_PASSWORD } else { "Acceptance!123" }),
    [ValidateSet('smoke', 'load')][string]$Profile = "load"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$evidenceDir = Join-Path $root "acceptance-evidence\AC-35"
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

function Get-UserToken([string]$Username) {
    $form = @{
        grant_type = "password"
        client_id  = "hr-web"
        username   = $Username
        password   = $Password
    }
    $resp = Invoke-RestMethod -Method Post -Uri "$KeycloakBase/realms/hrmanagement/protocol/openid-connect/token" -Body $form
    return $resp.access_token
}

$env:ACCESS_TOKEN = Get-UserToken $BranchUser
$env:BASE_URL = $BaseUrl
$env:NFR_PROFILE = $Profile
$env:VUS_BASE = if ($env:VUS_BASE) { $env:VUS_BASE } else { "20" }

$runDir = Join-Path $root "perf/results/ac35-$Profile-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
& pwsh (Join-Path $root "scripts/nfr-k6-run.ps1") -Scenario lookup-list-scoped -Profile $Profile -BaseUrl $BaseUrl -ResultsRoot (Split-Path $runDir -Parent) -Docker

$latest = Get-ChildItem (Join-Path $root "perf/results") -Directory |
    Where-Object { $_.Name -like "*lookup-list-scoped*" } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $latest) { Write-Error "k6 run directory not found" }

$summarySrc = Join-Path $latest.FullName "summary.json"
if (-not (Test-Path $summarySrc)) { Write-Error "summary.json missing from k6 run" }

$summarized = Join-Path $evidenceDir "summary.json"
& pwsh (Join-Path $root "scripts/nfr-k6-summarize.ps1") -SummaryPath $summarySrc -OutputPath $summarized

@{
    scenario = "lookup-list-scoped"
    profile = $Profile
    persona = $BranchUser
    baseUrl = $BaseUrl
    runDir = $latest.FullName
    capPerfDeferred = $true
} | ConvertTo-Json | Set-Content (Join-Path $evidenceDir "meta.json") -Encoding UTF8

@"
# AC-35 result

Pass — measured p50/p95/p99 and error rate captured in summary.json.
CAP-PERF-01 numeric SLO targets remain **Deferred** (lab measurement only).
"@ | Set-Content (Join-Path $evidenceDir "result.md") -Encoding UTF8

Write-Host "PASS AC-35 perf evidence -> $evidenceDir" -ForegroundColor Green
