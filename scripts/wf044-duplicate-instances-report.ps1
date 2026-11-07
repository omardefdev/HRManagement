<#
.SYNOPSIS
  PLAN-44: prints duplicate running workflow instances, duplicate idempotency keys and finished instances whose entity is still pending.
  Read only. Connects as the PostgreSQL administrator, because every table here is under forced row security and a role with no tenant
  sees no row.

.PARAMETER Server
  Runs on the demo server through ssh hrm-vps. Without it, runs on the local acceptance stack.

.PARAMETER FailOnDuplicates
  Exits 1 when a duplicate exists: the check after a migration.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/wf044-duplicate-instances-report.ps1 -Server -FailOnDuplicates
#>
param(
    [switch]$Server,
    [switch]$FailOnDuplicates,
    [string]$PostgresContainer = 'hrm-acceptance-postgresql-1',
    [string]$Database = 'hr_core_db'
)

$ErrorActionPreference = 'Stop'

$sql = @'
\pset format unaligned
\pset fieldsep ' | '
\pset tuples_only on
SELECT 'owner', tableowner FROM pg_tables WHERE schemaname = 'public' AND tablename = 'WorkflowInstances';
SELECT 'row-security', relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = '"WorkflowInstances"'::regclass;
SELECT 'instances', "EntityType", "Status", count(*) FROM "WorkflowInstances" GROUP BY 2, 3 ORDER BY 2, 3;
SELECT 'duplicate-running', "TenantId", "EntityType", "EntityId", count(*), string_agg("Id"::text, ',' ORDER BY "StartedAt" DESC)
FROM "WorkflowInstances" WHERE "Status" = 'InProgress'
GROUP BY "TenantId", "EntityType", "EntityId" HAVING count(*) > 1;
SELECT 'duplicate-key', "TenantId", "SourceClientId", "IdempotencyKey", count(*)
FROM "WorkflowInstances" WHERE "IdempotencyKey" IS NOT NULL
GROUP BY "TenantId", "SourceClientId", "IdempotencyKey" HAVING count(*) > 1;
SELECT 'resolved-by-migration', "TenantId", "EntityType", "EntityId", "Id"
FROM "WorkflowInstances" WHERE "Status" = 'Cancelled' AND "StepHistoryJson" LIKE '%PLAN-44: %';
SELECT 'stuck', 'RecruitmentRequisition', e."Id", i."Id", i."Status" FROM "RecruitmentRequisitions" e
JOIN "WorkflowInstances" i ON i."Id" = e."WorkflowInstanceId"
WHERE e."Status" = 'PendingApproval' AND i."Status" IN ('Completed', 'Rejected');
SELECT 'stuck', 'OfferLetter', e."Id", i."Id", i."Status" FROM "OfferLetters" e
JOIN "WorkflowInstances" i ON i."Id" = e."WorkflowInstanceId"
WHERE e."Status" = 1 AND i."Status" IN ('Completed', 'Rejected');
SELECT 'stuck', 'EstablishmentChangeRequest', e."Id", i."Id", i."Status" FROM "EstablishmentChangeRequests" e
JOIN "WorkflowInstances" i ON i."Id" = e."WorkflowInstanceId"
WHERE e."Status" = 'PendingApproval' AND i."Status" IN ('Completed', 'Rejected');
SELECT 'stuck', 'SelectionCommitteeSession', e."Id", i."Id", i."Status" FROM "SelectionCommitteeSessions" e
JOIN "WorkflowInstances" i ON i."Id" = e."WorkflowInstanceId"
WHERE e."SubmittedAt" IS NOT NULL AND e."ApprovedAt" IS NULL AND i."Status" IN ('Completed', 'Rejected');
SELECT 'stuck', 'RecruitmentAppeal', e."Id", i."Id", i."Status" FROM "RecruitmentAppeals" e
JOIN "WorkflowInstances" i ON i."Id" = e."WorkflowInstanceId"
WHERE e."Status" = 1 AND i."Status" IN ('Completed', 'Rejected');
SELECT 'stuck', 'RecruitmentCompetition', e."Id", i."Id", i."Status" FROM "RecruitmentCompetitions" e
JOIN "WorkflowInstances" i ON i."Id" = e."ApprovalWorkflowInstanceId"
WHERE e."Status" = 'PendingApproval' AND i."Status" IN ('Completed', 'Rejected');
SELECT 'stuck', 'CompetitionResultVersion', e."Id", i."Id", i."Status" FROM "CompetitionResultVersions" e
JOIN "WorkflowInstances" i ON i."Id" = e."WorkflowInstanceId"
WHERE e."Status" = 'PendingApproval' AND i."Status" IN ('Completed', 'Rejected');
'@

if ($Server) {
    $remote = 'docker exec -i hrm-postgresql sh -c ''psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d ' + $Database + ''''
    $lines = $sql | ssh -o BatchMode=yes hrm-vps $remote
}
else {
    $remote = 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d ' + $Database
    $lines = $sql | docker exec -i $PostgresContainer sh -c $remote
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "wf044 report: psql exited $LASTEXITCODE"
    exit 2
}

$lines | ForEach-Object { Write-Output $_ }

$duplicates = @($lines | Where-Object { $_ -like 'duplicate-*' }).Count
$stuck = @($lines | Where-Object { $_ -like 'stuck *' }).Count
Write-Output "summary | duplicates $duplicates | stuck $stuck"

if ($FailOnDuplicates -and $duplicates -gt 0) {
    exit 1
}
