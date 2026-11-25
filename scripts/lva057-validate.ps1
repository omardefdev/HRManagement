# PLAN-57 (LVA-C): static checks of the attendance period, its single writer and the handover to payroll; and a read-only
# inventory of the attendance data.
# ASCII only; Windows PowerShell 5.1 syntax (run here by pwsh 7).
#
#   pwsh -NoProfile -File scripts/lva057-validate.ps1 [-SelfTest]
#   pwsh -NoProfile -File scripts/lva057-validate.ps1 -Inventory [-PgContainer <name>] [-PgUser <role>] [-SshHost <alias>]
#
# What the static checks read (Lva057* tests, AttendanceSingleWriterArchitectureTests and AC-LVA-57 prove the behaviour; this script
# guards the files a reviewer or an operator edits by hand):
#   1. The files of the period, the writer, the correction, the handover and their tests exist.
#   2. Every write action of the three attendance controllers names its policy (HrCore.AttendanceWrite for a record and for asking
#      a correction, HrCore.AttendanceClose for a close, HrCore.AttendanceCorrectionApprove for a decision) and validates an
#      antiforgery token.
#   3. Nothing but its own contract file names IAttendancePeriodClosed (the old close's event has no publisher and no consumer).
#   4. No product code writes an attendance day outside AttendanceDayWriter (the demo seeder apart): no Add, Remove, bulk statement
#      or raw SQL on DailyAttendanceRecords, and no record constructed elsewhere.
#   5. The two migrations and their designers have no byte order mark; HR.Core's lifts FORCE on DailyAttendanceRecords before its
#      fill and puts it back after, and puts row security on its four new tables; Payroll's puts it on the receipts.
#   6. No list of holidays in code, in an appsettings file of HR.Core, or in a compose file other than the acceptance layer's.
#   7. The settings of appsettings.Production.json (the period, the close, the corrections, the rest days, payroll's gate) are
#      mirrored in docker-compose.acceptance-prodlike.yml with the same values.
#   8. docs/EVENT_PUBLISHER_INVENTORY.md has the IAttendanceSummaryPublished row with its publisher.
#   9. AcLva57AttendancePeriodTests is a required prod-like class of the main group, and AC-LVA-57 is owned by PLAN-57.
#  10. The holidays of docker-compose.acceptance.yml are the holidays of testdata/acceptance/lva057-periods.json, and the months that
#      file reserves are whole months of the past.
# -SelfTest runs each check on a planted defect and fails unless every one of them is refused.
#
# -Inventory reads, and writes nothing (every statement runs in a READ ONLY transaction, through psql inside the PostgreSQL
# container; over ssh when -SshHost is given). It prints counts and checksums only, never a name:
#   hr_core_db : attendance records by status; records on Fridays and Saturdays by status; OnLeave records made by a leave
#                (the note the leave consumer writes) and typed by hand; a checksum of every record (id, employee, date, status);
#                the periods, summaries and corrections once the tables exist.
#   payroll_db : absence rows by reason prefix and state; rows of the old close (AttendancePeriodClosed:); a checksum of the runs
#                and of the salary lines; the receipts once the table exists.
param(
    [switch]$SelfTest,
    [switch]$Inventory,
    [string]$PgContainer = "hrm-acceptance-postgresql-1",
    [string]$PgUser = "hrm_ci",
    [string]$SshHost = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }

# The note LeaveApprovedConsumer wrote on a leave-made record before PLAN-57 (Arabic, as PostgreSQL unicode escapes).
$leaveNotePattern = "U&'\0625\062C\0627\0632\0629 \0645\0639\062A\0645\062F\0629:%'"

function Invoke-ReadOnlySql([string]$database, [string]$sql) {
    $script = "BEGIN TRANSACTION READ ONLY;`n" + $sql + "`nCOMMIT;`n"
    $psql = "docker exec -i $PgContainer psql -X -q -v ON_ERROR_STOP=1 -U $PgUser -d $database -At -F '|' -f -"
    if ([string]::IsNullOrWhiteSpace($SshHost)) {
        $out = $script | & bash -c $psql 2>&1
    }
    else {
        $out = $script | & ssh -o BatchMode=yes $SshHost $psql 2>&1
    }
    if ($LASTEXITCODE -ne 0) { throw "psql failed on ${database}: $out" }
    return @($out | Where-Object { $_ -and ("$_" -ne "BEGIN") -and ("$_" -ne "COMMIT") })
}

function Show-Section([string]$title, $rows) {
    Write-Output ("## " + $title)
    if ($null -eq $rows -or @($rows).Count -eq 0) { Write-Output "(none)" }
    else { foreach ($r in @($rows)) { Write-Output ("  " + $r) } }
}

if ($Inventory) {
    $hr = @"
select 'records|' || count(*) from "DailyAttendanceRecords";
select 'status|' || "Status" || '|' || count(*) from "DailyAttendanceRecords" group by "Status" order by "Status";
select 'restday|' || d || '|status ' || st || '|' || n from (
  select case extract(dow from "AttendanceDate") when 5 then 'Friday' else 'Saturday' end as d, "Status" as st, count(*) as n
  from "DailyAttendanceRecords" where extract(dow from "AttendanceDate") in (5, 6) group by 1, 2) t order by d, st;
select 'onleave-by-leave|' || count(*) from "DailyAttendanceRecords" where "Status" = 2 and "Notes" like $leaveNotePattern;
select 'onleave-by-hand|' || count(*) from "DailyAttendanceRecords" where "Status" = 2 and ("Notes" is null or "Notes" not like $leaveNotePattern);
select 'checksum|' || coalesce(md5(string_agg(md5("Id"::text || '|' || "EmployeeId"::text || '|' || "AttendanceDate"::text || '|' || "Status"::text), '' order by "Id")), '-') || '|' || count(*)
  from "DailyAttendanceRecords";
select 'range|' || coalesce(min("AttendanceDate")::text, '-') || '|' || coalesce(max("AttendanceDate")::text, '-') from "DailyAttendanceRecords";
select 'tables|' || coalesce(string_agg(tablename, ',' order by tablename), '-') from pg_tables
  where schemaname = 'public' and tablename in ('AttendancePeriods', 'AttendancePeriodClosures', 'AttendanceEmployeeSummaries', 'AttendanceCorrections');
"@
    Show-Section "hr_core_db: attendance records" (Invoke-ReadOnlySql "hr_core_db" $hr)

    $hasNew = Invoke-ReadOnlySql "hr_core_db" "select count(*) from pg_tables where schemaname = 'public' and tablename = 'AttendanceEmployeeSummaries';"
    if ("$($hasNew | Select-Object -Last 1)" -eq "1") {
        $hrNew = @"
select 'source|' || "Source" || '|' || count(*) from "DailyAttendanceRecords" group by "Source" order by "Source";
select 'periods|' || to_char("PeriodStart", 'YYYY-MM') || '|status ' || "Status" from "AttendancePeriods" order by "PeriodStart";
select 'summaries|' || count(*) || '|max version ' || coalesce(max("Version"), 0) from "AttendanceEmployeeSummaries";
select 'corrections|status ' || "Status" || '|' || count(*) from "AttendanceCorrections" group by "Status" order by "Status";
"@
        Show-Section "hr_core_db: periods, summaries, corrections" (Invoke-ReadOnlySql "hr_core_db" $hrNew)
    }

    $pay = @"
select 'absences|' || count(*) from "AbsenceDeductions";
select 'absence|' || prefix || '|' || state || '|' || n || '|days ' || days from (
  select coalesce(split_part("Reason", ':', 1), '(no reason)') as prefix, case when "AppliedInRunId" is null then 'free' else 'applied' end as state,
         count(*) as n, sum("AbsentDays") as days
  from "AbsenceDeductions" group by 1, 2) t order by prefix, state;
select 'old-close-rows|' || count(*) from "AbsenceDeductions" where "Reason" like 'AttendancePeriodClosed:%';
select 'runs|' || coalesce(md5(string_agg(md5("Id"::text || '|' || "Status"::text || '|' || "TotalNetPay"::text || '|' || "PeriodStart"::text), '' order by "Id")), '-') || '|' || count(*) from "PayrollRuns";
select 'run-status|' || "Status" || '|' || count(*) from "PayrollRuns" group by "Status" order by "Status";
select 'lines|' || coalesce(md5(string_agg(md5("Id"::text || '|' || "GrossPay"::text || '|' || "TotalDeductions"::text || '|' || "NetPay"::text), '' order by "Id")), '-') || '|' || count(*) from "SalaryLines";
select 'audit-records|status ' || "Status" || '|' || count(*) from "PayrollAuditRecords" group by "Status" order by "Status";
"@
    Show-Section "payroll_db: absence rows, runs, lines" (Invoke-ReadOnlySql "payroll_db" $pay)

    $hasReceipts = Invoke-ReadOnlySql "payroll_db" "select count(*) from pg_tables where schemaname = 'public' and tablename = 'AttendanceSummaryReceipts';"
    if ("$($hasReceipts | Select-Object -Last 1)" -eq "1") {
        $payNew = @"
select 'receipts|' || count(*) || '|max version ' || coalesce(max("LatestVersion"), 0) from "AttendanceSummaryReceipts";
select 'attendance-rows|' || kind || '|' || state || '|' || n || '|days ' || days from (
  select case when "CarryForward" then 'carried' else 'own period' end as kind, case when "AppliedInRunId" is null then 'free' else 'applied' end as state,
         count(*) as n, sum("AbsentDays") as days
  from "AbsenceDeductions" where "SourceReference" like 'ATT:%' group by 1, 2) t order by kind, state;
"@
        Show-Section "payroll_db: receipts and attendance rows" (Invoke-ReadOnlySql "payroll_db" $payNew)
    }
    exit 0
}

# ---------------------------------------------------------------- the static checks

function Read-Text([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path)) { return $null }
    return [System.IO.File]::ReadAllText($path)
}

function Get-Json([string]$text) {
    if ($null -eq $text) { return $null }
    return ($text.TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

$paths = @{
    Services = "src/Services"
    Shared = "src/Shared"
    Records = "src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs"
    Periods = "src/Services/HRCore/HR.Core.Web/Controllers/AttendancePeriodsController.cs"
    Corrections = "src/Services/HRCore/HR.Core.Web/Controllers/AttendanceCorrectionsController.cs"
    Contract = "src/Shared/HRManagement.MessageContracts/Events/IAttendancePeriodClosed.cs"
    CoreMigrations = "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
    PayrollMigrations = "src/Services/Payroll/Payroll.Infrastructure/Migrations"
    CoreSettings = "src/Services/HRCore/HR.Core.Web/appsettings.Production.json"
    PayrollSettings = "src/Services/Payroll/Payroll.Web/appsettings.Production.json"
    Overlay = "docker-compose.acceptance-prodlike.yml"
    AcceptanceCompose = "docker-compose.acceptance.yml"
    Reserved = "testdata/acceptance/lva057-periods.json"
    Inventory = "docs/EVENT_PUBLISHER_INVENTORY.md"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
}
$requiredFiles = @(
    "src/Services/HRCore/HR.Core.Domain/Entities/AttendancePeriod.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/AttendanceEmployeeSummary.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/AttendanceCorrection.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/AttendanceSummaryCalculator.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Attendance/AttendanceDayWriter.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Attendance/AttendancePeriodService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Attendance/AttendanceCorrectionService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Attendance/AttendanceCalendar.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Attendance/AttendanceLockService.cs",
    "src/Shared/HRManagement.MessageContracts/Events/IAttendanceSummaryPublished.cs",
    "src/Services/Payroll/Payroll.Domain/Entities/AttendanceSummaryReceipt.cs",
    "src/Services/Payroll/Payroll.Infrastructure/Consumers/AttendanceSummaryHandoff.cs",
    "src/Services/Payroll/Payroll.Application/Services/PayrollAttendanceGate.cs",
    "src/Services/Payroll/Payroll.Application/Services/PayrollAttendanceFreeze.cs",
    "src/Tests/HRManagement.UnitTests/Architecture/AttendanceSingleWriterArchitectureTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Lva057AttendanceCloseIntegrationTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Lva057PayrollAttendanceHandoffIntegrationTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Lva057PayrollAttendanceGateTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcLva57AttendancePeriodTests.cs",
    "docs/runbooks/attendance-period-close.md"
)
# The only files that write an attendance day (as AttendanceSingleWriterArchitectureTests reads them).
$dayWriters = @(
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Attendance/AttendanceDayWriter.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Data/DemoDataSeeder.cs"
)
$coreTables = @("AttendancePeriods", "AttendancePeriodClosures", "AttendanceEmployeeSummaries", "AttendanceCorrections")
$holidaySettings = @(
    "src/Services/HRCore/HR.Core.Web/appsettings.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Production.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Staging.json"
)

# Source files of a folder (C# and Razor), as @{ Path = relative path; Text = content }, without build outputs and migrations.
function Get-Sources([string]$relative) {
    $directory = Join-Path $root $relative
    $files = @()
    if (-not (Test-Path $directory)) { return $files }
    foreach ($file in (Get-ChildItem -Path $directory -Recurse -File -Include *.cs, *.cshtml)) {
        $path = $file.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
        if ($path -match '/(bin|obj|Migrations)/') { continue }
        $files += @{ Path = $path; Text = [System.IO.File]::ReadAllText($file.FullName) }
    }
    return $files
}

# A line of source without its comment, so that prose about a rule is not read as code.
function Get-Code([string]$line) {
    $trimmed = $line.TrimStart()
    if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("*") -or $trimmed.StartsWith("/*")) { return "" }
    $comment = $line.IndexOf("//")
    if ($comment -ge 0) { return $line.Substring(0, $comment) }
    return $line
}

# 1.
function Test-Files($existing) {
    $problems = @()
    foreach ($file in $requiredFiles) { if (@($existing) -notcontains $file) { $problems += "$file is missing" } }
    return $problems
}

# The class of a file that holds several controllers: from its declaration to the next top-level class.
function Get-Class([string]$text, [string]$name) {
    if ($null -eq $text) { return $null }
    $start = [regex]::Match($text, '(?m)^public\s+(sealed\s+)?class\s+' + $name + '\b')
    if (-not $start.Success) { return $null }
    $rest = $text.Substring($start.Index + $start.Length)
    $next = [regex]::Match($rest, '(?m)^(\[[^\r\n]*\]\s*)*public\s+(sealed\s+)?class\s')
    if ($next.Success) { return $text.Substring($start.Index, $start.Length + $next.Index) }
    return $text.Substring($start.Index)
}

# 2. A controller's text: every action whose attributes carry a write verb names the policy its name asks for, and validates
#    the token. $policyOf maps an action's name to the policy's constant.
function Test-Actions([string]$name, [string]$controller, [scriptblock]$policyOf) {
    if ($null -eq $controller) { return @("$name is missing") }
    $problems = @()
    $lines = $controller -split "`n"
    $writes = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $signature = [regex]::Match($lines[$i], '^\s*public\s+(async\s+)?[\w<>\[\]?,. ]+\s+(\w+)\s*\(')
        if (-not $signature.Success) { continue }
        $attributes = ""
        # The attribute lines above the signature (a blank line between them does not end the list).
        for ($j = $i - 1; $j -ge 0 -and ($lines[$j].Trim().StartsWith("[") -or $lines[$j].Trim().Length -eq 0); $j--) { $attributes = $lines[$j] + "`n" + $attributes }
        if ($attributes -notmatch '\bHttp(Post|Put|Patch|Delete)\b') { continue }
        $writes++
        $action = $signature.Groups[2].Value
        $policy = & $policyOf $action
        if ($attributes -notmatch ('Authorize\(Policy\s*=\s*HrManagementPolicies\.' + $policy + '\)')) { $problems += "${name}.${action} does not name the policy $policy" }
        if ($attributes -notmatch '\bValidateAntiForgeryToken\b') { $problems += "${name}.${action} has no [ValidateAntiForgeryToken]" }
    }
    if ($writes -eq 0) { $problems += "$name has no write action: the check reads the wrong file" }
    return $problems
}

$recordPolicy = { param($action) "HrCoreAttendanceWrite" }
$periodPolicy = { param($action) "HrCoreAttendanceClose" }
$correctionPolicy = { param($action) if ($action -match '^(Approve|Reject)') { "HrCoreAttendanceCorrectionApprove" } else { "HrCoreAttendanceWrite" } }

# 3.
function Test-OldEvent($sources) {
    $problems = @()
    foreach ($source in $sources) {
        if ($source.Path -eq $paths.Contract) { continue }
        if ($source.Text -match '\bIAttendancePeriodClosed\b') { $problems += "$($source.Path) names IAttendancePeriodClosed: the old close's event has no publisher and no consumer" }
    }
    return $problems
}

# 4.
$dayWrites = @(
    @{ What = "adds, removes or updates an attendance record"; Pattern = New-Object System.Text.RegularExpressions.Regex('\bDailyAttendanceRecords\s*\.\s*(Add|AddAsync|AddRange|AddRangeAsync|Remove|RemoveRange|Update|UpdateRange|Attach)\s*\(') },
    @{ What = "runs a bulk statement on the attendance records"; Pattern = New-Object System.Text.RegularExpressions.Regex('\bDailyAttendanceRecords\b[^;]*\.\s*Execute(Update|Delete)(Async)?\s*\(') },
    @{ What = "constructs an attendance record"; Pattern = New-Object System.Text.RegularExpressions.Regex('\bnew\s+DailyAttendanceRecord\s*[\{\(\r\n]') },
    @{ What = "writes the attendance records by raw SQL"; Pattern = New-Object System.Text.RegularExpressions.Regex('(?i)\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+\\?"DailyAttendanceRecords\\?"') }
)
function Test-SingleWriter($sources) {
    $problems = @()
    foreach ($source in $sources) {
        if ($dayWriters -contains $source.Path) { continue }
        if ($source.Text -notmatch 'DailyAttendanceRecord') { continue }
        $code = (($source.Text -split "`n") | ForEach-Object { Get-Code $_ }) -join "`n"
        foreach ($write in $dayWrites) {
            if ($write.Pattern.IsMatch($code)) { $problems += "$($source.Path) $($write.What) outside AttendanceDayWriter" }
        }
    }
    return $problems
}

# 5.
function Test-NoBom([string]$name, $bytes) {
    if ($null -eq $bytes) { return @("migration $name is missing") }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return @("migration $name starts with a byte order mark") }
    return @()
}

function Test-RowSecurity([string]$name, [string]$text, $tables) {
    $problems = @()
    $up = [regex]::Match($text, '(?s)protected override void Up\(MigrationBuilder migrationBuilder\)(.*?)protected override void Down\(')
    if (-not $up.Success) { return @("$name has no Up method") }
    if ($up.Groups[1].Value -notmatch '(?s)foreach\s*\(var\s+table\s+in\s+NewTenantTables\)\s*TenantRlsMigrationHelper\.EnableRls\(\s*migrationBuilder\s*,\s*table\s*\)') {
        $problems += "$name does not put row security on its new tables"
    }
    $list = [regex]::Match($text, '(?s)NewTenantTables\s*=\s*(.*?);')
    foreach ($table in $tables) {
        if (-not $list.Success -or $list.Groups[1].Value -notmatch ('"' + $table + '"')) { $problems += "$name does not put row security on $table" }
    }
    return $problems
}

function Test-CoreMigration([string]$text) {
    if ($null -eq $text) { return @("migration LVA057_AttendancePeriods is missing") }
    $problems = @(Test-RowSecurity "LVA057_AttendancePeriods" $text $coreTables)
    $lift = $text.IndexOf('ALTER TABLE "DailyAttendanceRecords" NO FORCE ROW LEVEL SECURITY;')
    $fill = if ($lift -ge 0) { $text.IndexOf('migrationBuilder.Sql(MarkLeaveMadeRecords);', $lift) } else { -1 }
    $restore = if ($fill -ge 0) { $text.IndexOf('ALTER TABLE "DailyAttendanceRecords" FORCE ROW LEVEL SECURITY;', $fill) } else { -1 }
    if ($lift -lt 0) { $problems += "LVA057_AttendancePeriods does not lift FORCE on DailyAttendanceRecords before its fill: under forced row security the fill sees no row" }
    elseif ($fill -lt 0) { $problems += "LVA057_AttendancePeriods does not run its fill of the leave-made records after lifting FORCE" }
    elseif ($restore -lt 0) { $problems += "LVA057_AttendancePeriods does not put FORCE back on DailyAttendanceRecords after its fill" }
    return $problems
}

function Test-PayrollMigration([string]$text) {
    if ($null -eq $text) { return @("migration LVA057_AttendanceHandoff is missing") }
    return @(Test-RowSecurity "LVA057_AttendanceHandoff" $text @("AttendanceSummaryReceipts"))
}

# 6. $settings: @{ Path; Text } of HR.Core's appsettings files; $composes: @{ Path; Text } of the compose files.
function Test-NoHolidayList($sources, $settings, $composes) {
    $problems = @()
    foreach ($source in $sources) {
        if ($source.Text -notmatch 'Holiday') { continue }
        foreach ($line in ($source.Text -split "`n")) {
            $code = Get-Code $line
            if ($code -match 'Holiday' -and ($code -match '\b\d{4}-\d{2}-\d{2}\b' -or $code -match 'new\s+(DateOnly|DateTime)\s*\(\s*\d')) {
                $problems += "$($source.Path) holds a holiday in code: holidays are configuration"
                break
            }
        }
    }
    foreach ($file in $settings) {
        if ($null -eq $file.Text) { $problems += "$($file.Path) is missing"; continue }
        if ($file.Text -notmatch '"Holidays"\s*:\s*\[\s*\]') { $problems += "$($file.Path) does not keep Attendance:Calendar:Holidays empty: a list of holidays is the operator's, by environment" }
    }
    foreach ($file in $composes) {
        if ($file.Path -eq $paths.AcceptanceCompose) { continue }
        if ($file.Text -match 'Attendance__Calendar__Holidays') { $problems += "$($file.Path) sets holidays: only the acceptance layer does" }
    }
    return $problems
}

# 7.
function Test-Overlay($core, $payroll, [string]$overlay) {
    if ($null -eq $core -or $null -eq $core.Attendance) { return @("HR.Core's appsettings.Production.json has no Attendance section") }
    if ($null -eq $payroll -or $null -eq $payroll.Payroll -or $null -eq $payroll.Payroll.Attendance) { return @("Payroll's appsettings.Production.json has no Payroll:Attendance section") }
    if ($null -eq $overlay) { return @("docker-compose.acceptance-prodlike.yml is missing") }
    $expected = [ordered]@{
        "Attendance__Periods__Mode" = $core.Attendance.Periods.Mode
        "Attendance__Close__UnrecordedWorkingDays" = $core.Attendance.Close.UnrecordedWorkingDays
        "Attendance__Corrections__LeaveEventsAfterClose" = $core.Attendance.Corrections.LeaveEventsAfterClose
        "Attendance__Calendar__WeeklyRestDays" = $core.Attendance.Calendar.WeeklyRestDays
        "Payroll__Attendance__Gate" = $payroll.Payroll.Attendance.Gate
    }
    $problems = @()
    foreach ($key in $expected.Keys) {
        $row = [regex]::Match($overlay, '(?m)^\s*' + $key + '\s*:\s*"?([^"\r\n#]*?)"?\s*$')
        if ([string]::IsNullOrWhiteSpace($expected[$key])) { $problems += "appsettings.Production.json does not set $($key.Replace('__', ':'))" }
        elseif (-not $row.Success) { $problems += "docker-compose.acceptance-prodlike.yml does not set $key (Production: $($expected[$key]))" }
        elseif ($row.Groups[1].Value -ne $expected[$key]) { $problems += "docker-compose.acceptance-prodlike.yml sets $key to '$($row.Groups[1].Value)' and Production to '$($expected[$key])'" }
    }
    return $problems
}

# 8.
function Test-Inventory([string]$inventory) {
    if ($null -eq $inventory) { return @("docs/EVENT_PUBLISHER_INVENTORY.md is missing") }
    $row = [regex]::Match($inventory, '(?m)^\|\s*`IAttendanceSummaryPublished`[^|]*\|(.*)$')
    if (-not $row.Success) { return @("EVENT_PUBLISHER_INVENTORY.md has no IAttendanceSummaryPublished row") }
    if ($row.Groups[1].Value -notmatch 'AttendancePeriodService\.PublishSummaryAsync') { return @("the IAttendanceSummaryPublished row does not name its publisher (AttendancePeriodService.PublishSummaryAsync)") }
    return @()
}

# 9.
function Test-Acceptance($scope, $ownership) {
    $problems = @()
    if ($null -eq $scope) { return @("tests/acceptance/ci-scope.json is missing") }
    $row = @($scope.prodLike | Where-Object { $_.class -eq "AcLva57AttendancePeriodTests" })
    if ($row.Count -ne 1 -or $row[0].group -ne "main" -or -not $row[0].required) { $problems += "AcLva57AttendancePeriodTests is not a required prod-like class of the main group" }
    $entry = $null
    if ($null -ne $ownership -and $null -ne $ownership.acs) {
        $entry = $ownership.acs.PSObject.Properties | Where-Object { $_.Name -eq "AC-LVA-57" } | Select-Object -First 1
    }
    if ($null -eq $entry -or $entry.Value.ownerPlan -ne "PLAN-57" -or -not $entry.Value.provesClaim) { $problems += "AC-LVA-57 is not owned by PLAN-57 with provesClaim" }
    return $problems
}

# 10. $today: the day the months are compared with (a parameter, so that the self-test can move it).
function Test-Reserved($reserved, [string]$compose, [datetime]$today) {
    if ($null -eq $reserved) { return @("testdata/acceptance/lva057-periods.json is missing") }
    if ($null -eq $compose) { return @("docker-compose.acceptance.yml is missing") }
    $problems = @()
    $configured = @([regex]::Matches($compose, '(?m)^\s*Attendance__Calendar__Holidays__\d+\s*:\s*"?(\d{4}-\d{2}-\d{2})"?\s*$') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
    $listed = @($reserved.holidays | Sort-Object)
    if (($configured -join ",") -ne ($listed -join ",")) {
        $problems += "the acceptance layer's holidays ($($configured -join ', ')) are not the reserved file's ($($listed -join ', ')): AC-LVA-57b counts working days from both"
    }
    $thisMonth = $today.ToString("yyyy-MM")
    foreach ($month in $reserved.months.PSObject.Properties) {
        if ("$($month.Value)" -notmatch '^\d{4}-(0[1-9]|1[0-2])$') { $problems += "the reserved month $($month.Name) is not a yyyy-MM month" }
        elseif ([string]::CompareOrdinal("$($month.Value)", $thisMonth) -ge 0) { $problems += "the reserved month $($month.Name) ($($month.Value)) is not in the past: a period closes once it has ended" }
    }
    return $problems
}

function Get-MigrationFile([string]$directory, [string]$suffix) {
    $file = Get-ChildItem -Path (Join-Path $root $directory) -File -Filter ("*_" + $suffix) -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $file) { return @{ Name = $suffix; Bytes = $null; Text = $null } }
    return @{ Name = $file.Name; Bytes = [System.IO.File]::ReadAllBytes($file.FullName); Text = [System.IO.File]::ReadAllText($file.FullName) }
}

function Get-Product {
    return @((Get-Sources $paths.Services) + (Get-Sources $paths.Shared))
}

function Get-Settings {
    return @($holidaySettings | ForEach-Object { @{ Path = $_; Text = (Read-Text $_) } })
}

function Get-Composes {
    return @(Get-ChildItem -Path $root -File -Filter "docker-compose*.yml" | ForEach-Object { @{ Path = $_.Name; Text = [System.IO.File]::ReadAllText($_.FullName) } })
}

function Invoke-All {
    $all = @()
    $product = Get-Product
    $all += Test-Files @($requiredFiles | Where-Object { Test-Path (Join-Path $root $_) })
    $all += Test-Actions "DailyAttendanceRecordsController" (Get-Class (Read-Text $paths.Records) "DailyAttendanceRecordsController") $recordPolicy
    $all += Test-Actions "AttendancePeriodsController" (Read-Text $paths.Periods) $periodPolicy
    $all += Test-Actions "AttendanceCorrectionsController" (Read-Text $paths.Corrections) $correctionPolicy
    $all += Test-OldEvent $product
    $all += Test-SingleWriter $product
    foreach ($suffix in @("LVA057_AttendancePeriods.cs", "LVA057_AttendancePeriods.Designer.cs")) {
        $file = Get-MigrationFile $paths.CoreMigrations $suffix
        $all += Test-NoBom $file.Name $file.Bytes
    }
    foreach ($suffix in @("LVA057_AttendanceHandoff.cs", "LVA057_AttendanceHandoff.Designer.cs")) {
        $file = Get-MigrationFile $paths.PayrollMigrations $suffix
        $all += Test-NoBom $file.Name $file.Bytes
    }
    $all += Test-CoreMigration (Get-MigrationFile $paths.CoreMigrations "LVA057_AttendancePeriods.cs").Text
    $all += Test-PayrollMigration (Get-MigrationFile $paths.PayrollMigrations "LVA057_AttendanceHandoff.cs").Text
    $all += Test-NoHolidayList $product (Get-Settings) (Get-Composes)
    $all += Test-Overlay (Get-Json (Read-Text $paths.CoreSettings)) (Get-Json (Read-Text $paths.PayrollSettings)) (Read-Text $paths.Overlay)
    $all += Test-Inventory (Read-Text $paths.Inventory)
    $all += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    $all += Test-Reserved (Get-Json (Read-Text $paths.Reserved)) (Read-Text $paths.AcceptanceCompose) ([datetime]::UtcNow)
    return @($all | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $planted = 0
    $product = Get-Product

    # A copy of the sources with one file's text changed, or one file added.
    function Copy-Sources($sources, [string]$path, [string]$text) {
        $copy = @()
        $found = $false
        foreach ($source in $sources) {
            if ($source.Path -eq $path) { $copy += @{ Path = $path; Text = $text }; $found = $true } else { $copy += $source }
        }
        if (-not $found) { $copy += @{ Path = $path; Text = $text } }
        return $copy
    }

    # 1. the files
    $planted++; if ((Test-Files @($requiredFiles | Select-Object -Skip 1)).Count -eq 0) { $failures += "a missing file was not noticed" }

    # 2. the policies and the token
    $records = Get-Class (Read-Text $paths.Records) "DailyAttendanceRecordsController"
    $noPolicy = [regex]::Replace($records, '\[Authorize\(Policy = HrManagementPolicies\.HrCoreAttendanceWrite\)\]\s*', '', 1)
    $planted++; if ($noPolicy -eq $records -or (Test-Actions "planted" $noPolicy $recordPolicy).Count -eq 0) { $failures += "a record write without its policy was not refused" }
    $noToken = [regex]::Replace($records, '(\[HttpPost\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($noToken -eq $records -or (Test-Actions "planted" $noToken $recordPolicy).Count -eq 0) { $failures += "a record write without the antiforgery check was not refused" }
    $planted++; if ($null -ne $records -and $records -match 'class\s+FieldInspectionVisitsController') { $failures += "the records controller's text runs into the next class" }
    $periods = Read-Text $paths.Periods
    $weakClose = [regex]::Replace($periods, '(\[HttpPost\("api/attendance/periods/\{month\}/close"\)\]\s*)\[Authorize\(Policy = HrManagementPolicies\.HrCoreAttendanceClose\)\]', '${1}[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]', 1)
    $planted++; if ($weakClose -eq $periods -or (Test-Actions "planted" $weakClose $periodPolicy).Count -eq 0) { $failures += "a close open to every reader was not refused" }
    $closeNoToken = [regex]::Replace($periods, '(\[Authorize\(Policy = HrManagementPolicies\.HrCoreAttendanceClose\)\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($closeNoToken -eq $periods -or (Test-Actions "planted" $closeNoToken $periodPolicy).Count -eq 0) { $failures += "a close without the antiforgery check was not refused" }
    $corrections = Read-Text $paths.Corrections
    $branchDecides = $corrections -replace 'HrManagementPolicies\.HrCoreAttendanceCorrectionApprove', 'HrManagementPolicies.HrCoreAttendanceWrite'
    $planted++; if ($branchDecides -eq $corrections -or (Test-Actions "planted" $branchDecides $correctionPolicy).Count -eq 0) { $failures += "a decision open to whoever writes attendance was not refused" }
    $planted++; if ((Test-Actions "planted" "class Empty { }" $recordPolicy).Count -eq 0) { $failures += "a controller with no write action was not noticed" }
    $planted++; if ((Test-Actions "planted" $null $recordPolicy).Count -eq 0) { $failures += "a missing controller was not noticed" }

    # 3. the old event
    $publisher = "class Planted { Task Go(IPublishEndpoint bus) => bus.Publish<IAttendancePeriodClosed>(new { }); }"
    $planted++; if ((Test-OldEvent (Copy-Sources $product "src/Services/HRCore/HR.Core.Infrastructure/Planted.cs" $publisher)).Count -eq 0) { $failures += "a publisher of the old close's event was not refused" }
    $consumer = "class Planted : IConsumer<IAttendancePeriodClosed> { }"
    $planted++; if ((Test-OldEvent (Copy-Sources $product "src/Services/Payroll/Payroll.Infrastructure/Consumers/Planted.cs" $consumer)).Count -eq 0) { $failures += "a consumer of the old close's event was not refused" }

    # 4. the single writer
    $add = "class Planted { void Go(HRCoreDbContext db, DailyAttendanceRecord r) { db.DailyAttendanceRecords.Add(r); } }"
    $planted++; if ((Test-SingleWriter (Copy-Sources $product "src/Services/HRCore/HR.Core.Web/Controllers/Planted.cs" $add)).Count -eq 0) { $failures += "a controller that adds an attendance record was not refused" }
    $construct = "class Planted { object Go() => new DailyAttendanceRecord { Status = DailyAttendanceStatus.OnLeave }; }"
    $planted++; if ((Test-SingleWriter (Copy-Sources $product "src/Services/HRCore/HR.Core.Infrastructure/Consumers/Planted.cs" $construct)).Count -eq 0) { $failures += "a consumer that constructs an attendance record was not refused" }
    $bulk = "class Planted { Task Go(HRCoreDbContext db) => db.DailyAttendanceRecords.Where(r => r.EmployeeId == id).ExecuteDeleteAsync(); }"
    $planted++; if ((Test-SingleWriter (Copy-Sources $product "src/Services/HRCore/HR.Core.Infrastructure/Planted.cs" $bulk)).Count -eq 0) { $failures += "a bulk delete of the attendance records was not refused" }
    $sql = 'class Planted { string Sql = "UPDATE \"DailyAttendanceRecords\" SET \"Status\" = 0"; }'
    $planted++; if ((Test-SingleWriter (Copy-Sources $product "src/Services/HRCore/HR.Core.Infrastructure/Planted.cs" $sql)).Count -eq 0) { $failures += "a raw UPDATE of the attendance records was not refused" }
    $reader = "class Planted { Task<int> Go(HRCoreDbContext db) => db.DailyAttendanceRecords.CountAsync(r => r.Status == DailyAttendanceStatus.Absent); }"
    $planted++; if ((Test-SingleWriter (Copy-Sources $product "src/Services/HRCore/HR.Core.Infrastructure/Planted.cs" $reader)).Count -ne 0) { $failures += "a reader of the attendance records was taken for a writer" }
    $prose = "class Planted { // the old code called db.DailyAttendanceRecords.Add(record) here`n }"
    $planted++; if ((Test-SingleWriter (Copy-Sources $product "src/Services/HRCore/HR.Core.Infrastructure/Planted.cs" $prose)).Count -ne 0) { $failures += "a comment about the old code was taken for a write" }

    # 5. the migrations
    $core = Get-MigrationFile $paths.CoreMigrations "LVA057_AttendancePeriods.cs"
    $planted++; if ((Test-NoBom "planted" ([byte[]](@(0xEF, 0xBB, 0xBF) + $core.Bytes))).Count -eq 0) { $failures += "a migration with a byte order mark was not refused" }
    $planted++; if ((Test-NoBom "planted" $null).Count -eq 0) { $failures += "a missing migration was not noticed" }
    $noLift = $core.Text.Replace('migrationBuilder.Sql("""ALTER TABLE "DailyAttendanceRecords" NO FORCE ROW LEVEL SECURITY;""");', '')
    $planted++; if ($noLift -eq $core.Text -or (Test-CoreMigration $noLift).Count -eq 0) { $failures += "a fill under forced row security was not refused" }
    $noRestore = $core.Text.Replace('migrationBuilder.Sql("""ALTER TABLE "DailyAttendanceRecords" FORCE ROW LEVEL SECURITY;""");', '')
    $planted++; if ($noRestore -eq $core.Text -or (Test-CoreMigration $noRestore).Count -eq 0) { $failures += "a migration that leaves the records unforced was not refused" }
    $noRls = $core.Text -replace 'TenantRlsMigrationHelper\.EnableRls\(', 'TenantRlsMigrationHelper.Nothing('
    $planted++; if ($noRls -eq $core.Text -or (Test-CoreMigration $noRls).Count -eq 0) { $failures += "new attendance tables without row security were not refused" }
    $oneTable = $core.Text -replace '"AttendanceCorrections"(\s*[,\]\}])', '"SomethingElse"$1'
    $planted++; if ($oneTable -eq $core.Text -or (Test-CoreMigration $oneTable).Count -eq 0) { $failures += "a corrections table without row security was not refused" }
    $handoff = Get-MigrationFile $paths.PayrollMigrations "LVA057_AttendanceHandoff.cs"
    $noReceiptRls = $handoff.Text -replace 'NewTenantTables\s*=\s*\["AttendanceSummaryReceipts"\]', 'NewTenantTables = []'
    $planted++; if ($noReceiptRls -eq $handoff.Text -or (Test-PayrollMigration $noReceiptRls).Count -eq 0) { $failures += "receipts without row security were not refused" }

    # 6. no list of holidays
    $settings = Get-Settings
    $composes = Get-Composes
    $inCode = "class Planted { static readonly DateOnly[] Holidays = [new DateOnly(2027, 4, 17)]; }"
    $planted++; if ((Test-NoHolidayList (Copy-Sources $product "src/Services/HRCore/HR.Core.Infrastructure/Services/Attendance/Planted.cs" $inCode) $settings $composes).Count -eq 0) { $failures += "a holiday in code was not refused" }
    $inText = 'class Planted { string[] OfficialHolidays = ["2027-04-17"]; }'
    $planted++; if ((Test-NoHolidayList (Copy-Sources $product "src/Services/HRCore/HR.Core.Infrastructure/Services/Attendance/Planted.cs" $inText) $settings $composes).Count -eq 0) { $failures += "a holiday written as text in code was not refused" }
    $withList = @($settings | ForEach-Object { if ($_.Path -like "*Production*") { @{ Path = $_.Path; Text = ($_.Text -replace '"Holidays"\s*:\s*\[\s*\]', '"Holidays": [ "2027-04-17" ]') } } else { $_ } })
    $planted++; if ((Test-NoHolidayList $product $withList $composes).Count -eq 0) { $failures += "a holiday in the Production file was not refused" }
    $inCompose = @($composes + @{ Path = "docker-compose.vps.yml"; Text = '      Attendance__Calendar__Holidays__0: "2027-04-17"' })
    $planted++; if ((Test-NoHolidayList $product $settings $inCompose).Count -eq 0) { $failures += "a holiday in the demo's compose file was not refused" }

    # 7. the overlay
    $coreSettings = Get-Json (Read-Text $paths.CoreSettings)
    $payrollSettings = Get-Json (Read-Text $paths.PayrollSettings)
    $overlay = Read-Text $paths.Overlay
    $noGate = [regex]::Replace($overlay, '(?m)^\s*Payroll__Attendance__Gate:.*$', '')
    $planted++; if ($noGate -eq $overlay -or (Test-Overlay $coreSettings $payrollSettings $noGate).Count -eq 0) { $failures += "an overlay without payroll's gate was not refused" }
    $otherMode = $overlay -replace 'Attendance__Close__UnrecordedWorkingDays:\s*"RequireComplete"', 'Attendance__Close__UnrecordedWorkingDays: "Ignore"'
    $planted++; if ($otherMode -eq $overlay -or (Test-Overlay $coreSettings $payrollSettings $otherMode).Count -eq 0) { $failures += "an overlay that closes with another rule than Production's was not refused" }
    $planted++; if ((Test-Overlay $coreSettings (Get-Json '{ "Payroll": { } }') $overlay).Count -eq 0) { $failures += "a Production file without payroll's gate was not refused" }

    # 8. the event's row
    $publishers = Read-Text $paths.Inventory
    $noRow = [regex]::Replace($publishers, '(?m)^\|\s*`IAttendanceSummaryPublished`.*$', '')
    $planted++; if ($noRow -eq $publishers -or (Test-Inventory $noRow).Count -eq 0) { $failures += "an inventory without the IAttendanceSummaryPublished row was not refused" }
    $noPublisher = $publishers -replace 'AttendancePeriodService\.PublishSummaryAsync', 'SomewhereElse'
    $planted++; if ($noPublisher -eq $publishers -or (Test-Inventory $noPublisher).Count -eq 0) { $failures += "an IAttendanceSummaryPublished row without its publisher was not refused" }

    # 9. the acceptance class
    $ownership = Get-Json (Read-Text $paths.Ownership)
    $scope = Get-Json (Read-Text $paths.Scope)
    $scope.prodLike = @($scope.prodLike | Where-Object { $_.class -ne "AcLva57AttendancePeriodTests" })
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "a scope without AC-LVA-57 was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    ($scope.prodLike | Where-Object { $_.class -eq "AcLva57AttendancePeriodTests" }).required = $false
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "AC-LVA-57 not required was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $planted++; if ((Test-Acceptance $scope $null).Count -eq 0) { $failures += "AC-LVA-57 with no owner was not refused" }

    # 10. the reserved months and holidays
    $compose = Read-Text $paths.AcceptanceCompose
    $now = [datetime]::UtcNow
    $otherHoliday = $compose -replace '(Attendance__Calendar__Holidays__1:\s*)"\d{4}-\d{2}-\d{2}"', '$1"2025-02-19"'
    $planted++; if ($otherHoliday -eq $compose -or (Test-Reserved (Get-Json (Read-Text $paths.Reserved)) $otherHoliday $now).Count -eq 0) { $failures += "a holiday the reserved file does not list was not refused" }
    $oneLess = [regex]::Replace($compose, '(?m)^\s*Attendance__Calendar__Holidays__1:.*$', '')
    $planted++; if ($oneLess -eq $compose -or (Test-Reserved (Get-Json (Read-Text $paths.Reserved)) $oneLess $now).Count -eq 0) { $failures += "a reserved holiday the stack does not configure was not refused" }
    $planted++; if ((Test-Reserved (Get-Json (Read-Text $paths.Reserved)) $compose ([datetime]"2025-03-15")).Count -eq 0) { $failures += "a reserved month that has not ended was not refused" }
    $planted++; if ((Test-Reserved $null $compose $now).Count -eq 0) { $failures += "a missing reserved file was not noticed" }

    $own = Invoke-All
    if ($own.Count -ne 0) { $failures += "the repository itself does not pass: $($own -join '; ')" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] lva057-validate self-test: $planted planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] lva057-validate: every write of the attendance controllers names its policy and validates its token, nothing names the old close's event, an attendance day is written by AttendanceDayWriter only, the LVA057 migrations have no byte order mark, lift and restore FORCE around the fill and put row security on the new tables, no list of holidays is in code or in a Production or Staging file, the prod-like overlay mirrors Production's attendance settings, IAttendanceSummaryPublished is in the publisher inventory, AC-LVA-57 is required in the prod-like scope, and the acceptance holidays are the reserved file's" -ForegroundColor Green
exit 0
