<#
.SYNOPSIS
    AC-08 acceptance: seed PLATFORM orphan employee, quarantine via assign-tenants, capture reconcile report.

.EXAMPLE
    pwsh scripts/acceptance-quarantine-flow.ps1
#>
param(
    [string]$PostgresHost = $env:POSTGRES_HOST ?? "localhost",
    [int]$PostgresPort = [int]($env:POSTGRES_PORT ?? "6433"),
    [string]$PostgresUser = $env:POSTGRES_USER ?? "hrm_ci",
    [string]$PostgresPassword = $env:POSTGRES_PASSWORD ?? $env:PGPASSWORD,
    [string]$HrCoreDatabase = "hr_core_db",
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"
$DefaultTenantId = "00000000-0000-4000-8000-000000000001"
$EmployeeId = "a0000001-0000-4000-8000-000000000901"
$NationalId = "AC080000001"
$NationalIdHash = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($NationalId.Trim()))
).ToLowerInvariant()
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
        $env:ACCEPTANCE_EVIDENCE_DIR
    } else {
        Join-Path $root "artifacts/acceptance/evidence"
    }
    $ReportPath = Join-Path $evidenceRoot "AC-08/quarantine-flow.json"
}
if ([string]::IsNullOrWhiteSpace($PostgresPassword)) {
    throw "POSTGRES_PASSWORD is required for the isolated acceptance database."
}

function Invoke-PgScalar([string]$Sql) {
    $env:PGPASSWORD = $PostgresPassword
    $out = & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $HrCoreDatabase -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
    return ($out | Out-String).Trim()
}

function Invoke-PgNonQuery([string]$Sql) {
    $env:PGPASSWORD = $PostgresPassword
    $out = & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $HrCoreDatabase -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
}

Write-Host "=== AC-08 Quarantine acceptance flow ===" -ForegroundColor Cyan

# 1. Seed orphan employee on PLATFORM tenant (no org match)
$exists = Invoke-PgScalar "SELECT COUNT(*) FROM `"Employees`" WHERE `"Id`" = '$EmployeeId';"
if ([int]$exists -eq 0) {
    Invoke-PgNonQuery @"
INSERT INTO "Employees" (
    "Id", "TenantId", "NationalId", "NationalIdHash", "FirstName", "LastName", "FullName",
    "BirthDate", "Gender", "MaritalStatus", "CurrentStatus", "Cadre", "WorkScheduleMode",
    "CreatedAt", "UpdatedAt"
) VALUES (
    '$EmployeeId', '$DefaultTenantId', '$NationalId', '$NationalIdHash', 'orphan', 'acceptance', 'orphan acceptance',
    '1990-01-01T00:00:00Z', 0, 0, 0, 0, 0, NOW(), NOW()
);
"@ | Out-Null
    Write-Host "Seeded orphan employee $EmployeeId on PLATFORM tenant."
} else {
    Invoke-PgNonQuery "UPDATE `"Employees`" SET `"TenantId`" = '$DefaultTenantId', `"PrimaryOrgUnitId`" = NULL, `"NationalIdHash`" = '$NationalIdHash' WHERE `"Id`" = '$EmployeeId';" | Out-Null
    Write-Host "Reset orphan employee $EmployeeId to PLATFORM tenant."
}

# Clear prior quarantine for idempotent runs
Invoke-PgNonQuery @"
DELETE FROM "TenantQuarantineEntries"
WHERE "ServiceName" = 'HRCore' AND "TableName" = 'Employees' AND "RecordId" = '$EmployeeId';
"@ | Out-Null

# 2. assign-tenants (dry-run then live)
$assignScript = Join-Path $PSScriptRoot "assign-tenants.ps1"
& $assignScript -PostgresHost $PostgresHost -PostgresPort $PostgresPort -PostgresUser $PostgresUser -PostgresPassword $PostgresPassword -DryRun | Out-Null
& $assignScript -PostgresHost $PostgresHost -PostgresPort $PostgresPort -PostgresUser $PostgresUser -PostgresPassword $PostgresPassword | Out-Null

# 3. reconcile report
$reconcileScript = Join-Path $PSScriptRoot "reconcile-tenants.ps1"
$reconcileBefore = Join-Path (Split-Path $ReportPath) "reconcile-before.json"
& $reconcileScript -PostgresHost $PostgresHost -PostgresPort $PostgresPort -PostgresUser $PostgresUser -PostgresPassword $PostgresPassword -ReportPath $reconcileBefore | Out-Null

$entryId = Invoke-PgScalar @"
SELECT "Id"::text FROM "TenantQuarantineEntries"
WHERE "ServiceName" = 'HRCore' AND "TableName" = 'Employees' AND "RecordId" = '$EmployeeId' AND "ResolvedAt" IS NULL
LIMIT 1;
"@

if (-not $entryId) {
    Write-Warning "No open quarantine entry for employee $EmployeeId — agencies may be unseeded."
    exit 2
}

$openCount = Invoke-PgScalar 'SELECT COUNT(*) FROM "TenantQuarantineEntries" WHERE "ResolvedAt" IS NULL;'

$report = [ordered]@{
    GeneratedAt = (Get-Date).ToUniversalTime().ToString("o")
    EmployeeId = $EmployeeId
    QuarantineEntryId = $entryId
    OpenQuarantineCount = [int]$openCount
    ReconcileBefore = $reconcileBefore
}

$dir = Split-Path -Parent $ReportPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
($report | ConvertTo-Json -Depth 6) | Set-Content -Path $ReportPath -Encoding UTF8

Write-Output ($report | ConvertTo-Json -Compress -Depth 6)
exit 0
