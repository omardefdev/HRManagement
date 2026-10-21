# PLAN-13 AC-34: Docker-native backup and restore drill for the isolated acceptance stack.
# Usage: pwsh scripts/acceptance-backup-restore.ps1 [-OutputDir path] [-BackupRoot path]

param(
    [string]$OutputDir = "",
    [string]$BackupRoot = "",
    [string]$PostgresContainer = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root "artifacts\acceptance\evidence\AC-34" }
if (-not $BackupRoot) { $BackupRoot = Join-Path $root "artifacts\acceptance\backups\postgres" }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null

$logPath = Join-Path $OutputDir "restore-log.txt"
Set-Content -LiteralPath $logPath -Value "AC-34 backup/restore drill" -Encoding UTF8

function Write-DrillLog([string]$Message) {
    $line = "[$((Get-Date).ToUniversalTime().ToString('o'))] $Message"
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $Message
}

function Invoke-Docker {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & docker @Arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "docker $($Arguments -join ' ') failed ($exitCode): $output"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $output.Trim() }
}

function Resolve-AcceptanceContainer([string]$Service, [string]$Override = "") {
    if (-not [string]::IsNullOrWhiteSpace($Override)) { return $Override }
    $result = Invoke-Docker -Arguments @(
        "ps",
        "--filter", "label=com.docker.compose.project=hrm-acceptance",
        "--filter", "label=com.docker.compose.service=$Service",
        "--format", "{{.Names}}")
    $container = @($result.Output -split "`r?`n" | Where-Object { $_ }) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($container)) {
        throw "The hrm-acceptance container for '$Service' is not running."
    }
    return $container
}

function Invoke-Psql([string]$Database, [string]$Sql) {
    $result = Invoke-Docker -Arguments @(
        "exec", $PostgresContainer,
        "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "hrm_ci", "-d", $Database,
        "-t", "-A", "-c", $Sql)
    return $result.Output.Trim()
}

function Assert-SafeDrillDatabase([string]$Database) {
    if ($Database -notmatch '^ac34_[a-z]+_[0-9]{17}$') {
        throw "Refusing to manage unexpected drill database name '$Database'."
    }
}

function Drop-DrillDatabase([string]$Database) {
    Assert-SafeDrillDatabase $Database
    Invoke-Psql "postgres" "DROP DATABASE IF EXISTS `"$Database`" WITH (FORCE);" | Out-Null
}

$databases = @(
    "keycloak_db",
    "gateway_audit_db",
    "hr_core_db",
    "hr_contracts_db",
    "hr_health_db",
    "hr_movements_db",
    "hr_training_db",
    "payroll_db",
    "idcard_db",
    "archive_db",
    "admin_db",
    "it_infra_db",
    "analytics_db",
    "compliance_db",
    "auth_directory_db",
    "file_storage_db"
)

$critical = @(
    [ordered]@{ database = "hr_core_db"; table = "Employees"; minRows = 1; prefix = "hr" },
    [ordered]@{ database = "payroll_db"; table = "SalaryLines"; minRows = 1; prefix = "pay" },
    [ordered]@{ database = "admin_db"; table = "Assets"; minRows = 1; prefix = "admin" },
    [ordered]@{ database = "gateway_audit_db"; table = "gateway_audit_entries"; minRows = 0; prefix = "audit" },
    [ordered]@{ database = "file_storage_db"; table = "Files"; minRows = 1; prefix = "files" }
)

$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmssfff")
$backupPath = Join-Path $BackupRoot $stamp
$remoteRoot = "/tmp/ac34_$stamp"
$drillDatabases = [System.Collections.Generic.List[string]]::new()
$backupManifest = [System.Collections.Generic.List[object]]::new()
$restoreResults = [System.Collections.Generic.List[object]]::new()
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$exitCode = 0
$failure = $null
$relationshipChecks = [ordered]@{
    payrollEmployeeReferencesChecked = 0
    payrollOrphanEmployeeReferences = 0
    custodyEmployeeReferencesChecked = 0
    custodyOrphanEmployeeReferences = 0
    pass = $false
}

try {
    $PostgresContainer = Resolve-AcceptanceContainer "postgresql" $PostgresContainer
    New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
    Invoke-Docker -Arguments @("exec", $PostgresContainer, "mkdir", "-p", $remoteRoot) | Out-Null
    Write-DrillLog "Backing up $($databases.Count) acceptance databases from $PostgresContainer."

    foreach ($database in $databases) {
        $remoteDump = "$remoteRoot/$database.dump"
        $dumpWatch = [System.Diagnostics.Stopwatch]::StartNew()
        Invoke-Docker -Arguments @(
            "exec", $PostgresContainer,
            "pg_dump", "-U", "hrm_ci", "-Fc", "-f", $remoteDump, $database) | Out-Null
        $localDump = Join-Path $backupPath "$database.dump"
        Invoke-Docker -Arguments @("cp", "${PostgresContainer}:$remoteDump", $localDump) | Out-Null
        $dumpWatch.Stop()
        $file = Get-Item -LiteralPath $localDump
        $hash = (Get-FileHash -LiteralPath $localDump -Algorithm SHA256).Hash.ToLowerInvariant()
        $backupManifest.Add([ordered]@{
            database = $database
            file = $file.Name
            bytes = $file.Length
            sha256 = $hash
            backupMilliseconds = $dumpWatch.ElapsedMilliseconds
        })
        Write-DrillLog "Backup verified: $database ($($file.Length) bytes, SHA-256 $hash)."
    }

    [ordered]@{
        schema = "hrm-acceptance-backup-v1"
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        sourceContainer = $PostgresContainer
        databaseCount = $databases.Count
        databases = $backupManifest
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backupPath "manifest.json") -Encoding UTF8

    Write-DrillLog "Restoring the five critical databases into isolated drill databases."
    $restoredBySource = @{}
    foreach ($item in $critical) {
        $drillDatabase = "ac34_$($item.prefix)_$stamp"
        Assert-SafeDrillDatabase $drillDatabase
        $drillDatabases.Add($drillDatabase)
        Drop-DrillDatabase $drillDatabase
        Invoke-Psql "postgres" "CREATE DATABASE `"$drillDatabase`";" | Out-Null

        $restoreWatch = [System.Diagnostics.Stopwatch]::StartNew()
        Invoke-Docker -Arguments @(
            "exec", $PostgresContainer,
            "pg_restore", "--no-owner", "--no-privileges",
            "-U", "hrm_ci", "-d", $drillDatabase,
            "$remoteRoot/$($item.database).dump") | Out-Null
        $restoreWatch.Stop()

        $quotedTable = if ($item.table -eq "gateway_audit_entries") {
            "gateway_audit_entries"
        } else {
            "`"$($item.table)`""
        }
        $tableExists = Invoke-Psql $drillDatabase "SELECT to_regclass('public.$quotedTable') IS NOT NULL;"
        if ($tableExists -ne "t") { throw "Expected table $($item.table) is missing after restoring $($item.database)." }
        $rowCount = [long](Invoke-Psql $drillDatabase "SELECT COUNT(*) FROM $quotedTable;")
        if ($rowCount -lt [long]$item.minRows) {
            throw "Restored $($item.database).$($item.table) has $rowCount rows; expected at least $($item.minRows)."
        }

        $restoredBySource[$item.database] = $drillDatabase
        $restoreResults.Add([ordered]@{
            sourceDatabase = $item.database
            drillDatabase = $drillDatabase
            expectedTable = $item.table
            rowCount = $rowCount
            restoreMilliseconds = $restoreWatch.ElapsedMilliseconds
        })
        Write-DrillLog "Restore verified: $($item.database) -> $drillDatabase; $($item.table) rows=$rowCount."
    }

    $employeeIds = @((Invoke-Psql $restoredBySource["hr_core_db"] 'SELECT "Id"::text FROM "Employees";') -split "`r?`n" | Where-Object { $_ })
    $employeeSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $employeeIds) { [void]$employeeSet.Add($id.Trim()) }

    $payrollEmployeeIds = @((Invoke-Psql $restoredBySource["payroll_db"] 'SELECT DISTINCT "EmployeeId"::text FROM "SalaryLines" WHERE "EmployeeId" IS NOT NULL;') -split "`r?`n" | Where-Object { $_ })
    $payrollOrphans = @($payrollEmployeeIds | Where-Object { -not $employeeSet.Contains($_.Trim()) })
    $custodyEmployeeIds = @((Invoke-Psql $restoredBySource["admin_db"] 'SELECT DISTINCT "EmployeeId"::text FROM "AssetAssignments" WHERE "EmployeeId" IS NOT NULL;') -split "`r?`n" | Where-Object { $_ })
    $custodyOrphans = @($custodyEmployeeIds | Where-Object { -not $employeeSet.Contains($_.Trim()) })

    $relationshipChecks.payrollEmployeeReferencesChecked = $payrollEmployeeIds.Count
    $relationshipChecks.payrollOrphanEmployeeReferences = $payrollOrphans.Count
    $relationshipChecks.custodyEmployeeReferencesChecked = $custodyEmployeeIds.Count
    $relationshipChecks.custodyOrphanEmployeeReferences = $custodyOrphans.Count
    $relationshipChecks.pass = $payrollOrphans.Count -eq 0 -and $custodyOrphans.Count -eq 0
    if (-not $relationshipChecks.pass) {
        throw "Cross-database relationship check failed: payroll orphans=$($payrollOrphans.Count), custody orphans=$($custodyOrphans.Count)."
    }
    Write-DrillLog "Cross-database references verified: payroll=$($payrollEmployeeIds.Count), custody=$($custodyEmployeeIds.Count), orphans=0."
}
catch {
    $exitCode = 1
    $failure = $_.Exception.Message
    Write-DrillLog "FAIL: $failure"
}
finally {
    foreach ($database in $drillDatabases) {
        try { Drop-DrillDatabase $database } catch { Write-DrillLog "Cleanup warning for ${database}: $($_.Exception.Message)" }
    }
    if ($PostgresContainer) {
        [void](Invoke-Docker -Arguments @("exec", $PostgresContainer, "find", $remoteRoot, "-type", "f", "-name", "*.dump", "-delete") -AllowFailure)
        [void](Invoke-Docker -Arguments @("exec", $PostgresContainer, "rmdir", $remoteRoot) -AllowFailure)
    }
    $stopwatch.Stop()
}

$relationshipChecks | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDir "relationship-checks.json") -Encoding UTF8
[ordered]@{
    totalMilliseconds = $stopwatch.ElapsedMilliseconds
    backups = @($backupManifest | ForEach-Object { [ordered]@{ database = $_.database; milliseconds = $_.backupMilliseconds } })
    restores = @($restoreResults | ForEach-Object { [ordered]@{ database = $_.sourceDatabase; milliseconds = $_.restoreMilliseconds } })
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDir "timing.json") -Encoding UTF8

$result = [ordered]@{
    ok = $exitCode -eq 0
    backupPath = $backupPath
    databaseCount = $backupManifest.Count
    restoredCriticalCount = $restoreResults.Count
    relationshipChecks = $relationshipChecks
    failure = $failure
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDir "drill-result.json") -Encoding UTF8
Write-Host ($result | ConvertTo-Json -Depth 6)
exit $exitCode
