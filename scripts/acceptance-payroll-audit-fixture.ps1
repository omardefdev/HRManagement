# AC-25 precondition: create a closed, dual-control payroll audit for a dynamically created run.
param(
    [Parameter(Mandatory = $true)][Guid]$RunId,
    [Parameter(Mandatory = $true)][Guid]$AuditorId,
    [Parameter(Mandatory = $true)][Guid]$ClosedBy,
    [Guid]$TenantId = "a0000001-0000-4000-8000-000000000001",
    [string]$PostgresContainer = "",
    [string]$PayrollDatabase = "payroll_db"
)

$ErrorActionPreference = "Stop"
if ($AuditorId -eq $ClosedBy) {
    throw "AuditorId and ClosedBy must be different for dual control."
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

$auditId = [Guid]::NewGuid()
$sql = @"
INSERT INTO "PayrollAuditRecords"
    ("Id", "PayrollRunId", "AuditorId", "ClosedBy", "Findings", "Status",
     "TenantId", "ClosedAt", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy")
SELECT
    '$($auditId.ToString("D"))', '$($RunId.ToString("D"))', '$($AuditorId.ToString("D"))',
    '$($ClosedBy.ToString("D"))', 'AC-25 synthetic acceptance review: no unresolved findings.', 1,
    '$($TenantId.ToString("D"))', NOW(), NOW(), NOW(),
    '$($AuditorId.ToString("D"))', '$($ClosedBy.ToString("D"))'
WHERE NOT EXISTS (
    SELECT 1 FROM "PayrollAuditRecords"
    WHERE "PayrollRunId" = '$($RunId.ToString("D"))' AND "Status" = 1
);
SELECT COUNT(*) FROM "PayrollAuditRecords"
WHERE "PayrollRunId" = '$($RunId.ToString("D"))' AND "Status" = 1;
"@

$raw = docker exec $PostgresContainer psql -U hrm_ci -d $PayrollDatabase -t -A -v ON_ERROR_STOP=1 -c $sql 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Failed to establish the AC-25 payroll audit precondition: $raw"
}
$count = [int](($raw | Select-Object -Last 1).ToString().Trim())
if ($count -ne 1) {
    throw "Expected one closed payroll audit for $RunId; found $count."
}

@{
    runId = $RunId
    auditId = $auditId
    auditorId = $AuditorId
    closedBy = $ClosedBy
    closedAuditCount = $count
} | ConvertTo-Json -Compress
