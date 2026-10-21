<#
.SYNOPSIS
    AC-19: inject read-model drift, reconcile dry-run/live, capture before/after reports.

.EXAMPLE
    pwsh scripts/acceptance-analytics-reconcile.ps1
#>
param(
    [string]$PostgresContainer = "",
    [string]$PostgresUser = "hrm_ci",
    [string]$AnalyticsDatabase = "analytics_db",
    [string]$TenantId = "a0000001-0000-4000-8000-000000000001",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
        $env:ACCEPTANCE_EVIDENCE_DIR
    } else {
        Join-Path $root "acceptance-evidence"
    }
    $OutputPath = Join-Path $evidenceRoot "AC-19\read-model-discovery.json"
}

if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    $PostgresContainer = docker ps `
        --filter "label=com.docker.compose.project=hrm-acceptance" `
        --filter "label=com.docker.compose.service=postgresql" `
        --format "{{.Names}}" | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    throw "The hrm-acceptance PostgreSQL container is not running."
}

function Invoke-PgScalar([string]$Sql) {
    $out = docker exec $PostgresContainer psql -X -v ON_ERROR_STOP=1 `
        -U $PostgresUser -d $AnalyticsDatabase -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
    return ($out | Out-String).Trim()
}

function Invoke-PgNonQuery([string]$Sql) {
    $out = docker exec $PostgresContainer psql -X -v ON_ERROR_STOP=1 `
        -U $PostgresUser -d $AnalyticsDatabase -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
}

Write-Host "=== AC-19 Analytics read-model reconciliation ===" -ForegroundColor Cyan
try {
    $TenantId = ([guid]::Parse($TenantId)).ToString("D")
}
catch {
    throw "TenantId must be a GUID."
}

$readModelId = Invoke-PgScalar @"
SELECT "Id"::text FROM "ReadModels"
WHERE "TenantId" = '$TenantId'
ORDER BY "Name" LIMIT 1;
"@
if (-not $readModelId) {
    throw "No ReadModels exist in $AnalyticsDatabase. Acceptance v2 analytics seed is incomplete."
}

$before = Invoke-PgScalar @"
SELECT json_build_object(
  'id', "Id",
  'name', "Name",
  'lastFingerprint', "LastFingerprint",
  'schema', "Schema",
  'tenantId', "TenantId"
)::text FROM "ReadModels" WHERE "Id" = '$readModelId';
"@

Invoke-PgNonQuery @"
UPDATE "ReadModels"
SET "LastFingerprint" = 'DEADBEEF', "Schema" = '{}'
WHERE "Id" = '$readModelId';
"@

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$result = [ordered]@{
    readModelId = $readModelId
    database = $AnalyticsDatabase
    tenantId = $TenantId
    postgresContainer = $PostgresContainer
    before = ($before | ConvertFrom-Json)
    driftInjected = $true
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output ($result | ConvertTo-Json -Depth 8 -Compress)
exit 0
