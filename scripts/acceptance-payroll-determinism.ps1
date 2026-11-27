# PLAN-06 AC-21: dual payroll run determinism via gateway API.
# PLAN-59 (P-8): no SQL. The month comes from the class (-PeriodStart, the first day of a month of AC-21's window in
# testdata/acceptance/payroll-months.json, found by AcceptancePayrollMonths); the class may pass the run it created there (-RunAId).
# Run A is cancelled through PLAN-56's API (POST api/payroll/runs/{id}/cancel with a reason and the antiforgery token of
# GET api/payroll/run-lifecycle/antiforgery), which frees the period and every input its calculation took, so Run B reads the same
# frozen inputs; Run B is cancelled the same way at the end, whatever happened.
# Requires: ACCESS_TOKEN (payroll-processor), acceptance stack up, SEED_ACCEPTANCE on payroll-web.
#
# Usage:
#   $env:ACCESS_TOKEN = '<jwt>'
#   pwsh scripts/acceptance-payroll-determinism.ps1 -PeriodStart 2024-01-01 [-RunAId <guid>]

param(
    [Parameter(Mandatory = $true)][string]$PeriodStart,
    [string]$RunAId = "",
    [string]$BaseUrl = $(if ($env:BASE_URL) { $env:BASE_URL } else { 'http://localhost:5200' }),
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
        $env:ACCEPTANCE_EVIDENCE_DIR
    } else {
        Join-Path $root "acceptance-evidence"
    }
    $OutputPath = Join-Path $evidenceRoot "AC-21\determinism-diff.json"
}

if (-not $env:ACCESS_TOKEN) {
    Write-Error "ACCESS_TOKEN is required (payroll-processor Bearer token)."
}

$headers = @{
    Authorization = "Bearer $($env:ACCESS_TOKEN)"
    "Content-Type" = "application/json"
}

function Invoke-PayrollApi {
    param([string]$Method, [string]$Path, [object]$Body = $null)
    $uri = "$($BaseUrl.TrimEnd('/'))/$Path"
    if ($Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body ($Body | ConvertTo-Json -Depth 6)
    }
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
}

function Get-RunLineSignature($Run) {
    $lines = @($Run.salaryLines)
    if (-not $lines -or $lines.Count -eq 0) { return @() }
    return $lines |
        Sort-Object employeeId |
        ForEach-Object {
            [ordered]@{
                employeeId = $_.employeeId
                grossPay = [decimal]$_.grossPay
                totalDeductions = [decimal]$_.totalDeductions
                netPay = [decimal]$_.netPay
            }
        }
}

function Cancel-PayrollRun([string]$RunId, [string]$Reason) {
    # PLAN-56: a run that is not posted is cancelled with a reason; its period and its inputs are free again. The antiforgery cookie
    # goes through a session (Windows PowerShell 5.1 drops a Cookie entry in -Headers without a word). The service scopes it to its UI
    # path (/ui/payroll); the API sits under /api/payroll, so it is added again for "/".
    $base = $BaseUrl.TrimEnd('/')
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $auth = @{ Authorization = "Bearer $($env:ACCESS_TOKEN)" }
    $issued = Invoke-WebRequest -UseBasicParsing -Method Get -Uri "$base/api/payroll/run-lifecycle/antiforgery" -Headers $auth -WebSession $session
    $token = $issued.Content | ConvertFrom-Json
    $cookie = [regex]::Match((@($issued.Headers['Set-Cookie']) -join ','), '(\.AspNetCore\.Antiforgery\.[^=;,\s]+)=([^;,\s]+)')
    if (-not $cookie.Success -or -not $token.headerName -or -not $token.requestToken) {
        throw "The antiforgery token of api/payroll/run-lifecycle/antiforgery was not issued."
    }
    $session.Cookies.Add([Uri]$base, (New-Object Net.Cookie($cookie.Groups[1].Value, $cookie.Groups[2].Value, '/')))
    $h = @{ Authorization = "Bearer $($env:ACCESS_TOKEN)" }
    $h[[string]$token.headerName] = [string]$token.requestToken
    $body = @{ reason = $Reason } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$base/api/payroll/runs/$RunId/cancel" -Headers $h -ContentType "application/json" -Body $body -WebSession $session | Out-Null
}

$start = [datetime]::ParseExact($PeriodStart, 'yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
if ($start.Day -ne 1) { throw "-PeriodStart must be the first day of a month: $PeriodStart" }
$runPeriodStart = [datetime]::SpecifyKind($start, [DateTimeKind]::Utc)
$runPeriodEnd = $runPeriodStart.AddMonths(1).AddDays(-1)
$runDate = $runPeriodEnd

$createPayload = @{
    periodStart = $runPeriodStart.ToString("o")
    periodEnd = $runPeriodEnd.ToString("o")
    runDate = $runDate.ToString("o")
}

$runBId = $null
$cancelled = @{}
try {
    if ($RunAId) {
        Write-Host "Run A is the class's run ($RunAId)."
        $runAId = $RunAId
    }
    else {
        Write-Host "Creating Run A..."
        $runA = Invoke-PayrollApi -Method Post -Path "api/payroll/runs" -Body $createPayload
        $runAId = $runA.id
    }
    Write-Host "Calculating Run A ($runAId)..."
    Invoke-PayrollApi -Method Post -Path "api/payroll/runs/$runAId/calculate" | Out-Null
    $provA = Invoke-PayrollApi -Method Get -Path "api/payroll/runs/$runAId/provenance"
    $runADetail = Invoke-PayrollApi -Method Get -Path "api/payroll/runs/$runAId"
    $sigA = Get-RunLineSignature $runADetail

    Write-Host "Cancelling Run A through the API (frees the period and its inputs)..."
    Cancel-PayrollRun $runAId "AC-21: determinism experiment, run A (PLAN-59)"
    $cancelled[$runAId] = $true

    Write-Host "Creating Run B (same period)..."
    $runB = Invoke-PayrollApi -Method Post -Path "api/payroll/runs" -Body $createPayload
    $runBId = $runB.id
    Write-Host "Calculating Run B ($runBId)..."
    Invoke-PayrollApi -Method Post -Path "api/payroll/runs/$runBId/calculate" | Out-Null
    $provB = Invoke-PayrollApi -Method Get -Path "api/payroll/runs/$runBId/provenance"
    $runBDetail = Invoke-PayrollApi -Method Get -Path "api/payroll/runs/$runBId"
    $sigB = Get-RunLineSignature $runBDetail
}
finally {
    # Leave the shared stack reusable: neither run keeps the month or an input, whatever happened above.
    foreach ($id in @($runAId, $runBId)) {
        if ($id -and -not $cancelled[$id]) {
            try { Cancel-PayrollRun $id "AC-21: determinism experiment ended (PLAN-59)"; $cancelled[$id] = $true }
            catch { Write-Host "Cancelling run $id failed: $($_.Exception.Message)" }
        }
    }
}

$diff = @{
    periodStart = $runPeriodStart.ToString("yyyy-MM-dd")
    runAId = $runAId
    runBId = $runBId
    configHashMatch = ($provA.configContentHash -eq $provB.configContentHash)
    lineCountA = @($sigA).Count
    lineCountB = @($sigB).Count
    signaturesMatch = ($sigA | ConvertTo-Json -Compress) -eq ($sigB | ConvertTo-Json -Compress)
    provenanceA = $provA
    provenanceB = $provB
    differences = @()
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}

if (-not $diff.signaturesMatch) {
    $max = [Math]::Max(@($sigA).Count, @($sigB).Count)
    for ($i = 0; $i -lt $max; $i++) {
        $a = if ($i -lt @($sigA).Count) { $sigA[$i] } else { $null }
        $b = if ($i -lt @($sigB).Count) { $sigB[$i] } else { $null }
        if (($a | ConvertTo-Json -Compress) -ne ($b | ConvertTo-Json -Compress)) {
            $diff.differences += [ordered]@{ index = $i; runA = $a; runB = $b }
        }
    }
}

$dir = Split-Path -Parent $OutputPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$provAPath = Join-Path $dir "run-a-provenance.json"
$provBPath = Join-Path $dir "run-b-provenance.json"
$provA | ConvertTo-Json -Depth 6 | Set-Content -Path $provAPath -Encoding UTF8
$provB | ConvertTo-Json -Depth 6 | Set-Content -Path $provBPath -Encoding UTF8

$diff.cancelledThroughApi = (@($runAId, $runBId) | Where-Object { $cancelled[$_] }).Count
$diff | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8

Write-Host ($diff | ConvertTo-Json -Depth 6)
if ($diff.signaturesMatch -and $diff.configHashMatch -and $diff.lineCountA -gt 0 -and $diff.cancelledThroughApi -eq 2) {
    Write-Host "PASS: AC-21 payroll determinism"
    exit 0
}

Write-Host "FAIL: determinism diff non-empty, zero lines, or a run not cancelled through the API"
exit 1
