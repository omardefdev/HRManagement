<#
.SYNOPSIS
    HCM-028 (PLAN-28 A3): lists selection committee sessions that hold the same employee more than once. Read-only.

.DESCRIPTION
    SelectionCommitteeMembers has a unique index on (TenantId, SessionId, EmployeeId), so this should always
    report none; it is a sanity check before relying on one-row-per-member (votes, per-candidate conflicts).
    Nothing is changed or deleted — resolve any finding by hand.
    Exit code 0 = no duplicates, 2 = duplicates found.

.EXAMPLE
    pwsh scripts/hcm028-find-duplicate-committee-members.ps1
    pwsh scripts/hcm028-find-duplicate-committee-members.ps1 -PostgresContainer hrm-postgresql-1 -Database hr_core_db
#>
param(
    [string]$PostgresContainer = "",
    [string]$PostgresUser = "hrm_ci",
    [string]$Database = "hr_core_db"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    $PostgresContainer = docker ps `
        --filter "label=com.docker.compose.project=hrm-acceptance" `
        --filter "label=com.docker.compose.service=postgresql" `
        --format "{{.Names}}" | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    $PostgresContainer = docker ps `
        --filter "label=com.docker.compose.service=postgresql" `
        --format "{{.Names}}" | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    throw "No running PostgreSQL container found. Pass -PostgresContainer explicitly."
}

$sql = @"
SELECT m."TenantId",
       m."SessionId",
       m."EmployeeId",
       COUNT(*) AS "MemberRows",
       string_agg(m."Id"::text, ', ' ORDER BY m."CreatedAt") AS "MemberIds"
FROM "SelectionCommitteeMembers" m
GROUP BY m."TenantId", m."SessionId", m."EmployeeId"
HAVING COUNT(*) > 1
ORDER BY m."TenantId", m."SessionId";
"@

Write-Host "=== HCM-028 duplicate committee members ($PostgresContainer / $Database) ===" -ForegroundColor Cyan
$out = docker exec $PostgresContainer psql -X -v ON_ERROR_STOP=1 -U $PostgresUser -d $Database `
    -t -A -F "|" -c $sql 2>&1
if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }

$rows = @($out | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($rows.Count -eq 0) {
    Write-Host "No duplicate committee members." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($rows.Count) session/employee pair(s) with more than one member row:" -ForegroundColor Yellow
foreach ($row in $rows) {
    $f = $row -split "\|"
    Write-Host ("  tenant={0} session={1} employee={2} rows={3}: {4}" -f $f[0], $f[1], $f[2], $f[3], $f[4])
}
Write-Host "Remove the surplus rows manually (keep one per employee), then re-run this script." -ForegroundColor Yellow
exit 2
