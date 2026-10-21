# AC-18: stop Auth.Directory, publish while its queue has no consumer, then prove catch-up.
param(
    [string]$ConsumerContainer = "",
    [Parameter(Mandatory = $true)]
    [string]$EmployeeId,
    [string]$TenantId = "",
    [string]$RabbitContainer = "",
    [string]$PostgresContainer = "",
    [string]$ComposeProjectName = $(if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "hrm-acceptance" }),
    [string]$AuthDb = "auth_directory_db",
    [int]$CatchUpTimeoutSec = 120
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
    $env:ACCEPTANCE_EVIDENCE_DIR
} else {
    Join-Path $root "artifacts/acceptance/evidence"
}
$evidenceDir = Join-Path $evidenceRoot "AC-18"
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

function Resolve-ComposeServiceContainer([string]$Service, [string]$Override = "") {
    if (-not [string]::IsNullOrWhiteSpace($Override)) { return $Override }
    $container = docker ps -a `
        --filter "label=com.docker.compose.project=$ComposeProjectName" `
        --filter "label=com.docker.compose.service=$Service" `
        --format "{{.ID}}" | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($container)) {
        throw "Compose service '$Service' was not found in project '$ComposeProjectName'."
    }
    return $container.Trim()
}

$consumer = Resolve-ComposeServiceContainer "auth-directory-web" $ConsumerContainer
$rabbit = Resolve-ComposeServiceContainer "rabbitmq" $RabbitContainer
$postgres = Resolve-ComposeServiceContainer "postgresql" $PostgresContainer
$queueName = "auth-directory-web-employee-created"

function Get-QueueDepth {
    $raw = docker exec $rabbit rabbitmqctl list_queues name messages 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Could not read RabbitMQ queues." }
    foreach ($line in ($raw -split "`n")) {
        if ($line -match "^\s*$([regex]::Escape($queueName))\s+(\d+)") {
            return [int]$Matches[1]
        }
    }
    throw "RabbitMQ queue '$queueName' was not found."
}

function Get-AuthUserCount {
    $tenantClause = if ($TenantId) { ' AND "TenantId" = ''{0}''' -f $TenantId } else { "" }
    $sql = 'SELECT COUNT(*) FROM "AuthUsers" WHERE "EmployeeId" = ''{0}''{1};' -f $EmployeeId, $tenantClause
    $raw = $sql | docker exec -i $postgres psql -X -v ON_ERROR_STOP=1 -U hrm_ci -d $AuthDb -t -A 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Could not query Auth.Directory: $($raw | Out-String)" }
    return [int](($raw | Out-String).Trim())
}

$beforeDepth = Get-QueueDepth
@{ queue = $queueName; queueDepth = $beforeDepth } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDir "queue-depth-before.json") -Encoding UTF8

$consumerStopped = $false
try {
    docker stop $consumer | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not stop Auth.Directory consumer." }
    $consumerStopped = $true
    Start-Sleep -Seconds 8

    $stoppedDepth = Get-QueueDepth
    @{ queue = $queueName; queueDepth = $stoppedDepth; consumerStopped = $true } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDir "queue-depth-stopped.json") -Encoding UTF8

    docker start $consumer | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restart Auth.Directory consumer." }
    $consumerStopped = $false

    $deadline = (Get-Date).AddSeconds($CatchUpTimeoutSec)
    $afterDepth = $stoppedDepth
    $authUsers = 0
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        $afterDepth = Get-QueueDepth
        $authUsers = Get-AuthUserCount
        if ($afterDepth -eq 0 -and $authUsers -eq 1) { break }
    }

    $ok = ($stoppedDepth -ge 1) -and ($afterDepth -eq 0) -and ($authUsers -eq 1)
    $report = [ordered]@{
        queue = $queueName
        employeeId = $EmployeeId
        tenantId = $TenantId
        queueDepthBefore = $beforeDepth
        queueDepthWhileStopped = $stoppedDepth
        queueDepthAfterRestart = $afterDepth
        authUserCount = $authUsers
        ok = $ok
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceDir "db-checks.json") -Encoding UTF8
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceDir "result.json") -Encoding UTF8
    Write-Output ($report | ConvertTo-Json -Compress -Depth 4)
    if (-not $ok) { exit 1 }
}
finally {
    if ($consumerStopped) {
        docker start $consumer | Out-Null
    }
}
exit 0
