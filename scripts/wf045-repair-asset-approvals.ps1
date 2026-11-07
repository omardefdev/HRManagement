<#
.SYNOPSIS
  PLAN-45: finds custody and loss requests left PendingApproval with no workflow instance, and hands them to the retry job.
  Dry run by default. Connects as the PostgreSQL administrator, because the tables are under forced row security and a role with no
  tenant sees no row.

.PARAMETER Apply
  Turns PendingApproval with no instance into ApprovalStarting. AssetApprovalStartRetryJob picks each one up with its requester and tenant.

.PARAMETER RestartOnLatest
  Only when asked: instances still running on a retired definition version, at step 1 with no decision, are cancelled and their requests
  go back to ApprovalStarting, so they start again on the latest version. The old key is retired with the instance. Never on the demo
  server: running instances finish on their pinned version (PLAN-45's decision).

.PARAMETER Server
  Runs on the demo server through ssh hrm-vps. Without it, runs on the local acceptance stack.

.PARAMETER ReportPath
  Where the JSON report goes. Default: artifacts/plan45/wf045-repair-<utc>.json

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/wf045-repair-asset-approvals.ps1 -Server
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/wf045-repair-asset-approvals.ps1 -Server -Apply
#>
param(
    [switch]$Apply,
    [switch]$RestartOnLatest,
    [switch]$Server,
    [string]$PostgresContainer = 'hrm-acceptance-postgresql-1',
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'

function Invoke-Sql {
    param([string]$Database, [string]$Sql)

    $header = "\pset format unaligned`n\pset fieldsep '|'`n\pset tuples_only on`n"
    $text = $header + $Sql
    if ($Server) {
        $remote = 'docker exec -i hrm-postgresql sh -c ''psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d ' + $Database + ''''
        $lines = $text | ssh -o BatchMode=yes hrm-vps $remote
    }
    else {
        $remote = 'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d ' + $Database
        $lines = $text | docker exec -i $PostgresContainer sh -c $remote
    }

    if ($LASTEXITCODE -ne 0) {
        throw "wf045 repair: psql exited $LASTEXITCODE on $Database"
    }

    return @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function ConvertTo-Row {
    param([string]$Line, [string[]]$Names)

    $parts = $Line.Split('|')
    $row = [ordered]@{}
    for ($i = 0; $i -lt $Names.Count; $i++) {
        $row[$Names[$i]] = $parts[$i]
    }
    return [pscustomobject]$row
}

$tables = @(
    @{ Kind = 'AssetAssignment'; Table = 'AssetAssignments' },
    @{ Kind = 'AssetLossReport'; Table = 'AssetLossReports' }
)

$orphans = @()
$pending = @()
foreach ($t in $tables) {
    $table = $t.Table
    $rows = Invoke-Sql 'admin_db' "SELECT `"Id`", `"TenantId`", `"AssetId`", `"CreatedBy`" FROM `"$table`" WHERE `"Status`" = 0 AND `"WorkflowInstanceId`" IS NULL ORDER BY `"CreatedAt`";"
    foreach ($line in $rows) {
        $row = ConvertTo-Row $line @('Id', 'TenantId', 'AssetId', 'CreatedBy')
        $row | Add-Member -NotePropertyName Kind -NotePropertyValue $t.Kind
        $orphans += $row
    }

    $rows = Invoke-Sql 'admin_db' "SELECT `"Id`", `"TenantId`", `"WorkflowInstanceId`" FROM `"$table`" WHERE `"Status`" = 0 AND `"WorkflowInstanceId`" IS NOT NULL ORDER BY `"CreatedAt`";"
    foreach ($line in $rows) {
        $row = ConvertTo-Row $line @('Id', 'TenantId', 'InstanceId')
        $row | Add-Member -NotePropertyName Kind -NotePropertyValue $t.Kind
        $pending += $row
    }
}

$candidates = @()
if ($pending.Count -gt 0) {
    $ids = ($pending | ForEach-Object { "'" + $_.InstanceId + "'" }) -join ','
    $sql = @"
SELECT i."Id", i."DefinitionVersion", i."CurrentStepLevel", d."IsPublished",
       jsonb_array_length(COALESCE(NULLIF(btrim(i."StepHistoryJson"), ''), '[]')::jsonb)
FROM "WorkflowInstances" i
JOIN "WorkflowDefinitions" d ON d."Id" = i."DefinitionId" AND d."Version" = i."DefinitionVersion"
WHERE i."Id" IN ($ids) AND i."Status" = 'InProgress';
"@
    foreach ($line in (Invoke-Sql 'hr_core_db' $sql)) {
        $row = ConvertTo-Row $line @('InstanceId', 'Version', 'Step', 'Published', 'Decisions')
        if ($row.Step -eq '1' -and $row.Published -eq 'f' -and $row.Decisions -eq '0') {
            $request = $pending | Where-Object { $_.InstanceId -eq $row.InstanceId } | Select-Object -First 1
            $row | Add-Member -NotePropertyName RequestId -NotePropertyValue $request.Id
            $row | Add-Member -NotePropertyName Kind -NotePropertyValue $request.Kind
            $candidates += $row
        }
    }
}

$applied = 0
if ($Apply) {
    foreach ($t in $tables) {
        $table = $t.Table
        $changed = Invoke-Sql 'admin_db' "WITH changed AS (UPDATE `"$table`" SET `"Status`" = 4, `"UpdatedAt`" = now() WHERE `"Status`" = 0 AND `"WorkflowInstanceId`" IS NULL RETURNING 1) SELECT count(*) FROM changed;"
        $applied += [int](@($changed)[0])
    }
}

$restarted = 0
if ($RestartOnLatest) {
    foreach ($c in $candidates) {
        $table = if ($c.Kind -eq 'AssetLossReport') { 'AssetLossReports' } else { 'AssetAssignments' }
        $id = $c.InstanceId

        # HR.Core first: the key stays with the cancelled instance, so the restart makes a new one.
        $sql = @"
UPDATE "WorkflowInstances"
SET "Status" = 'Cancelled',
    "CompletedAt" = now(),
    "IdempotencyKey" = "IdempotencyKey" || ':superseded:' || "Id",
    "StepHistoryJson" = (COALESCE(NULLIF(btrim("StepHistoryJson"), ''), '[]')::jsonb || jsonb_build_array(jsonb_build_object(
        'Level', 1, 'StepName', '', 'Status', 4, 'ActorId', NULL, 'ActorName', 'wf045-repair',
        'Comments', 'PLAN-45: restarted on the latest version', 'ActionAt', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'))))::text
WHERE "Id" = '$id' AND "Status" = 'InProgress';
"@
        Invoke-Sql 'hr_core_db' $sql | Out-Null
        Invoke-Sql 'admin_db' "UPDATE `"$table`" SET `"Status`" = 4, `"WorkflowInstanceId`" = NULL, `"NeedsIntervention`" = false, `"UpdatedAt`" = now() WHERE `"WorkflowInstanceId`" = '$id' AND `"Status`" = 0;" | Out-Null
        $restarted++
    }
}

$report = [ordered]@{
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    target = $(if ($Server) { 'hrm-vps' } else { $PostgresContainer })
    mode = $(if ($Apply -or $RestartOnLatest) { 'applied' } else { 'dry-run' })
    pendingWithoutInstance = @($orphans)
    pendingWithInstance = $pending.Count
    onRetiredVersionAtStepOne = @($candidates)
    movedToApprovalStarting = $applied
    restartedOnLatest = $restarted
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $ReportPath = Join-Path $root "artifacts\plan45\wf045-repair-$stamp.json"
}

$directory = Split-Path -Parent $ReportPath
if (-not (Test-Path $directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
$report | ConvertTo-Json -Depth 5 | Out-File -Encoding utf8 $ReportPath

Write-Output "wf045 repair: $($report.mode) | pending without instance $($orphans.Count) | pending with instance $($pending.Count) | on a retired version at step 1 $($candidates.Count) | moved $applied | restarted $restarted"
Write-Output "report: $ReportPath"
