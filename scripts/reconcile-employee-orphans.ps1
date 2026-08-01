<#
.SYNOPSIS
    DATA-002: Detect EmployeeId orphans across Payroll, Contracts, and Movements vs HR.Core.

.DESCRIPTION
    Read-only: lists EmployeeId values present in satellite DBs but missing from hr_core."Employees".
    Outputs JSON for review. Does NOT delete or purge rows.

.EXAMPLE
    .\scripts\reconcile-employee-orphans.ps1 -ReportPath .\reports\employee-orphans.json
#>
param(
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432,
    [string]$PostgresUser = "postgres",
    [string]$PostgresPassword = $env:PGPASSWORD,
    [string]$HrCoreDatabase = "hr_core",
    [string]$PayrollDatabase = "payroll",
    [string]$ContractsDatabase = "hr_contracts",
    [string]$MovementsDatabase = "hr_movements",
    [string]$ReportPath = ".\reports\employee-orphans-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
)

$ErrorActionPreference = "Stop"

function Invoke-PgQuery([string]$Database, [string]$Sql) {
    $env:PGPASSWORD = $PostgresPassword
    $out = & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $Database -t -A -F "|" -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed on $Database : $out" }
    return @($out | Where-Object { $_ -and $_.ToString().Trim().Length -gt 0 })
}

Write-Host "=== DATA-002 Employee Orphan Reconciliation (dry-run) ===" -ForegroundColor Cyan

$checks = @(
    @{ Service = "Payroll"; Database = $PayrollDatabase; Table = "EmployeePayProfiles"; Column = "EmployeeId" },
    @{ Service = "Payroll"; Database = $PayrollDatabase; Table = "SalaryLines"; Column = "EmployeeId" },
    @{ Service = "HRContracts"; Database = $ContractsDatabase; Table = "Contracts"; Column = "EmployeeId" },
    @{ Service = "HRContracts"; Database = $ContractsDatabase; Table = "LeaveRequests"; Column = "EmployeeId" },
    @{ Service = "HRContracts"; Database = $ContractsDatabase; Table = "SeparationRecords"; Column = "EmployeeId" },
    @{ Service = "HRMovements"; Database = $MovementsDatabase; Table = "TransferRequests"; Column = "EmployeeId" },
    @{ Service = "HRMovements"; Database = $MovementsDatabase; Table = "MovementNotices"; Column = "EmployeeId" }
)

$coreIds = Invoke-PgQuery $HrCoreDatabase 'SELECT "Id"::text FROM "Employees";'
$coreSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$coreIds)

$orphanGroups = @()
$totalOrphans = 0

foreach ($c in $checks) {
    $sql = @"
SELECT DISTINCT "$($c.Column)"::text
FROM "$($c.Table)"
WHERE "$($c.Column)" IS NOT NULL;
"@
    try {
        $ids = Invoke-PgQuery $c.Database $sql
    }
    catch {
        Write-Warning "Skip $($c.Service).$($c.Table): $_"
        continue
    }

    $orphans = @($ids | Where-Object { -not $coreSet.Contains($_) })
    $totalOrphans += $orphans.Count
    $orphanGroups += [ordered]@{
        Service = $c.Service
        Database = $c.Database
        Table = $c.Table
        Column = $c.Column
        DistinctEmployeeIds = $ids.Count
        OrphanCount = $orphans.Count
        OrphanEmployeeIds = $orphans
    }
    Write-Host ("{0}.{1}: {2} orphans / {3} distinct" -f $c.Service, $c.Table, $orphans.Count, $ids.Count)
}

$report = [ordered]@{
    GeneratedAt = (Get-Date).ToUniversalTime().ToString("o")
    Mode = "dry-run"
    HrCoreEmployeeCount = $coreSet.Count
    TotalOrphanReferences = $totalOrphans
    Checks = $orphanGroups
}

$dir = Split-Path -Parent $ReportPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
($report | ConvertTo-Json -Depth 6) | Set-Content -Path $ReportPath -Encoding UTF8
Write-Host "Report written: $ReportPath" -ForegroundColor Green
exit 0
