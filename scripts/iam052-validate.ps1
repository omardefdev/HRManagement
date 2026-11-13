# PLAN-52 (IAM-D): static checks of the branch scope mechanism. ASCII only; Windows PowerShell 5.1.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/iam052-validate.ps1 [-SelfTest]
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/iam052-validate.ps1 -Counts [-Project hrm-acceptance] [-SshHost hrm-vps -Project hrm]
#
# What it checks (OrgScopeCoverageTests, Iam052* and AC-IAM-52 prove the behaviour; this script guards the files a reviewer or an
# operator edits by hand):
#   1. OrgScopeRequirement, its handler and AddOrgScopeAuthorization are gone from the code.
#   2. The registry, the attributes and the filter exist, and HR.Core, HR.Contracts and Admin.Logistics each register the filter with
#      their own keys' Configure.
#   3. Every org-scope list row is signed "IAM-H:" with a reason, and the three services have no list.
#   4. The migration HCM052_OrgScopeKeys is present, with no byte order mark, and lifts and restores FORCE.
#   5. The two transfer consumers exist: Auth.Directory clears the scope (and asks Keycloak when the sync is on), HR.Health moves the
#      employee snapshot; both keep a receipt.
#   6. AC-IAM-52 is a required prod-like class of the main group, owned by PLAN-52.
# -SelfTest runs each check on a planted defect and fails unless every one of them is refused.
# -Counts reads, read-only, the records that have no scope key (central for a restricted user, D-52-1) per entity, through
# "docker exec ... psql" on the local stack or, with -SshHost, on the server. It prints "database | entity | no key | total".
param(
    [switch]$SelfTest,
    [switch]$Counts,
    [string]$Project = "hrm-acceptance",
    [string]$SshHost = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }

function Read-Text([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path)) { return $null }
    return [System.IO.File]::ReadAllText($path)
}

function Get-Json([string]$text) {
    if ($null -eq $text) { return $null }
    return ($text | ConvertFrom-Json)
}

# $sources: relative path -> text, every .cs file under src/Services and src/Shared
function Test-Requirement([hashtable]$sources) {
    $problems = @()
    foreach ($name in $sources.Keys) {
        $text = $sources[$name]
        if ($text -match 'class\s+OrgScopeRequirement\b') { $problems += "${name}: OrgScopeRequirement is back" }
        if ($text -match 'class\s+OrgScopeAuthorizationHandler\b') { $problems += "${name}: OrgScopeAuthorizationHandler is back" }
        if ($text -match '\bAddOrgScopeAuthorization\s*\(') { $problems += "${name}: AddOrgScopeAuthorization() is called again" }
    }
    return $problems
}

function Test-Mechanism([string]$registry, [string]$attributes, [string]$filter, [hashtable]$programs) {
    $problems = @()
    if ($null -eq $registry -or $registry -notmatch 'class\s+OrgScopeKeyRegistry\b') { $problems += "OrgScopeKeyRegistry is missing" }
    if ($null -eq $attributes) { $problems += "OrgScopeAttributes.cs is missing" }
    else {
        foreach ($attribute in @("OrgScopedAttribute", "OrgScopeCentralOnlyAttribute", "OrgScopeNotApplicableAttribute", "OrgScopeIdAttribute", "NotOrgScopeKeyAttribute")) {
            if ($attributes -notmatch "class\s+$attribute\b") { $problems += "$attribute is missing" }
        }
    }
    if ($null -eq $filter -or $filter -notmatch 'class\s+OrgScopeActionFilter<') { $problems += "OrgScopeActionFilter is missing" }
    elseif ($filter -notmatch 'FilterOrder\s*=\s*-3000') { $problems += "the filter no longer runs before the automatic 400 (order -3000)" }
    foreach ($service in $programs.Keys) {
        $keys = $programs[$service].Keys
        $text = $programs[$service].Text
        if ($null -eq $text) { $problems += "${service}: Program.cs is missing"; continue }
        if ($text -notmatch "AddOrgScopeFilter<[^>]+>\s*\(\s*[\w\.]*$keys\.Service\s*,\s*[\w\.]*$keys\.Configure\s*\)") {
            $problems += "${service}: Program.cs does not register the filter with $keys.Configure"
        }
    }
    return $problems
}

# $files: file name -> text, the files of tests/architecture/org-scope-allowlist
function Test-Lists([hashtable]$files) {
    $problems = @()
    foreach ($service in @("hr-core", "hr-contracts", "admin-logistics")) {
        if ($files.ContainsKey("$service.txt")) { $problems += "$service has an org-scope list again: every controller declares its stance" }
    }
    foreach ($name in $files.Keys) {
        $rows = @($files[$name] -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" -and -not $_.StartsWith("#") })
        foreach ($row in $rows) {
            $parts = $row -split '\|', 2
            if ($parts.Count -ne 2) { $problems += "${name}: '$row' has no reason"; continue }
            $reason = $parts[1].Trim()
            if (-not $reason.StartsWith("IAM-H:")) { $problems += "${name}: '$row' is not signed IAM-H" }
            elseif ($reason.Substring(6).Trim().Length -lt 10) { $problems += "${name}: '$row' gives no reason" }
        }
    }
    return $problems
}

# $bytes: the migration's first bytes; $text: the migration
function Test-Migration([byte[]]$bytes, [string]$text) {
    if ($null -eq $text) { return @("the migration HCM052_OrgScopeKeys is missing") }
    $problems = @()
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $problems += "HCM052_OrgScopeKeys has a byte order mark" }
    # The migration records the forced tables, lifts FORCE on them (ForEachRecorded("NO FORCE")) and restores it on exactly those.
    if ($text -notmatch 'relforcerowsecurity') { $problems += "HCM052_OrgScopeKeys does not record which tables are forced" }
    if ($text -notmatch 'ForEachRecorded\("NO FORCE"\)') { $problems += "HCM052_OrgScopeKeys does not lift FORCE" }
    if ($text -notmatch 'ForEachRecorded\("FORCE"\)') { $problems += "HCM052_OrgScopeKeys does not restore FORCE" }
    if ($text -notmatch 'ROW LEVEL SECURITY') { $problems += "HCM052_OrgScopeKeys does not alter row level security" }
    if ($text -notmatch 'ScopeOrgUnitId') { $problems += "HCM052_OrgScopeKeys does not backfill the workflow instances" }
    return $problems
}

function Test-Consumers([string]$directory, [string]$health) {
    $problems = @()
    if ($null -eq $directory) { $problems += "Auth.Directory's TransferExecutedConsumer is missing" }
    else {
        if ($directory -notmatch 'ScopeOrgUnitId\s*=\s*null') { $problems += "Auth.Directory's consumer does not clear the scope" }
        if ($directory -notmatch 'TryClearScopeAttributeAsync') { $problems += "Auth.Directory's consumer does not ask Keycloak to remove the attribute" }
        if ($directory -notmatch 'DirectoryScopeSyncSkipped') { $problems += "Auth.Directory's consumer does not count a skipped sync" }
        if ($directory -notmatch 'StageReceipt') { $problems += "Auth.Directory's consumer keeps no receipt" }
    }
    if ($null -eq $health) { $problems += "HR.Health's TransferExecutedConsumer is missing" }
    else {
        if ($health -notmatch 'EmployeeOrgUnitSnapshots') { $problems += "HR.Health's consumer does not move the employee snapshot" }
        if ($health -notmatch 'StageReceipt') { $problems += "HR.Health's consumer keeps no receipt" }
    }
    return $problems
}

function Test-Acceptance($scope, $ownership) {
    $problems = @()
    $row = @($scope.prodLike | Where-Object { $_.class -eq "AcIam52BranchScopeTests" })
    if ($row.Count -ne 1 -or $row[0].group -ne "main" -or -not $row[0].required) { $problems += "AcIam52BranchScopeTests is not a required prod-like class of the main group" }
    $ac = $ownership.PSObject.Properties | Where-Object { $_.Name -eq "acs" -or $_.Name -eq "classes" -or $_.Name -eq "entries" } | Select-Object -First 1
    $entries = if ($null -ne $ac) { $ac.Value } else { $ownership }
    $entry = $entries.PSObject.Properties | Where-Object { $_.Name -eq "AC-IAM-52" } | Select-Object -First 1
    if ($null -eq $entry -or $entry.Value.ownerPlan -ne "PLAN-52" -or -not $entry.Value.provesClaim) { $problems += "AC-IAM-52 is not owned by PLAN-52 with provesClaim" }
    return $problems
}

$paths = @{
    Registry = "src/Shared/HRManagement.ServiceDefaults/OrgScope/OrgScopeKeyRegistry.cs"
    Attributes = "src/Shared/HRManagement.ServiceDefaults/OrgScope/OrgScopeAttributes.cs"
    Filter = "src/Shared/HRManagement.ServiceDefaults/OrgScope/OrgScopeActionFilter.cs"
    Directory = "src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/TransferExecutedConsumer.cs"
    Health = "src/Services/HRHealth/HR.Health.Infrastructure/Consumers/TransferExecutedConsumer.cs"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
    Lists = "tests/architecture/org-scope-allowlist"
    Migrations = "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
}

function Get-Programs {
    return @{
        "hr-core" = @{ Keys = "HrCoreOrgScopeKeys"; Text = (Read-Text "src/Services/HRCore/HR.Core.Web/Program.cs") }
        "hr-contracts" = @{ Keys = "ContractsOrgScopeKeys"; Text = (Read-Text "src/Services/HRContracts/HR.Contracts.Web/Program.cs") }
        "admin-logistics" = @{ Keys = "AdminLogisticsOrgScopeKeys"; Text = (Read-Text "src/Services/AdminLogistics/Admin.Logistics.Web/Program.cs") }
    }
}

function Get-ListFiles {
    $files = @{}
    $folder = Join-Path $root $paths.Lists
    if (Test-Path $folder) {
        Get-ChildItem -Path $folder -Filter *.txt | ForEach-Object { $files[$_.Name] = [System.IO.File]::ReadAllText($_.FullName) }
    }
    return $files
}

function Get-Sources {
    $sources = @{}
    foreach ($folder in @("src/Services", "src/Shared")) {
        Get-ChildItem -Path (Join-Path $root $folder) -Recurse -Filter *.cs |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
            ForEach-Object { $sources[$_.FullName.Substring($root.Length + 1)] = [System.IO.File]::ReadAllText($_.FullName) }
    }
    return $sources
}

function Get-Migration {
    $folder = Join-Path $root $paths.Migrations
    $file = Get-ChildItem -Path $folder -Filter "*_HCM052_OrgScopeKeys.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Select-Object -First 1
    if ($null -eq $file) { return @{ Bytes = [byte[]]@(); Text = $null } }
    return @{ Bytes = [System.IO.File]::ReadAllBytes($file.FullName); Text = [System.IO.File]::ReadAllText($file.FullName) }
}

function Invoke-All {
    $all = @()
    $all += Test-Requirement (Get-Sources)
    $all += Test-Mechanism (Read-Text $paths.Registry) (Read-Text $paths.Attributes) (Read-Text $paths.Filter) (Get-Programs)
    $all += Test-Lists (Get-ListFiles)
    $migration = Get-Migration
    $all += Test-Migration $migration.Bytes $migration.Text
    $all += Test-Consumers (Read-Text $paths.Directory) (Read-Text $paths.Health)
    $all += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    return @($all | Where-Object { $_ })
}

# ---------------------------------------------------------------- counts (read-only)

$countQueries = [ordered]@{
    "hr_core_db" = @(
        'SELECT ''WorkflowInstances:'' || "EntityType", count(*) FILTER (WHERE "ScopeOrgUnitId" IS NULL), count(*) FROM "WorkflowInstances" GROUP BY "EntityType"',
        'SELECT ''DiwanCorrespondences'', count(*) FILTER (WHERE "OrgUnitId" IS NULL), count(*) FROM "DiwanCorrespondences"',
        'SELECT ''FieldInspectionVisits'', count(*) FILTER (WHERE "OrgUnitId" IS NULL), count(*) FROM "FieldInspectionVisits"',
        'SELECT ''HrStatisticReports'', count(*) FILTER (WHERE "OrgUnitId" IS NULL), count(*) FROM "HrStatisticReports"',
        'SELECT ''ShiftRosters'', count(*) FILTER (WHERE "OrgUnitId" IS NULL), count(*) FROM "ShiftRosters"',
        'SELECT ''KeyPositions'', count(*) FILTER (WHERE "OrgUnitId" IS NULL), count(*) FROM "KeyPositions"',
        'SELECT ''InternalVacancies'', count(*) FILTER (WHERE "OrgUnitId" IS NULL), count(*) FROM "InternalVacancies"',
        'SELECT ''Employees'', count(*) FILTER (WHERE "PrimaryOrgUnitId" IS NULL), count(*) FROM "Employees"'
    )
    "hr_contracts_db" = @(
        'SELECT ''ServiceTransactions'', count(*) FILTER (WHERE s."PrimaryOrgUnitId" IS NULL), count(*) FROM "ServiceTransactions" t LEFT JOIN "EmployeeOrgUnitSnapshots" s ON s."EmployeeId" = t."EmployeeId"',
        'SELECT ''Contracts'', count(*) FILTER (WHERE s."PrimaryOrgUnitId" IS NULL), count(*) FROM "Contracts" t LEFT JOIN "EmployeeOrgUnitSnapshots" s ON s."EmployeeId" = t."EmployeeId"',
        'SELECT ''EmployeeOrgUnitSnapshots'', count(*) FILTER (WHERE "PrimaryOrgUnitId" IS NULL), count(*) FROM "EmployeeOrgUnitSnapshots"',
        'SELECT ''OrgUnitSnapshots'', 0, count(*) FROM "OrgUnitSnapshots"'
    )
    "admin_db" = @(
        'SELECT ''SupplyOrders'', count(*) FILTER (WHERE "OrgUnitId" IS NULL), count(*) FROM "SupplyOrders"',
        'SELECT ''Assets'', count(*) FILTER (WHERE NOT EXISTS (SELECT 1 FROM "AssetAssignments" x JOIN "EmployeeOrgUnitSnapshots" s ON s."EmployeeId" = x."EmployeeId" WHERE x."AssetId" = a."Id" AND x."ReturnedAt" IS NULL AND s."PrimaryOrgUnitId" IS NOT NULL)), count(*) FROM "Assets" a',
        'SELECT ''EmployeeOrgUnitSnapshots'', count(*) FILTER (WHERE "PrimaryOrgUnitId" IS NULL), count(*) FROM "EmployeeOrgUnitSnapshots"',
        'SELECT ''OrgUnitSnapshots'', 0, count(*) FROM "OrgUnitSnapshots"'
    )
}

function Invoke-Counts {
    $OutputEncoding = New-Object System.Text.UTF8Encoding $false
    $locate = "docker ps -q --filter label=com.docker.compose.project=$Project --filter label=com.docker.compose.service=postgresql"
    $rows = @()
    foreach ($database in $countQueries.Keys) {
        $sql = ($countQueries[$database] -join " UNION ALL ") + " ORDER BY 1;"
        if ([string]::IsNullOrWhiteSpace($SshHost)) {
            $pg = (& docker ps -q --filter "label=com.docker.compose.project=$Project" --filter "label=com.docker.compose.service=postgresql" | Select-Object -First 1)
            if ([string]::IsNullOrWhiteSpace($pg)) { throw "No postgresql container of the project $Project is running." }
            $output = $sql | & docker exec -i $pg sh -c "psql -X -q -U `"`$POSTGRES_USER`" -d $database -v ON_ERROR_STOP=1 -tA -F '|' -c 'BEGIN READ ONLY' -f - -c 'ROLLBACK'"
        }
        else {
            # One remote shell: find the container, then psql reads the statement from stdin, inside a read-only transaction.
            $remote = "pg=`$($locate | head -n 1); [ -n `"`$pg`" ] || { echo 'no postgresql container' >&2; exit 2; }; docker exec -i `"`$pg`" sh -c 'psql -X -q -U `"`$POSTGRES_USER`" -d $database -v ON_ERROR_STOP=1 -tA -F `"|`" -c `"BEGIN READ ONLY`" -f - -c ROLLBACK'"
            $output = $sql | & ssh -o BatchMode=yes $SshHost $remote
        }
        if ($LASTEXITCODE -ne 0) { throw "psql on $database failed (exit $LASTEXITCODE)." }
        foreach ($line in @($output | Where-Object { $_ -match '\|' })) {
            $parts = $line -split '\|'
            $rows += [pscustomobject]@{ Database = $database; Entity = $parts[0]; NoKey = [int]$parts[1]; Total = [int]$parts[2] }
        }
    }
    return $rows
}

if ($Counts) {
    $rows = Invoke-Counts
    $where = if ([string]::IsNullOrWhiteSpace($SshHost)) { "local project $Project" } else { "$SshHost, project $Project" }
    Write-Host "Records with no scope key (central for a restricted user, D-52-1) - $where"
    $rows | ForEach-Object { Write-Output ("{0} | {1} | {2} | {3}" -f $_.Database, $_.Entity, $_.NoKey, $_.Total) }
    exit 0
}

if ($SelfTest) {
    $failures = @()
    $sources = @{ "planted.cs" = "public sealed class OrgScopeRequirement : IAuthorizationRequirement { }" }
    if ((Test-Requirement $sources).Count -eq 0) { $failures += "OrgScopeRequirement back in the code was not refused" }
    $programs = Get-Programs
    $planted = @{}
    foreach ($key in $programs.Keys) { $planted[$key] = $programs[$key].Clone() }
    $planted["admin-logistics"].Text = $planted["admin-logistics"].Text -replace 'AddOrgScopeFilter<', 'AddSomethingElse<'
    if ((Test-Mechanism (Read-Text $paths.Registry) (Read-Text $paths.Attributes) (Read-Text $paths.Filter) $planted).Count -eq 0) { $failures += "a service without the filter was not refused" }
    $filter = (Read-Text $paths.Filter) -replace 'FilterOrder\s*=\s*-3000', 'FilterOrder = 0'
    if ((Test-Mechanism (Read-Text $paths.Registry) (Read-Text $paths.Attributes) $filter $programs).Count -eq 0) { $failures += "a filter after the automatic 400 was not refused" }
    $lists = Get-ListFiles
    $withList = $lists.Clone(); $withList["hr-core.txt"] = "Employees | IAM-H: latent, no restricted role reaches it (D-IAM-3)"
    if ((Test-Lists $withList).Count -eq 0) { $failures += "an HR.Core list was not refused" }
    $unsigned = $lists.Clone(); $unsigned["payroll.txt"] = "PayrollRuns | to save time"
    if ((Test-Lists $unsigned).Count -eq 0) { $failures += "an unsigned row was not refused" }
    $migration = Get-Migration
    $bom = [byte[]](@(0xEF, 0xBB, 0xBF) + $migration.Bytes)
    if ((Test-Migration $bom $migration.Text).Count -eq 0) { $failures += "a migration with a byte order mark was not refused" }
    if ((Test-Migration $migration.Bytes ($migration.Text -replace 'ForEachRecorded\("NO FORCE"\)', 'string.Empty')).Count -eq 0) { $failures += "a migration that keeps FORCE on was not refused" }
    $directory = (Read-Text $paths.Directory) -replace 'TryClearScopeAttributeAsync', 'Nothing'
    if ((Test-Consumers $directory (Read-Text $paths.Health)).Count -eq 0) { $failures += "a consumer that leaves the Keycloak attribute was not refused" }
    if ((Test-Consumers (Read-Text $paths.Directory) $null).Count -eq 0) { $failures += "a missing HR.Health consumer was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $scope.prodLike = @($scope.prodLike | Where-Object { $_.class -ne "AcIam52BranchScopeTests" })
    if ((Test-Acceptance $scope (Get-Json (Read-Text $paths.Ownership))).Count -eq 0) { $failures += "a scope without AC-IAM-52 was not refused" }
    if ((Invoke-All).Count -ne 0) { $failures += "the repository itself does not pass" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] iam052-validate self-test: 10 planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] iam052-validate: no OrgScopeRequirement, the registry and the filter in the three services, IAM-H lists only, HCM052, the transfer consumers, AC-IAM-52" -ForegroundColor Green
exit 0
