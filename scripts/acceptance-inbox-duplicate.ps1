# PLAN-05 AC-16: verify duplicate MessageId => single inbox receipt + single side effect.
# Usage:
#   pwsh scripts/acceptance-inbox-duplicate.ps1 -MessageId guid -EmployeeId guid -TenantId guid

param(
    [Parameter(Mandatory = $true)]
    [string]$MessageId,
    [Parameter(Mandatory = $true)]
    [string]$EmployeeId,
    [Parameter(Mandatory = $true)]
    [string]$TenantId,
    [string]$PostgresContainer = "",
    [string]$ComposeProjectName = $(if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "hrm-acceptance" }),
    [string]$AuthDb = "auth_directory_db"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    . "$PSScriptRoot/acceptance-local-env.ps1"
    Import-AcceptanceLocalEnvironment -RepoRoot $root
    $composeFiles = @(
        "docker-compose.yml",
        "docker-compose.services.yml",
        "docker-compose.dev-profiles.yml",
        "docker-compose.full-stack-gateway.yml",
        "docker-compose.acceptance.yml"
    )
    $composeArgs = @()
    foreach ($file in $composeFiles) { $composeArgs += @("-f", (Join-Path $root $file)) }
    $PostgresContainer = (& docker compose -p $ComposeProjectName @composeArgs ps -q postgresql | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
        throw "The $ComposeProjectName PostgreSQL container is not running."
    }
}

function Invoke-PsqlCount([string]$Sql) {
    $raw = $Sql | docker exec -i $PostgresContainer psql -X -v ON_ERROR_STOP=1 -U hrm_ci -d $AuthDb -t -A 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL verification query failed: $($raw | Out-String)"
    }
    return [int](($raw | Out-String).Trim())
}

$receiptsSql = 'SELECT COUNT(*) FROM "IntegrationMessageReceipts" WHERE "MessageId" = ''{0}'' AND "TenantId" = ''{1}'';' -f $MessageId, $TenantId
$authUsersSql = 'SELECT COUNT(*) FROM "AuthUsers" WHERE "EmployeeId" = ''{0}'' AND "TenantId" = ''{1}'';' -f $EmployeeId, $TenantId
$receipts = Invoke-PsqlCount $receiptsSql
$authUsers = Invoke-PsqlCount $authUsersSql

$ok = ($receipts -eq 1) -and ($authUsers -eq 1)
$report = [ordered]@{
    messageId = $MessageId
    employeeId = $EmployeeId
    tenantId = $TenantId
    receiptCount = $receipts
    authUserCount = $authUsers
    ok = $ok
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}

$evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
    $env:ACCEPTANCE_EVIDENCE_DIR
} else {
    Join-Path $root "artifacts/acceptance/evidence"
}
$outPath = Join-Path $evidenceRoot "AC-16/db-checks.json"
New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null
$report | ConvertTo-Json -Depth 4 | Set-Content -Path $outPath -Encoding UTF8
Write-Host ($report | ConvertTo-Json -Depth 4)

if (-not $ok) { exit 1 }
Write-Host "PASS: inbox duplicate single effect"
exit 0
