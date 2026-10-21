# PLAN-06 AC-21: dual payroll run determinism via gateway API.
# Requires: ACCESS_TOKEN (payroll-processor), acceptance stack up, SEED_ACCEPTANCE on payroll-web.
#
# Usage:
#   $env:ACCESS_TOKEN = '<jwt>'
#   pwsh scripts/acceptance-payroll-determinism.ps1

param(
    [string]$BaseUrl = $(if ($env:BASE_URL) { $env:BASE_URL } else { 'http://localhost:5200' }),
    [string]$OutputPath = "",
    [string]$PostgresContainer = "",
    [string]$PayrollDb = "payroll_db"
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

if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    $PostgresContainer = docker ps `
        --filter "label=com.docker.compose.project=hrm-acceptance" `
        --filter "label=com.docker.compose.service=postgresql" `
        --format "{{.Names}}" | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    throw "The hrm-acceptance PostgreSQL container is not running."
}

function Invoke-PayrollSql([string]$Sql) {
    $output = docker exec $PostgresContainer psql -v ON_ERROR_STOP=1 -U hrm_ci -d $PayrollDb -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Payroll database command failed: $output"
    }
    return $output
}

function Cancel-PayrollRunInDb([string]$RunId) {
    # Calculation claims one-off inputs atomically. The determinism experiment must
    # release those claims when it cancels its disposable run, otherwise Run B no
    # longer receives the same frozen inputs as Run A.
    $sql = @"
BEGIN;
UPDATE "DeductionRecords" SET "AppliedInRunId" = NULL WHERE "AppliedInRunId" = '$RunId';
UPDATE "AbsenceDeductions" SET "AppliedInRunId" = NULL WHERE "AppliedInRunId" = '$RunId';
UPDATE "PayrollAdjustments" SET "PayrollRunId" = NULL WHERE "PayrollRunId" = '$RunId';
UPDATE "PayrollRuns" SET "Status" = 8 WHERE "Id" = '$RunId';
COMMIT;
"@
    Invoke-PayrollSql $sql | Out-Null
}

function Reset-ReservedPeriod {
    $sql = @'
BEGIN;
CREATE TEMP TABLE acceptance_reserved_runs ON COMMIT DROP AS
SELECT "Id"
FROM "PayrollRuns"
WHERE "PeriodStart" = TIMESTAMPTZ '2027-01-01T00:00:00Z'
  AND "PeriodEnd" = TIMESTAMPTZ '2027-01-31T00:00:00Z';

UPDATE "DeductionRecords"
SET "AppliedInRunId" = NULL
WHERE "AppliedInRunId" IN (SELECT "Id" FROM acceptance_reserved_runs);

UPDATE "AbsenceDeductions"
SET "AppliedInRunId" = NULL
WHERE "AppliedInRunId" IN (SELECT "Id" FROM acceptance_reserved_runs);

UPDATE "PayrollAdjustments"
SET "PayrollRunId" = NULL
WHERE "PayrollRunId" IN (SELECT "Id" FROM acceptance_reserved_runs);

UPDATE "PayrollRuns"
SET "Status" = 8
WHERE "Id" IN (SELECT "Id" FROM acceptance_reserved_runs)
  AND "Status" NOT IN (5, 8);
COMMIT;
'@
    Invoke-PayrollSql $sql | Out-Null
}

$periodStart = [datetime]::SpecifyKind([datetime]'2027-01-01', [DateTimeKind]::Utc)
$periodEnd = [datetime]::SpecifyKind([datetime]'2027-01-31', [DateTimeKind]::Utc)
$runDate = [datetime]::SpecifyKind([datetime]'2026-08-01', [DateTimeKind]::Utc)

$createPayload = @{
    periodStart = $periodStart.ToString("o")
    periodEnd = $periodEnd.ToString("o")
    runDate = $runDate.ToString("o")
}

Reset-ReservedPeriod

Write-Host "Creating Run A..."
$runA = Invoke-PayrollApi -Method Post -Path "api/payroll/runs" -Body $createPayload
$runAId = $runA.id
Write-Host "Calculating Run A ($runAId)..."
Invoke-PayrollApi -Method Post -Path "api/payroll/runs/$runAId/calculate" | Out-Null
$provA = Invoke-PayrollApi -Method Get -Path "api/payroll/runs/$runAId/provenance"
$runADetail = Invoke-PayrollApi -Method Get -Path "api/payroll/runs/$runAId"
$sigA = Get-RunLineSignature $runADetail

Write-Host "Cancelling Run A in DB (free live period slot)..."
Cancel-PayrollRunInDb $runAId

Write-Host "Creating Run B (same period)..."
$runB = Invoke-PayrollApi -Method Post -Path "api/payroll/runs" -Body $createPayload
$runBId = $runB.id
Write-Host "Calculating Run B ($runBId)..."
Invoke-PayrollApi -Method Post -Path "api/payroll/runs/$runBId/calculate" | Out-Null
$provB = Invoke-PayrollApi -Method Get -Path "api/payroll/runs/$runBId/provenance"
$runBDetail = Invoke-PayrollApi -Method Get -Path "api/payroll/runs/$runBId"
$sigB = Get-RunLineSignature $runBDetail

$diff = @{
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
$diff | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8

$provAPath = Join-Path $dir "run-a-provenance.json"
$provBPath = Join-Path $dir "run-b-provenance.json"
$provA | ConvertTo-Json -Depth 6 | Set-Content -Path $provAPath -Encoding UTF8
$provB | ConvertTo-Json -Depth 6 | Set-Content -Path $provBPath -Encoding UTF8

# Leave the shared Acceptance environment reusable for any subsequent payroll
# scenario while retaining both runs and their provenance as evidence.
Cancel-PayrollRunInDb $runBId

Write-Host ($diff | ConvertTo-Json -Depth 6)
if ($diff.signaturesMatch -and $diff.configHashMatch -and $diff.lineCountA -gt 0) {
    Write-Host "PASS: AC-21 payroll determinism"
    exit 0
}

Write-Host "FAIL: determinism diff non-empty or zero lines"
exit 1
