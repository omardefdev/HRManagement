# PLAN-06 AC-22: kill mid-partition + resume without duplicate salary lines.
# Requires: ACCESS_TOKEN, acceptance stack, SEED_ACCEPTANCE payroll profiles (>=52 employees).
#
# Usage:
#   $env:ACCESS_TOKEN = '<jwt>'
#   $env:RUN_CHAOS = 'true'
#   pwsh scripts/acceptance-payroll-resume-chaos.ps1

param(
    [string]$BaseUrl = $(if ($env:BASE_URL) { $env:BASE_URL } else { 'http://localhost:5000' }),
    [string]$OutputDir = "",
    [string]$PostgresContainer = "",
    [string]$PayrollDb = "payroll_db",
    [string]$PayrollContainer = "",
    [int]$PollTimeoutSec = 120,
    [switch]$UseDockerKill = ([string]::Equals($env:RUN_CHAOS, 'true', [System.StringComparison]::OrdinalIgnoreCase))
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) {
    $OutputDir = Join-Path $root "acceptance-evidence\AC-22"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutputDir "db-checks") | Out-Null

if (-not $env:ACCESS_TOKEN) {
    Write-Error "ACCESS_TOKEN is required."
}

function Resolve-ComposeServiceContainer([string]$Service, [string]$Override = "") {
    if (-not [string]::IsNullOrWhiteSpace($Override)) { return $Override }
    $container = docker ps `
        --filter "label=com.docker.compose.project=hrm-acceptance" `
        --filter "label=com.docker.compose.service=$Service" `
        --format "{{.Names}}" | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($container)) {
        throw "The hrm-acceptance container for service '$Service' is not running."
    }
    return $container
}

$PostgresContainer = Resolve-ComposeServiceContainer "postgresql" $PostgresContainer
$PayrollContainer = Resolve-ComposeServiceContainer "payroll-web" $PayrollContainer

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

function Invoke-PsqlJson([string]$Sql) {
    $raw = docker exec $PostgresContainer psql -X -v ON_ERROR_STOP=1 -U hrm_ci -d $PayrollDb -t -A -c $Sql 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $raw.Trim()
}

function Invoke-PayrollSql([string]$Sql) {
    $raw = docker exec $PostgresContainer psql -X -v ON_ERROR_STOP=1 -U hrm_ci -d $PayrollDb -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Payroll database command failed: $raw" }
    return $raw
}

function Reset-ReservedPeriod([datetime]$Start, [datetime]$End) {
    $startText = $Start.ToString("yyyy-MM-dd")
    $endText = $End.ToString("yyyy-MM-dd")
    $sql = @"
BEGIN;
CREATE TEMP TABLE acceptance_reserved_runs ON COMMIT DROP AS
SELECT "Id" FROM "PayrollRuns"
WHERE "PeriodStart" = TIMESTAMPTZ '$($startText)T00:00:00Z'
  AND "PeriodEnd" = TIMESTAMPTZ '$($endText)T00:00:00Z';
UPDATE "DeductionRecords" SET "AppliedInRunId" = NULL
WHERE "AppliedInRunId" IN (SELECT "Id" FROM acceptance_reserved_runs);
UPDATE "AbsenceDeductions" SET "AppliedInRunId" = NULL
WHERE "AppliedInRunId" IN (SELECT "Id" FROM acceptance_reserved_runs);
UPDATE "PayrollAdjustments" SET "PayrollRunId" = NULL
WHERE "PayrollRunId" IN (SELECT "Id" FROM acceptance_reserved_runs);
UPDATE "PayrollRuns" SET "Status" = 8
WHERE "Id" IN (SELECT "Id" FROM acceptance_reserved_runs)
  AND "Status" NOT IN (5, 8);
COMMIT;
"@
    Invoke-PayrollSql $sql | Out-Null
}

function Cancel-TestRun([string]$RunId) {
    $sql = @"
BEGIN;
UPDATE "DeductionRecords" SET "AppliedInRunId" = NULL WHERE "AppliedInRunId" = '$RunId';
UPDATE "AbsenceDeductions" SET "AppliedInRunId" = NULL WHERE "AppliedInRunId" = '$RunId';
UPDATE "PayrollAdjustments" SET "PayrollRunId" = NULL WHERE "PayrollRunId" = '$RunId';
UPDATE "PayrollRuns" SET "Status" = 8 WHERE "Id" = '$RunId' AND "Status" NOT IN (5, 8);
COMMIT;
"@
    Invoke-PayrollSql $sql | Out-Null
}

function Get-PartitionSnapshot([string]$RunId) {
    $sql = @"
SELECT COALESCE(json_agg(row_to_json(t)), '[]'::json)::text
FROM (
  SELECT p."PartitionIndex", p."Status", p."ProcessedCount", p."EmployeeCount",
         j."ProcessedEmployeeCount", j."TotalEmployeeCount", j."LastCheckpointAt"::text,
         j."LeaseOwner", j."Status" AS job_status, j."Id" AS job_id
  FROM "PayrollRunPartitions" p
  JOIN "PayrollRunJobs" j ON j."Id" = p."PayrollRunJobId"
  WHERE j."PayrollRunId" = '$RunId'
  ORDER BY p."PartitionIndex"
) t;
"@
    $raw = Invoke-PsqlJson $sql
    if (-not $raw) { return @() }
    return $raw | ConvertFrom-Json
}

function Get-SalaryLineCounts([string]$RunId) {
    $total = Invoke-PsqlJson "SELECT COUNT(*) FROM ""SalaryLines"" WHERE ""PayrollRunId"" = '$RunId';"
    $dupes = Invoke-PsqlJson @"
SELECT COUNT(*) FROM (
  SELECT ""EmployeeId"" FROM ""SalaryLines"" WHERE ""PayrollRunId"" = '$RunId'
  GROUP BY ""EmployeeId"" HAVING COUNT(*) > 1
) d;
"@
    return [ordered]@{
        totalLines = [int]$total
        duplicateEmployeeGroups = [int]$dupes
    }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$periodStart = [datetime]::SpecifyKind([datetime]'2027-03-01', [DateTimeKind]::Utc)
$periodEnd = [datetime]::SpecifyKind([datetime]'2027-03-31', [DateTimeKind]::Utc)
$runDate = [datetime]::SpecifyKind([datetime]'2026-08-01', [DateTimeKind]::Utc)
Reset-ReservedPeriod $periodStart $periodEnd

$createPayload = @{
    periodStart = $periodStart.ToString("o")
    periodEnd = $periodEnd.ToString("o")
    runDate = $runDate.ToString("o")
}

Write-Host "Creating payroll run for chaos resume..."
$run = Invoke-PayrollApi -Method Post -Path "api/payroll/runs" -Body $createPayload
$runId = $run.id

$calcScript = {
    param($Base, $Token, $Rid)
    $h = @{ Authorization = "Bearer $Token"; "Content-Type" = "application/json" }
    try {
        Invoke-RestMethod -Method Post -Uri "$Base/api/payroll/runs/$Rid/calculate" -Headers $h -TimeoutSec 600 | Out-Null
    }
    catch {
        # Expected when container is killed mid-request.
    }
}

Write-Host "Starting calculate in background..."
$job = Start-Job -ScriptBlock $calcScript -ArgumentList $BaseUrl.TrimEnd('/'), $env:ACCESS_TOKEN, $runId

$jobId = $null
$deadline = (Get-Date).AddSeconds($PollTimeoutSec)
$midPartition = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $parts = Get-PartitionSnapshot $runId
    if ($parts -and $parts.Count -gt 0) {
        $jobId = $parts[0].job_id
        $processed = [int]$parts[0].processedemployeecount
        $total = [int]$parts[0].totalemployeecount
        if ($processed -gt 0 -and $processed -lt $total) {
            $midPartition = $true
            break
        }
        if ($total -gt 0 -and $processed -ge $total) {
            break
        }
    }
}

$beforeParts = Get-PartitionSnapshot $runId
$beforeLines = Get-SalaryLineCounts $runId
$beforeParts | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDir "db-checks\partitions-before.json") -Encoding UTF8
$beforeLines | ConvertTo-Json | Set-Content (Join-Path $OutputDir "db-checks\salary-line-counts-before.json") -Encoding UTF8

$interruptionApplied = $false
if ($UseDockerKill -and $midPartition) {
    # A graceful stop can give the in-flight calculation enough time to finish,
    # which would turn this into a timing test instead of a crash-recovery test.
    Write-Host "RUN_CHAOS: docker kill $PayrollContainer mid-partition..."
    docker kill $PayrollContainer | Out-Null
    Start-Sleep -Seconds 3
    Write-Host "docker start $PayrollContainer..."
    docker start $PayrollContainer | Out-Null
    $healthDeadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $healthDeadline) {
        try {
            Invoke-RestMethod -Uri "$($BaseUrl.TrimEnd('/'))/health/upstreams/payroll" -TimeoutSec 5 | Out-Null
            break
        }
        catch { Start-Sleep -Seconds 3 }
    }
    Write-Host "Marking the interrupted running job as paused after process recovery..."
    Invoke-PayrollApi -Method Post -Path "api/payroll/jobs/$jobId/pause" | Out-Null
    $interruptionApplied = $true
}
elseif ($jobId -and $midPartition) {
    Write-Host "Pausing job $jobId (CI-friendly path)..."
    Invoke-PayrollApi -Method Post -Path "api/payroll/jobs/$jobId/pause" | Out-Null
    $interruptionApplied = $true
}

if ($jobId -and $interruptionApplied) {
    Wait-Job $job -Timeout 30 | Out-Null
    $pausedDeadline = (Get-Date).AddSeconds(30)
    $paused = $false
    while ((Get-Date) -lt $pausedDeadline) {
        $snapshot = Get-PartitionSnapshot $runId
        if ($snapshot -and [string]$snapshot[0].job_status -eq "Paused") {
            $paused = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $paused) {
        # Do not leave a live reserved run behind when the assertion fails. This
        # script is also used as a prerequisite for the following payroll cases.
        Cancel-TestRun $runId
        throw "Interrupted payroll job did not reach Paused before resume."
    }
    Write-Host "Resuming job $jobId..."
    Invoke-PayrollApi -Method Post -Path "api/payroll/jobs/$jobId/resume" | Out-Null
}

Remove-Job $job -Force -ErrorAction SilentlyContinue

$resumeDeadline = (Get-Date).AddSeconds($PollTimeoutSec)
while ((Get-Date) -lt $resumeDeadline) {
    $parts = Get-PartitionSnapshot $runId
    if ($parts -and $parts.Count -gt 0) {
        $processed = [int]$parts[0].processedemployeecount
        $total = [int]$parts[0].totalemployeecount
        $status = [string]$parts[0].job_status
        if ($total -gt 0 -and $processed -ge $total -and $status -eq "Completed") { break }
    }
    Start-Sleep -Seconds 2
}

$afterParts = Get-PartitionSnapshot $runId
$afterLines = Get-SalaryLineCounts $runId
$afterParts | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDir "db-checks\partitions-after.json") -Encoding UTF8
$afterLines | ConvertTo-Json | Set-Content (Join-Path $OutputDir "db-checks\salary-line-counts.json") -Encoding UTF8

$leaseConflict = $null
try {
    Invoke-PayrollApi -Method Post -Path "api/payroll/runs/$runId/calculate" | Out-Null
    $leaseConflict = [ordered]@{ conflict = $false; statusCode = 200 }
}
catch {
    $resp = $_.Exception.Response
    $code = if ($resp) { [int]$resp.StatusCode } else { 0 }
    $leaseConflict = [ordered]@{ conflict = ($code -eq 409 -or $code -eq 400); statusCode = $code; message = $_.Exception.Message }
}

$leaseConflict | ConvertTo-Json | Set-Content (Join-Path $OutputDir "lease-conflict.json") -Encoding UTF8

$sw.Stop()
@{ elapsedMs = $sw.ElapsedMilliseconds; runId = $runId; jobId = $jobId; usedDockerKill = ($UseDockerKill -and $midPartition); interruptionApplied = $interruptionApplied } |
    ConvertTo-Json | Set-Content (Join-Path $OutputDir "timing.json") -Encoding UTF8

$totalEmployees = if ($afterParts -and $afterParts.Count -gt 0) { [int]$afterParts[0].totalemployeecount } else { 0 }
$ok = $interruptionApplied -and ($afterLines.duplicateEmployeeGroups -eq 0) -and ($totalEmployees -gt 0) -and ($afterLines.totalLines -eq $totalEmployees)

$result = [ordered]@{
    ok = $ok
    runId = $runId
    totalEmployees = $totalEmployees
    salaryLines = $afterLines
    midPartitionObserved = $midPartition
    interruptionApplied = $interruptionApplied
    leaseConflict = $leaseConflict
}
$result | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDir "result.json") -Encoding UTF8

Write-Host ($result | ConvertTo-Json -Depth 6)
Cancel-TestRun $runId
if ($ok) {
    Write-Host "PASS: AC-22 payroll resume chaos"
    exit 0
}
exit 1
