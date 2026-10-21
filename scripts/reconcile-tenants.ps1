<#
.SYNOPSIS
    TEN-003: Before/after tenant reconciliation across service databases.

.DESCRIPTION
    Counts rows per tenant, computes checksums, finds orphans still on the
    default PLATFORM tenant, and detects child rows whose TenantId differs
    from a linked parent (sample FK pairs). Outputs JSON report.

.EXAMPLE
    .\scripts\reconcile-tenants.ps1 -ReportPath .\reports\reconcile.json
#>
param(
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432,
    [string]$PostgresUser = "postgres",
    [string]$PostgresPassword = $env:PGPASSWORD,
    [string]$HrCoreDatabase = "hr_core_db",
    [string]$ReportPath = ".\reports\tenant-reconcile-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
)

$ErrorActionPreference = "Stop"
$DefaultTenantId = "00000000-0000-4000-8000-000000000001"

$services = @(
    @{ Name = "HRCore"; Database = "hr_core_db"; Tables = @("Employees", "OrgUnits", "ApprovalRequests") },
    @{ Name = "HRContracts"; Database = "hr_contracts_db"; Tables = @("Contracts", "LeaveRequests", "SeparationRecords", "OrgUnitSnapshots") },
    @{ Name = "Payroll"; Database = "payroll_db"; Tables = @("PayrollRuns", "SalaryLines", "EmployeePayProfiles") },
    @{ Name = "HRMovements"; Database = "hr_movements_db"; Tables = @("TransferRequests", "MovementNotices") },
    @{ Name = "HRTraining"; Database = "hr_training_db"; Tables = @("TrainingCourses", "TrainingEnrollments") },
    @{ Name = "HRHealth"; Database = "hr_health_db"; Tables = @("MedicalRecords") },
    @{ Name = "AuthDirectory"; Database = "auth_directory_db"; Tables = @("AuthUsers", "Roles") },
    @{ Name = "FileStorage"; Database = "file_storage_db"; Tables = @("Files") },
    @{ Name = "IDCard"; Database = "idcard_db"; Tables = @("IssuedCards", "CardRequests") },
    @{ Name = "Compliance"; Database = "compliance_db"; Tables = @("Complaints", "AuditFindings") },
    @{ Name = "RecordsArchive"; Database = "archive_db"; Tables = @("ArchiveItems") },
    @{ Name = "AdminLogistics"; Database = "admin_db"; Tables = @("Assets") },
    @{ Name = "ITInfra"; Database = "it_infra_db"; Tables = @("ServerDevices") },
    @{ Name = "DevAnalytics"; Database = "analytics_db"; Tables = @("Reports", "KPIs", "ReadModels") }
)

# Sample parent/child pairs for cross-tenant FK orphan detection
$parentChildChecks = @(
    @{ Database = "hr_core_db"; Child = "Employees"; ChildFk = "PrimaryOrgUnitId"; Parent = "OrgUnits"; ParentPk = "Id" },
    @{ Database = "payroll_db"; Child = "SalaryLines"; ChildFk = "PayrollRunId"; Parent = "PayrollRuns"; ParentPk = "Id" },
    @{ Database = "hr_contracts_db"; Child = "LeaveRequests"; ChildFk = "EmployeeId"; Parent = $null; ParentPk = $null }
)

function Invoke-PgScalar([string]$Database, [string]$Sql) {
    $env:PGPASSWORD = $PostgresPassword
    $out = & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $Database -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed on $Database : $out" }
    return ($out | Out-String).Trim()
}

function Test-PgTableExists([string]$Database, [string]$Table) {
    $sql = 'SELECT to_regclass(''public."{0}"'') IS NOT NULL;' -f $Table
    return (Invoke-PgScalar $Database $sql) -eq "t"
}

Write-Host "=== TEN-003 Tenant Reconciliation ===" -ForegroundColor Cyan

$generatedAt = (Get-Date).ToUniversalTime()
$reports = @()
$summary = [ordered]@{
    GeneratedAt = $generatedAt.ToString("o")
    DefaultTenantId = $DefaultTenantId
    Services = @()
    CrossTenantFkOrphans = @()
    QuarantineOpenCount = 0
    Totals = [ordered]@{ TotalRows = 0; AssignedRows = 0; OrphanRows = 0; QuarantinedRows = 0 }
}

$qOpen = Invoke-PgScalar $HrCoreDatabase @"
SELECT COUNT(*) FROM "TenantQuarantineEntries" WHERE "ResolvedAt" IS NULL;
"@
$summary.QuarantineOpenCount = [int]$qOpen
$summary.Totals.QuarantinedRows = [int]$qOpen

foreach ($svc in $services) {
    $svcBlock = [ordered]@{
        ServiceName = $svc.Name
        Database = $svc.Database
        Tables = @()
    }

    foreach ($table in $svc.Tables) {
        if (-not (Test-PgTableExists $svc.Database $table)) {
            Write-Warning "$($svc.Name).$table missing — skipped"
            continue
        }

        $total = [int](Invoke-PgScalar $svc.Database "SELECT COUNT(*) FROM `"$table`";")
        $orphans = [int](Invoke-PgScalar $svc.Database "SELECT COUNT(*) FROM `"$table`" WHERE `"TenantId`" = '$DefaultTenantId';")
        $assigned = $total - $orphans

        $perTenant = Invoke-PgScalar $svc.Database @"
SELECT "TenantId"::text || '=' || COUNT(*)::text
FROM "$table"
GROUP BY "TenantId"
ORDER BY COUNT(*) DESC;
"@

        $checksum = Invoke-PgScalar $svc.Database @"
SELECT COALESCE(SUM(('x' || substr(replace("Id"::text, '-', ''), 1, 16))::bit(64)::bigint), 0)::text
     || '|' || COUNT(*)::text
     || '|' || COUNT(DISTINCT "TenantId")::text
FROM "$table";
"@

        $entry = [ordered]@{
            ServiceName = $svc.Name
            TableName = $table
            TotalRows = $total
            AssignedRows = $assigned
            QuarantinedRows = 0
            OrphanRows = $orphans
            PerTenantCounts = @($perTenant -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() })
            Checksum = $checksum
            GeneratedAt = $generatedAt.ToString("o")
        }

        # Quarantine count for this table
        $qTable = Invoke-PgScalar $HrCoreDatabase @"
SELECT COUNT(*) FROM "TenantQuarantineEntries"
WHERE "ServiceName" = '$($svc.Name)' AND "TableName" = '$table' AND "ResolvedAt" IS NULL;
"@
        $entry.QuarantinedRows = [int]$qTable

        $svcBlock.Tables += $entry
        $reports += $entry

        $summary.Totals.TotalRows += $total
        $summary.Totals.AssignedRows += $assigned
        $summary.Totals.OrphanRows += $orphans

        Write-Host ("  {0}.{1}: total={2} assigned={3} orphan(default)={4} quarantine={5}" -f `
            $svc.Name, $table, $total, $assigned, $orphans, $entry.QuarantinedRows)
    }

    $summary.Services += $svcBlock
}

foreach ($pair in $parentChildChecks) {
    if (-not $pair.Parent) { continue }
    $childExists = Test-PgTableExists $pair.Database $pair.Child
    $parentExists = Test-PgTableExists $pair.Database $pair.Parent
    if (-not $childExists -or -not $parentExists) { continue }

    $mismatch = Invoke-PgScalar $pair.Database @"
SELECT COUNT(*)
FROM "$($pair.Child)" c
JOIN "$($pair.Parent)" p ON c."$($pair.ChildFk)" = p."$($pair.ParentPk)"
WHERE c."TenantId" <> p."TenantId";
"@
    $count = [int]$mismatch
    $summary.CrossTenantFkOrphans += [ordered]@{
        Database = $pair.Database
        Child = $pair.Child
        Parent = $pair.Parent
        MismatchCount = $count
    }
    if ($count -gt 0) {
        Write-Warning "Cross-tenant FK orphans: $($pair.Database).$($pair.Child) -> $($pair.Parent) = $count"
    }
}

$payload = [ordered]@{
    Summary = $summary
    Reports = $reports
}

$dir = Split-Path -Parent $ReportPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
($payload | ConvertTo-Json -Depth 10) | Set-Content -Path $ReportPath -Encoding UTF8
Write-Host "Reconciliation report: $ReportPath" -ForegroundColor Green

if ($summary.Totals.OrphanRows -gt 0 -and $summary.QuarantineOpenCount -lt $summary.Totals.OrphanRows) {
    Write-Warning "Some default-tenant rows are not yet quarantined. Run assign-tenants.ps1."
    exit 2
}

Write-Host "Reconciliation OK (orphans quarantined or assigned)." -ForegroundColor Green
exit 0
