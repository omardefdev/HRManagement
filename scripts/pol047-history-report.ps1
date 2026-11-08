# PLAN-47 (P-3): list the policy rules rewritten in place before PLAN-47 (DeactivateRuleIfPresentAsync, names starting with
# the Arabic "[deactivated]" prefix). Read-only; nothing is changed.
# Usage (PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol047-history-report.ps1 [-Out artifacts/plan47/history-report.txt]
# Server (bash): docker exec -i hrm-postgresql sh -c 'psql -X -U "$POSTGRES_USER" -d hr_core_db -A -F " | "' < scripts/pol047-history-report.sql
param(
    [string]$Out = "",
    [string]$PostgresContainer = "",
    [string]$ComposeProjectName = $(if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "hrm-acceptance" }),
    [string]$DbUser = "hrm_ci",
    [string]$Database = "hr_core_db"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    $PostgresContainer = (& docker ps -q --filter "label=com.docker.compose.project=$ComposeProjectName" --filter "label=com.docker.compose.service=postgresql" | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($PostgresContainer)) { throw "The $ComposeProjectName PostgreSQL container is not running." }
}

$sql = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot "pol047-history-report.sql"))
$raw = $sql | docker exec -i $PostgresContainer psql -X -v ON_ERROR_STOP=1 -U $DbUser -d $Database -A -F " | "
if ($LASTEXITCODE -ne 0) { throw "The history query failed: $($raw | Out-String)" }
$text = ($raw | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($Out)) {
    $dir = Split-Path -Parent $Out
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Out, $text, (New-Object System.Text.UTF8Encoding($false)))
}
Write-Output $text
exit 0
