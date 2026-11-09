# PLAN-47 (P-9): fingerprint every published policy version of HR.Core, and optionally compare with an earlier file.
# Usage (PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol047-policy-fingerprint.ps1 -Out artifacts/plan47/fingerprint-before.json
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol047-policy-fingerprint.ps1 -Out artifacts/plan47/fingerprint-after.json -Compare artifacts/plan47/fingerprint-before.json
# Server (bash): docker exec -i hrm-postgresql sh -c 'psql -X -U "$POSTGRES_USER" -d hr_core_db -At' < scripts/pol047-policy-fingerprint.sql
# Ids differ on every stack: compare only two files taken from the same database.
param(
    [string]$Out = "",
    [string]$Compare = "",
    [string]$InputJson = "",
    [string]$PostgresContainer = "",
    [string]$ComposeProjectName = $(if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "hrm-acceptance" }),
    [string]$DbUser = "hrm_ci",
    [string]$Database = "hr_core_db"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sqlPath = Join-Path $PSScriptRoot "pol047-policy-fingerprint.sql"

if ([string]::IsNullOrWhiteSpace($InputJson)) {
    if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
        $PostgresContainer = (& docker ps -q --filter "label=com.docker.compose.project=$ComposeProjectName" --filter "label=com.docker.compose.service=postgresql" | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
            throw "The $ComposeProjectName PostgreSQL container is not running."
        }
    }
    $sql = [System.IO.File]::ReadAllText($sqlPath)
    $raw = $sql | docker exec -i $PostgresContainer psql -X -v ON_ERROR_STOP=1 -U $DbUser -d $Database -t -A
    if ($LASTEXITCODE -ne 0) { throw "The fingerprint query failed: $($raw | Out-String)" }
    $json = ($raw | Out-String).Trim()
} else {
    $json = [System.IO.File]::ReadAllText((Resolve-Path $InputJson)).Trim()
}

$rows = @((ConvertFrom-Json -InputObject $json) | ForEach-Object { $_ })
if (-not [string]::IsNullOrWhiteSpace($Out)) {
    $dir = Split-Path -Parent $Out
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Out, $json, (New-Object System.Text.UTF8Encoding($false)))
}
Write-Host ("Published versions: {0}; rules: {1}" -f $rows.Count, (($rows | Measure-Object -Property RuleCount -Sum).Sum))

if (-not [string]::IsNullOrWhiteSpace($Compare)) {
    $before = @((ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText((Resolve-Path $Compare)))) | ForEach-Object { $_ })
    $now = @{}
    foreach ($r in $rows) { $now[[string]$r.VersionId] = $r }
    $changed = 0
    $missing = 0
    foreach ($b in $before) {
        $key = [string]$b.VersionId
        if (-not $now.ContainsKey($key)) {
            # A version that is no longer Published (none should be: published versions are never archived).
            Write-Host ("MISSING {0} {1} v{2}" -f $b.Domain, $key, $b.VersionNumber)
            $missing++
            continue
        }
        if ($now[$key].RulesSha256 -ne $b.RulesSha256) {
            Write-Host ("CHANGED {0} {1} v{2}" -f $b.Domain, $key, $b.VersionNumber)
            $changed++
        }
        $now.Remove($key)
    }
    foreach ($n in $now.Values) {
        Write-Host ("NEW {0} {1} v{2} rules={3}" -f $n.Domain, $n.VersionId, $n.VersionNumber, $n.RuleCount)
    }
    Write-Host ("Compared {0} earlier versions: changed={1} missing={2} new={3}" -f $before.Count, $changed, $missing, $now.Count)
    if ($changed -gt 0 -or $missing -gt 0) { exit 1 }
}
exit 0
