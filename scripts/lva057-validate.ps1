# PLAN-57 (LVA-C): static checks of the attendance period, its single writer and the handover to payroll; and a read-only
# inventory of the attendance data.
# ASCII only; Windows PowerShell 5.1 syntax (run here by pwsh 7).
#
#   pwsh -NoProfile -File scripts/lva057-validate.ps1 [-SelfTest]
#   pwsh -NoProfile -File scripts/lva057-validate.ps1 -Inventory [-PgContainer <name>] [-PgUser <role>] [-SshHost <alias>]
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

Write-Output "lva057-validate: the static checks are added with the code they guard (PLAN-57 P7)."
exit 0
