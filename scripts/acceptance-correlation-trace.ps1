# AC-15: prove one correlation across gateway audit/logs and the asynchronous consumer side effect.
param(
    [Parameter(Mandatory = $true)]
    [string]$CorrelationId,
    [Parameter(Mandatory = $true)]
    [string]$EmployeeId,
    [Parameter(Mandatory = $true)]
    [string]$MessageId,
    [int]$SinceMinutes = 10,
    [string]$OutputPath = "",
    [string]$ComposeProjectName = $(if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "hrm-acceptance" }),
    [string]$PostgresContainer = "",
    [string]$GatewayDb = "gateway_audit_db",
    [string]$AuthDb = "auth_directory_db"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
        $env:ACCEPTANCE_EVIDENCE_DIR
    } else {
        Join-Path $root "artifacts/acceptance/evidence"
    }
    $OutputPath = Join-Path $evidenceRoot "AC-15/correlation-trace.json"
}

function Resolve-ComposeServiceContainer([string]$Service, [string]$Override = "") {
    if (-not [string]::IsNullOrWhiteSpace($Override)) { return $Override }
    $container = docker ps `
        --filter "label=com.docker.compose.project=$ComposeProjectName" `
        --filter "label=com.docker.compose.service=$Service" `
        --format "{{.ID}}" | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($container)) {
        throw "Compose service '$Service' is not running in project '$ComposeProjectName'."
    }
    return $container.Trim()
}

$postgres = Resolve-ComposeServiceContainer "postgresql" $PostgresContainer
$gateway = Resolve-ComposeServiceContainer "hr-gateway"
$hrCore = Resolve-ComposeServiceContainer "hr-core-web"
$contracts = Resolve-ComposeServiceContainer "hr-contracts-web"
$auth = Resolve-ComposeServiceContainer "auth-directory-web"

function Test-DockerLogHop([string]$Container, [string]$Needle) {
    $since = (Get-Date).AddMinutes(-1 * $SinceMinutes).ToUniversalTime().ToString("o")
    $logs = docker logs $Container --since $since 2>&1 | Out-String
    return $logs -match [regex]::Escape($Needle)
}

function Invoke-PsqlCount([string]$Database, [string]$Sql) {
    $raw = $Sql | docker exec -i $postgres psql -X -v ON_ERROR_STOP=1 -U hrm_ci -d $Database -t -A 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL correlation verification failed on $Database`: $($raw | Out-String)"
    }
    return [int](($raw | Out-String).Trim())
}

$auditSql = 'SELECT COUNT(*) FROM gateway_audit_entries WHERE "CorrelationId" = ''{0}'';' -f $CorrelationId
$authUserSql = 'SELECT COUNT(*) FROM "AuthUsers" WHERE "EmployeeId" = ''{0}'';' -f $EmployeeId
$receiptSql = 'SELECT COUNT(*) FROM "IntegrationMessageReceipts" WHERE "MessageId" = ''{0}'';' -f $MessageId

$hops = @(
    [ordered]@{ name = "gateway-audit"; found = (Invoke-PsqlCount $GatewayDb $auditSql) -ge 2 },
    [ordered]@{ name = "gateway-log"; found = (Test-DockerLogHop $gateway $CorrelationId) },
    [ordered]@{ name = "hr-core-log"; found = (Test-DockerLogHop $hrCore $CorrelationId) },
    [ordered]@{ name = "hr-contracts-log"; found = (Test-DockerLogHop $contracts $CorrelationId) },
    [ordered]@{ name = "auth-directory-consumer-log"; found = (Test-DockerLogHop $auth $CorrelationId) },
    [ordered]@{ name = "auth-directory-side-effect"; found = (Invoke-PsqlCount $AuthDb $authUserSql) -eq 1 },
    [ordered]@{ name = "auth-directory-inbox-receipt"; found = (Invoke-PsqlCount $AuthDb $receiptSql) -eq 1 }
)

$foundCount = @($hops | Where-Object { $_.found }).Count
$ok = $foundCount -ge 5 `
    -and ($hops | Where-Object name -eq "gateway-audit").found `
    -and ($hops | Where-Object name -eq "auth-directory-consumer-log").found `
    -and ($hops | Where-Object name -eq "auth-directory-side-effect").found `
    -and ($hops | Where-Object name -eq "auth-directory-inbox-receipt").found

$report = [ordered]@{
    correlationId = $CorrelationId
    employeeId = $EmployeeId
    messageId = $MessageId
    sinceMinutes = $SinceMinutes
    foundCount = $foundCount
    requiredMin = 5
    ok = [bool]$ok
    hops = $hops
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}

$dir = Split-Path -Parent $OutputPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output ($report | ConvertTo-Json -Compress -Depth 6)
if (-not $ok) { exit 1 }
exit 0
