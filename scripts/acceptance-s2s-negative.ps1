# PLAN-11 AC-30: S2S file reference — bad token rejected, zero side effect.
# Usage: pwsh scripts/acceptance-s2s-negative.ps1 -FileId guid

param(
    [Parameter(Mandatory = $true)][string]$FileId,
    [string]$BaseUrl = $env:BASE_URL,
    [string]$KeycloakBase = $env:KEYCLOAK_BASE_URL,
    [string]$ValidClientId = "svc-hr-core",
    [string]$ValidClientSecret = $env:SVC_HR_CORE_CLIENT_SECRET,
    [string]$RogueClientId = "svc-payroll",
    [string]$RogueClientSecret = $env:SVC_PAYROLL_CLIENT_SECRET,
    [string]$PostgresContainer = "",
    [string]$CallerContainer = "",
    [string]$TenantId = "a0000001-0000-4000-8000-000000000001",
    [string]$FileDb = "file_storage_db"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BaseUrl)) { $BaseUrl = "http://localhost:6200" }
if ([string]::IsNullOrWhiteSpace($KeycloakBase)) { $KeycloakBase = "http://localhost:6388" }
if ([string]::IsNullOrWhiteSpace($ValidClientSecret)) { $ValidClientSecret = "dev-svc-secret-local-only" }
if ([string]::IsNullOrWhiteSpace($RogueClientSecret)) { $RogueClientSecret = "dev-svc-secret-local-only" }

$evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
    $env:ACCEPTANCE_EVIDENCE_DIR
} else {
    Join-Path $root "artifacts/acceptance/evidence"
}
$evidenceDir = Join-Path $evidenceRoot "AC-30"
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    $PostgresContainer = docker ps `
        --filter "label=com.docker.compose.project=hrm-acceptance" `
        --filter "label=com.docker.compose.service=postgresql" `
        --format "{{.Names}}" | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($CallerContainer)) {
    $CallerContainer = docker ps `
        --filter "label=com.docker.compose.project=hrm-acceptance" `
        --filter "label=com.docker.compose.service=hr-core-web" `
        --format "{{.Names}}" | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($CallerContainer)) {
    throw "The hrm-acceptance HR.Core container is not running."
}
if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
    throw "The hrm-acceptance PostgreSQL container is not running."
}

function Get-ServiceToken([string]$ClientId, [string]$Secret, [string]$Scope = "") {
    $form = @{
        grant_type = "client_credentials"
        client_id = $ClientId
        client_secret = $Secret
    }
    if (-not [string]::IsNullOrWhiteSpace($Scope)) { $form.scope = $Scope }
    $resp = Invoke-RestMethod -Method Post -Uri "$KeycloakBase/realms/hrmanagement/protocol/openid-connect/token" -Body $form
    return $resp.access_token
}

function Get-RefCount {
    $sql = "SELECT COUNT(*) FROM ""FileReferences"" WHERE ""FileId"" = '$FileId';"
    $raw = docker exec $PostgresContainer psql -U hrm_ci -d $FileDb -t -A -c $sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Failed to query File.Storage reference count: $raw" }
    return [int]($raw.Trim())
}

function Post-Reference([string]$Token, [string]$HolderKey) {
    $body = @{ holderKey = $HolderKey } | ConvertTo-Json -Compress
    $dockerArgs = @(
        "exec", $CallerContainer,
        "curl", "-sS", "-o", "/dev/null", "-w", "%{http_code}",
        "-X", "POST",
        "http://file-storage-web:8080/api/files/$FileId/references",
        "-H", "Authorization: Bearer $Token",
        "-H", "Content-Type: application/json",
        "-H", "X-Tenant-ID: $TenantId",
        "--data", $body
    )
    $raw = & docker @dockerArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Direct S2S request failed: $raw" }
    $statusText = ($raw | Out-String).Trim()
    if ($statusText -notmatch '^\d{3}$') { throw "Unexpected HTTP status output: $statusText" }
    return [int]$statusText
}

$before = Get-RefCount
$before | ConvertTo-Json | Set-Content (Join-Path $evidenceDir "before.json") -Encoding UTF8

$validToken = Get-ServiceToken $ValidClientId $ValidClientSecret "file-storage.references"
$validStatus = Post-Reference $validToken "HR.Core:Employee:ac30-script-valid:FileId"
$midCount = Get-RefCount

$rogueToken = Get-ServiceToken $RogueClientId $RogueClientSecret
$rejectStatus = Post-Reference $rogueToken "Payroll:Employee:ac30-script-rogue:FileId"
$after = Get-RefCount

@{ statusCode = $rejectStatus } | ConvertTo-Json | Set-Content (Join-Path $evidenceDir "http-reject.json") -Encoding UTF8
@{ referenceCountBefore = $before; referenceCountAfterValid = $midCount; referenceCountAfterReject = $after; validStatus = $validStatus; rejectStatus = $rejectStatus } |
    ConvertTo-Json | Set-Content (Join-Path $evidenceDir "after.json") -Encoding UTF8

$ok = ($validStatus -eq 200) -and ($rejectStatus -ge 400) -and ($after -eq $midCount)
if (-not $ok) {
    Write-Error "AC-30 FAIL valid=$validStatus reject=$rejectStatus counts before=$before mid=$midCount after=$after"
}
Write-Host "PASS AC-30 S2S negative"
exit 0
