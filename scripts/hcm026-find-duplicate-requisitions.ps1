<#
.SYNOPSIS
    HCM-026 (PLAN-26 D3): lists funded positions that carry more than one blocking requisition
    (Draft / PendingApproval / Approved / Published) per tenant. Read-only.

.DESCRIPTION
    Run this before applying migration HCM026_RequisitionIntegrity: the migration refuses to create the
    filtered unique index IX_RecruitmentRequisitions_Tenant_Position_Blocking while duplicates exist.
    Resolve each duplicate by hand (cancel or close the surplus requisition in the UI), then migrate.
    Exit code 0 = no duplicates, 2 = duplicates found.

.EXAMPLE
    pwsh scripts/hcm026-find-duplicate-requisitions.ps1
    pwsh scripts/hcm026-find-duplicate-requisitions.ps1 -PostgresContainer hrm-postgresql-1 -Database hr_core_db
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
SELECT r."TenantId",
       r."FundedPositionId",
       COALESCE(p."PositionCode", '?') AS "PositionCode",
       COUNT(*) AS "BlockingCount",
       string_agg(r."RequisitionNumber" || ':' || r."Status", ', ' ORDER BY r."RequestedDate") AS "Requisitions"
FROM "RecruitmentRequisitions" r
LEFT JOIN "FundedPositions" p ON p."Id" = r."FundedPositionId"
WHERE r."Status" IN ('Draft','PendingApproval','Approved','Published')
GROUP BY r."TenantId", r."FundedPositionId", p."PositionCode"
HAVING COUNT(*) > 1
ORDER BY r."TenantId", "PositionCode";
"@

Write-Host "=== HCM-026 duplicate blocking requisitions ($PostgresContainer / $Database) ===" -ForegroundColor Cyan
$out = docker exec $PostgresContainer psql -X -v ON_ERROR_STOP=1 -U $PostgresUser -d $Database `
    -t -A -F "|" -c $sql 2>&1
if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }

$rows = @($out | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($rows.Count -eq 0) {
    Write-Host "No duplicates — HCM026_RequisitionIntegrity can be applied." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($rows.Count) funded position(s) with more than one blocking requisition:" -ForegroundColor Yellow
foreach ($row in $rows) {
    $f = $row -split "\|"
    Write-Host ("  tenant={0} position={1} ({2}) count={3}: {4}" -f $f[0], $f[2], $f[1], $f[3], $f[4])
}
Write-Host "Resolve them manually (cancel/close the surplus requisition), then re-run this script." -ForegroundColor Yellow
exit 2
