# HCM-030 (C1, AC-HCM-30j): link requisition-linked citizen/internal applications submitted before unification
# to a RecruitmentApplication (scripts/sql/hcm030-backfill-unified-intake.sql).
#
# The SQL is one transaction and idempotent (a second run links nothing). It writes no application events, so the
# candidate notifications of PLAN-30 session 2 are never sent for these historic rows.
#
# Usage:
#   powershell -File scripts/hcm030-backfill-unified-intake.ps1                 # dry run (default): report only
#   powershell -File scripts/hcm030-backfill-unified-intake.ps1 -Apply
#   powershell -File scripts/hcm030-backfill-unified-intake.ps1 -Apply -ConnectionString "Host=localhost;..."
#
# -ConnectionString (default: $env:HRCORE_DB_CONNECTION) may be an Npgsql string ("Host=...;Username=...") or anything
# psql accepts (libpq keywords or a postgresql:// URI).

param(
    [switch]$DryRun,
    [switch]$Apply,
    [string]$ConnectionString = "",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

if ($DryRun -and $Apply) { throw "Use either -DryRun or -Apply." }
$isDryRun = -not $Apply

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

if (-not $ConnectionString) {
    $ConnectionString = $env:HRCORE_DB_CONNECTION
    if (-not $ConnectionString) {
        $ConnectionString = "Host=localhost;Port=5432;Database=hr_core_db;Username=hr;Password=hr"
    }
}

# Npgsql "Key=Value;..." -> libpq "key=value ..." (psql does not read the Npgsql format).
function ConvertTo-LibpqConnection([string]$Value) {
    if ($Value -match '^postgres(ql)?://' -or $Value -notmatch ';') { return $Value }
    $map = @{ host = "host"; server = "host"; port = "port"; database = "dbname"; username = "user"; "user id" = "user"; userid = "user"; password = "password"; "ssl mode" = "sslmode"; sslmode = "sslmode" }
    $parts = foreach ($pair in ($Value -split ';')) {
        if ([string]::IsNullOrWhiteSpace($pair) -or $pair -notmatch '=') { continue }
        $k, $v = $pair -split '=', 2
        $key = $map[$k.Trim().ToLowerInvariant()]
        if ($key) { "$key='" + $v.Trim().Replace("'", "\'") + "'" }
    }
    return ($parts -join ' ')
}
$ConnectionString = ConvertTo-LibpqConnection $ConnectionString

$sqlPath = Join-Path $RepoRoot "scripts/sql/hcm030-backfill-unified-intake.sql"
$sql = Get-Content -Path $sqlPath -Raw -Encoding utf8
$commitMarker = "COMMIT; -- HCM030:END"
if (-not $sql.Contains($commitMarker)) { throw "Commit marker not found in $sqlPath." }
if ($isDryRun) {
    $sql = $sql.Replace($commitMarker, "ROLLBACK; -- HCM030:END (dry run)")
}

$outDir = Join-Path $RepoRoot "artifacts/hcm030-backfill"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$mode = if ($isDryRun) { "dryrun" } else { "apply" }
$reportPath = Join-Path $outDir "unified-intake-$mode-$(Get-Date -Format 'yyyyMMdd-HHmmss').csv"

$tmp = [IO.Path]::GetTempFileName()
try {
    # No BOM: psql would read it as part of the first statement.
    [IO.File]::WriteAllText($tmp, $sql, (New-Object Text.UTF8Encoding $false))
    $env:PGCLIENTENCODING = "UTF8"
    $rows = @(& psql $ConnectionString -q -v ON_ERROR_STOP=1 -At -F '|' -f $tmp)
    if ($LASTEXITCODE -ne 0) { throw "psql failed ($LASTEXITCODE)" }
}
finally {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}

$statusNames = @{ "0" = "Submitted"; "1" = "UnderReview"; "2" = "Shortlisted"; "3" = "Rejected" }
$sourceNames = @{ "0" = "Received/Submitted"; "1" = "UnderReview"; "2" = "Shortlisted"; "3" = "Accepted"; "4" = "Rejected" }

$reportLines = @("SourceTable,SourceId,TenantId,RequisitionId,SourceStatus,RequisitionStatus,NewStatus,RejectionKind,RecruitmentApplicationId")
$counts = @{}
foreach ($row in $rows) {
    if ([string]::IsNullOrWhiteSpace($row)) { continue }
    $p = $row -split '\|'
    if ($p.Count -lt 9) { continue }
    $source = $sourceNames[$p[4]]
    $target = $statusNames[$p[6]]
    $reportLines += "$($p[0]),$($p[1]),$($p[2]),$($p[3]),$source,$($p[5]),$target,$($p[7]),$($p[8])"
    $key = "$($p[0]) $source [$($p[5])] -> $target$(if ($p[7]) { " ($($p[7]))" })"
    $counts[$key] = 1 + [int]$counts[$key]
}

$reportLines | Set-Content -Path $reportPath -Encoding utf8

Write-Host ("HCM-030 unified intake backfill - " + $(if ($isDryRun) { "DRY RUN (rolled back)" } else { "APPLIED" }))
foreach ($key in ($counts.Keys | Sort-Object)) {
    Write-Host ("  {0,-70} {1,5}" -f $key, $counts[$key])
}
Write-Host "  Total linked: $($reportLines.Count - 1)"
Write-Host "  Report:       $reportPath"

exit 0
