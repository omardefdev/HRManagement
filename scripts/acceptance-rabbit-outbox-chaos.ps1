# PLAN-05 AC-17: stop Rabbit, wait for outbox rows, restart, poll drain.
# Usage:
#   pwsh scripts/acceptance-rabbit-outbox-chaos.ps1 -PublisherDb payroll_db -OutboxTable OutboxMessage

param(
    [string]$RabbitContainer = "",
    [string]$PostgresContainer = "",
    [string]$PublisherDb = "payroll_db",
    [string]$OutboxTable = "OutboxMessage",
    [int]$DrainTimeoutSec = 120
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) { $env:ACCEPTANCE_EVIDENCE_DIR } else { Join-Path $root "acceptance-evidence" }
$evidenceDir = Join-Path $evidenceRoot "AC-17"
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

function Resolve-ComposeServiceContainer([string]$Service, [string]$Override = "") {
    if (-not [string]::IsNullOrWhiteSpace($Override)) { return $Override }
    $container = docker ps `
        --filter "label=com.docker.compose.project=hrm-acceptance" `
        --filter "label=com.docker.compose.service=$Service" `
        --format "{{.Names}}" | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($container)) {
        throw "The hrm-acceptance container for service '$Service' is not running."
    }
    return $container
}

$RabbitContainer = Resolve-ComposeServiceContainer "rabbitmq" $RabbitContainer
$PostgresContainer = Resolve-ComposeServiceContainer "postgresql" $PostgresContainer
if ($OutboxTable -notmatch '^[A-Za-z][A-Za-z0-9_]*$') {
    throw "Invalid outbox table name: $OutboxTable"
}

function Get-OutboxCount {
    $sql = 'SELECT COUNT(*) FROM "' + $OutboxTable + '";'
    $raw = docker exec $PostgresContainer psql -X -v ON_ERROR_STOP=1 -U hrm_ci -d $PublisherDb -t -A -c $sql 2>$null
    if ($LASTEXITCODE -ne 0) { return -1 }
    return [int]($raw.Trim())
}

$before = Get-OutboxCount
Write-Host "Outbox before stop: $before"

docker stop $RabbitContainer | Out-Null
Start-Sleep -Seconds 2
$stopped = $true

Write-Host "Rabbit stopped — publish now (test window)"
Start-Sleep -Seconds 5

$during = Get-OutboxCount
$dir = Join-Path $evidenceDir "db-checks"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
@{ phase = "during"; count = $during; rabbitStopped = $stopped } |
    ConvertTo-Json | Set-Content (Join-Path $dir "outbox-during.json")

docker start $RabbitContainer | Out-Null
$deadline = (Get-Date).AddSeconds($DrainTimeoutSec)
$after = $during
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $after = Get-OutboxCount
    if ($after -eq 0) { break }
}

$timing = @{
    drainTimeoutSec = $DrainTimeoutSec
    outboxBefore = $before
    outboxDuring = $during
    outboxAfter = $after
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}
$timing | ConvertTo-Json | Set-Content (Join-Path $evidenceDir "timing.json")

$ok = ($during -ge 1) -and ($after -eq 0)
@{ ok = $ok; timing = $timing } | ConvertTo-Json | Set-Content (Join-Path $evidenceDir "result.json")
Write-Host ($timing | ConvertTo-Json)

if (-not $ok) { exit 1 }
Write-Host "PASS: outbox survived Rabbit down and drained"
exit 0
