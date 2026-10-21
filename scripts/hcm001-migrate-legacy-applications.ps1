# HCM-001 Plan-5: backfill RequisitionId on legacy recruitment applications.
# Usage:
#   powershell -File scripts/hcm001-migrate-legacy-applications.ps1
#   powershell -File scripts/hcm001-migrate-legacy-applications.ps1 -ConnectionString "Host=localhost;..."

param(
    [string]$ConnectionString = "",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

if (-not $ConnectionString) {
    $ConnectionString = $env:HRCORE_DB_CONNECTION
    if (-not $ConnectionString) {
        $ConnectionString = "Host=localhost;Port=5432;Database=hr_core_db;Username=hr;Password=hr"
    }
}

$outDir = Join-Path $RepoRoot "artifacts/hcm001-migration"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$reportPath = Join-Path $outDir "legacy-applications-$(Get-Date -Format 'yyyyMMdd-HHmmss').csv"

function Invoke-HrCoreSql([string]$Sql) {
    $tmp = [IO.Path]::GetTempFileName()
    Set-Content -Path $tmp -Value $Sql -Encoding utf8
    try {
        & psql $ConnectionString -v ON_ERROR_STOP=1 -At -F '|' -f $tmp
        if ($LASTEXITCODE -ne 0) { throw "psql failed ($LASTEXITCODE)" }
    }
    finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

$rows = @(Invoke-HrCoreSql @'
SELECT a."Id", a."TenantId", trim(a."AppliedPosition") AS applied_position
FROM "RecruitmentApplications" a
WHERE a."RequisitionId" IS NULL
  AND coalesce(a."IsLegacyUnlinked", false) = false;
'@)

$linked = 0
$flagged = 0
$skipped = 0
$reportLines = @("ApplicationId,TenantId,AppliedPosition,Action,RequisitionId,Notes")

foreach ($row in $rows) {
    if ([string]::IsNullOrWhiteSpace($row)) { continue }
    $parts = $row -split '\|', 3
    if ($parts.Count -lt 3) { continue }

    $appId = $parts[0]
    $tenantId = $parts[1]
    $applied = ($parts[2] -replace "'", "''").Trim()

    if ([string]::IsNullOrWhiteSpace($applied)) {
        Invoke-HrCoreSql "UPDATE ""RecruitmentApplications"" SET ""IsLegacyUnlinked"" = true WHERE ""Id"" = '$appId';"
        $flagged++
        $reportLines += "$appId,$tenantId,,$applied,FLAG_EMPTY,No applied position text"
        continue
    }

    $matches = @(Invoke-HrCoreSql @"
SELECT r.""Id""
FROM ""RecruitmentRequisitions"" r
WHERE r.""TenantId"" = '$tenantId'
  AND r.""Status"" = 'Published'
  AND lower(trim(r.""JobTitle"")) = lower('$applied')
ORDER BY r.""RequestedDate"" DESC;
"@)

    $matchIds = @($matches | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($matchIds.Count -eq 1) {
        $reqId = $matchIds[0]
        Invoke-HrCoreSql "UPDATE ""RecruitmentApplications"" SET ""RequisitionId"" = '$reqId' WHERE ""Id"" = '$appId';"
        $linked++
        $reportLines += "$appId,$tenantId,$applied,LINKED,$reqId,Single published match"
    }
    else {
        Invoke-HrCoreSql "UPDATE ""RecruitmentApplications"" SET ""IsLegacyUnlinked"" = true WHERE ""Id"" = '$appId';"
        $flagged++
        $reason = if ($matchIds.Count -eq 0) { "No published match" } else { "Ambiguous $($matchIds.Count) matches" }
        $reportLines += "$appId,$tenantId,$applied,FLAGGED,,$reason"
    }
}

$reportLines | Set-Content -Path $reportPath -Encoding utf8

Write-Host "Legacy migration complete."
Write-Host "  Linked:  $linked"
Write-Host "  Flagged: $flagged"
Write-Host "  Report:  $reportPath"

exit 0
