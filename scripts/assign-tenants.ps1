<#
.SYNOPSIS
    TEN-003: Assign rows from the platform default tenant to real agency tenants.

.DESCRIPTION
    Discovers tenant ownership (OrgUnit root Code -> Tenant.Code, explicit rules,
    then quarantines remaining default-tenant rows). Idempotent: only updates rows
    still on the default tenant. Supports -DryRun.

.EXAMPLE
    .\scripts\assign-tenants.ps1 -DryRun
    .\scripts\assign-tenants.ps1 -PostgresHost localhost -ReportPath .\reports\assign.json
#>
param(
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432,
    [string]$PostgresUser = "postgres",
    [string]$PostgresPassword = $env:PGPASSWORD,
    [string]$HrCoreDatabase = "hr_core",
    [string]$ReportPath = ".\reports\tenant-assignment-$(Get-Date -Format 'yyyyMMdd-HHmmss').json",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$DefaultTenantId = "00000000-0000-4000-8000-000000000001"

$services = @(
    @{ Name = "HRCore"; Database = "hr_core"; Tables = @("Employees", "OrgUnits", "ApprovalRequests", "PerformanceEvaluations", "WorkforcePlans", "DailyAttendanceRecords", "RecruitmentApplications", "Shifts", "CareerPaths", "KeyPositions", "SuccessionPlans", "InternalVacancies", "PublicJobPostings", "CitizenApplications") },
    @{ Name = "HRContracts"; Database = "hr_contracts"; Tables = @("Contracts", "LeaveRequests", "SeparationRecords", "ContractTypes", "OrgUnitSnapshots", "ServiceTransactions") },
    @{ Name = "Payroll"; Database = "payroll"; Tables = @("PayrollRuns", "SalaryLines", "EmployeePayProfiles", "PayGrades", "PaymentBatches") },
    @{ Name = "HRMovements"; Database = "hr_movements"; Tables = @("TransferRequests", "MovementNotices") },
    @{ Name = "HRTraining"; Database = "hr_training"; Tables = @("TrainingCourses", "TrainingEnrollments") },
    @{ Name = "HRHealth"; Database = "hr_health"; Tables = @("MedicalRecords") },
    @{ Name = "AuthDirectory"; Database = "auth_directory"; Tables = @("AuthUsers", "Roles") },
    @{ Name = "FileStorage"; Database = "file_storage"; Tables = @("Files") },
    @{ Name = "IDCard"; Database = "idcard"; Tables = @("IssuedCards", "CardRequests", "CardDesigns") },
    @{ Name = "Compliance"; Database = "compliance"; Tables = @("Complaints", "AuditFindings", "DisciplinaryActions") },
    @{ Name = "RecordsArchive"; Database = "records_archive"; Tables = @("ArchiveItems", "LoanRecords") },
    @{ Name = "AdminLogistics"; Database = "admin_logistics"; Tables = @("Assets", "Vendors", "InventoryItems") },
    @{ Name = "ITInfra"; Database = "it_infra"; Tables = @("ServerDevices", "ITSupportTickets") },
    @{ Name = "DevAnalytics"; Database = "dev_analytics"; Tables = @("Reports", "Dashboards", "KPIs") }
)

function New-PgConnection([string]$Database) {
    $cs = "Host=$PostgresHost;Port=$PostgresPort;Username=$PostgresUser;Database=$Database"
    if ($PostgresPassword) { $cs += ";Password=$PostgresPassword" }
    return $cs
}

function Invoke-PgScalar([string]$Database, [string]$Sql) {
    $env:PGPASSWORD = $PostgresPassword
    $out = & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $Database -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed on $Database : $out" }
    return ($out | Out-String).Trim()
}

function Invoke-PgNonQuery([string]$Database, [string]$Sql) {
    if ($DryRun) {
        Write-Host "  [DRY-RUN] $Sql" -ForegroundColor Yellow
        return 0
    }
    $env:PGPASSWORD = $PostgresPassword
    $out = & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $Database -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed on $Database : $out" }
    return ($out | Out-String).Trim()
}

Write-Host "=== TEN-003 Tenant Assignment $(if ($DryRun) { '(DRY-RUN)' } else { '' }) ===" -ForegroundColor Cyan

# Discover real tenants (non-default)
$tenantRows = Invoke-PgScalar $HrCoreDatabase @"
SELECT "Id"::text || '|' || "Code"
FROM "Tenants"
WHERE "Id" <> '$DefaultTenantId' AND "IsActive" = TRUE
ORDER BY "Code";
"@

$tenants = @()
if ($tenantRows) {
    foreach ($line in ($tenantRows -split "`n" | Where-Object { $_.Trim() })) {
        $parts = $line.Trim() -split '\|', 2
        if ($parts.Length -eq 2) {
            $tenants += [pscustomobject]@{ Id = $parts[0]; Code = $parts[1] }
        }
    }
}

if ($tenants.Count -eq 0) {
    Write-Warning "No real tenants found in $HrCoreDatabase.Tenants (beyond PLATFORM). Seed agencies before assignment."
}

Write-Host ("Discovered tenants: {0}" -f (($tenants | ForEach-Object { $_.Code }) -join ', '))

$report = [ordered]@{
    GeneratedAt = (Get-Date).ToUniversalTime().ToString("o")
    DryRun = [bool]$DryRun
    DefaultTenantId = $DefaultTenantId
    Tenants = @($tenants)
    Services = @()
    QuarantineInserts = 0
}

# --- HRCore OrgUnit discovery: root OrgUnit.Code matching Tenant.Code ---
foreach ($t in $tenants) {
    $rootUpdate = @"
UPDATE "OrgUnits"
SET "TenantId" = '$($t.Id)'
WHERE "TenantId" = '$DefaultTenantId'
  AND "ParentId" IS NULL
  AND UPPER("Code") = UPPER('$($t.Code)');
"@
    Write-Host "Assigning root OrgUnits for $($t.Code)..."
    Invoke-PgNonQuery $HrCoreDatabase $rootUpdate | Out-Null

    # Children under roots already assigned to this tenant (by MaterializedPath prefix of matching roots)
    $childUpdate = @"
UPDATE "OrgUnits" child
SET "TenantId" = '$($t.Id)'
FROM "OrgUnits" root
WHERE child."TenantId" = '$DefaultTenantId'
  AND root."TenantId" = '$($t.Id)'
  AND root."ParentId" IS NULL
  AND UPPER(root."Code") = UPPER('$($t.Code)')
  AND (
    child."MaterializedPath" LIKE root."MaterializedPath" || '%'
    OR child."ParentId" = root."Id"
  );
"@
    Invoke-PgNonQuery $HrCoreDatabase $childUpdate | Out-Null

    # Employees by PrimaryOrgUnit
    $empUpdate = @"
UPDATE "Employees" e
SET "TenantId" = ou."TenantId"
FROM "OrgUnits" ou
WHERE e."TenantId" = '$DefaultTenantId'
  AND e."PrimaryOrgUnitId" = ou."Id"
  AND ou."TenantId" = '$($t.Id)';
"@
    Invoke-PgNonQuery $HrCoreDatabase $empUpdate | Out-Null
}

# Apply Explicit rules from TenantAssignmentRules
$rulesRaw = Invoke-PgScalar $HrCoreDatabase @"
SELECT "TargetTable" || '|' || COALESCE("TargetTenantId"::text,'') || '|' || COALESCE("SqlCondition",'') || '|' || "DiscoveryMethod"
FROM "TenantAssignmentRules"
WHERE "DiscoveryMethod" IN ('Explicit', 'OrgUnit')
  AND "TargetTenantId" IS NOT NULL;
"@

if ($rulesRaw) {
    foreach ($line in ($rulesRaw -split "`n" | Where-Object { $_.Trim() })) {
        $p = $line.Trim() -split '\|', 4
        $table = $p[0]; $tenantId = $p[1]; $cond = $p[2]; $method = $p[3]
        if (-not $tenantId) { continue }
        $whereExtra = if ($cond) { "AND ($cond)" } else { "" }
        # Apply against HRCore first; other DBs handled in service loop via matching table names
        $exists = Invoke-PgScalar $HrCoreDatabase "SELECT to_regclass('public.\"$table\"') IS NOT NULL;"
        if ($exists -eq "t") {
            $sql = @"
UPDATE "$table"
SET "TenantId" = '$tenantId'
WHERE "TenantId" = '$DefaultTenantId'
$whereExtra;
"@
            Write-Host "Applying rule ($method) on HRCore.$table -> $tenantId"
            Invoke-PgNonQuery $HrCoreDatabase $sql | Out-Null
        }
    }
}

# Per-service: count remaining default rows; quarantine them
foreach ($svc in $services) {
    $svcReport = [ordered]@{
        ServiceName = $svc.Name
        Database = $svc.Database
        Tables = @()
    }

    foreach ($table in $svc.Tables) {
        $exists = Invoke-PgScalar $svc.Database "SELECT to_regclass('public.\"$table\"') IS NOT NULL;"
        if ($exists -ne "t") {
            $svcReport.Tables += [ordered]@{ Table = $table; Skipped = $true; Reason = "table missing" }
            continue
        }

        $before = [int](Invoke-PgScalar $svc.Database "SELECT COUNT(*) FROM `"$table`";")
        $defaultCount = [int](Invoke-PgScalar $svc.Database "SELECT COUNT(*) FROM `"$table`" WHERE `"TenantId`" = '$DefaultTenantId';")
        $assigned = $before - $defaultCount

        # Quarantine remaining default-tenant rows
        $ids = Invoke-PgScalar $svc.Database @"
SELECT "Id"::text FROM "$table" WHERE "TenantId" = '$DefaultTenantId';
"@
        $quarantined = 0
        if ($ids) {
            foreach ($id in ($ids -split "`n" | Where-Object { $_.Trim() })) {
                $rid = $id.Trim()
                $ins = @"
INSERT INTO "TenantQuarantineEntries" ("Id", "ServiceName", "TableName", "RecordId", "Reason", "CreatedAt")
VALUES (gen_random_uuid(), '$($svc.Name)', '$table', '$rid', 'Unresolvable after OrgUnit/Explicit assignment; remains on PLATFORM default tenant', NOW())
ON CONFLICT ("ServiceName", "TableName", "RecordId") DO NOTHING;
"@
                # Quarantine lives in HRCore
                try {
                    Invoke-PgNonQuery $HrCoreDatabase $ins | Out-Null
                    $quarantined++
                    $report.QuarantineInserts++
                }
                catch {
                    Write-Warning "Quarantine insert failed for $($svc.Name).$table/$rid : $_"
                }
            }
        }

        $checksum = Invoke-PgScalar $svc.Database @"
SELECT COALESCE(SUM(('x' || substr(replace("Id"::text, '-', ''), 1, 16))::bit(64)::bigint), 0)
FROM "$table";
"@

        $svcReport.Tables += [ordered]@{
            Table = $table
            TotalRows = $before
            AssignedRows = $assigned
            RemainingDefault = $defaultCount
            Quarantined = $quarantined
            Checksum = $checksum
        }
        Write-Host ("  {0}.{1}: total={2} assigned={3} default={4} quarantined={5}" -f $svc.Name, $table, $before, $assigned, $defaultCount, $quarantined)
    }

    $report.Services += $svcReport
}

$dir = Split-Path -Parent $ReportPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
($report | ConvertTo-Json -Depth 8) | Set-Content -Path $ReportPath -Encoding UTF8
Write-Host "Report written to $ReportPath" -ForegroundColor Green
if ($DryRun) {
    Write-Host "Dry-run complete. Re-run without -DryRun to apply updates." -ForegroundColor Yellow
}
