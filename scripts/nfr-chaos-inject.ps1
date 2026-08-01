# NFR-002: add/replace toxics on a Toxiproxy proxy.
# Usage:
#   powershell -File scripts/nfr-chaos-inject.ps1 -Target pg -Fault latency -LatencyMs 2000
#   powershell -File scripts/nfr-chaos-inject.ps1 -Target rabbit -Fault down
#   powershell -File scripts/nfr-chaos-inject.ps1 -Target pg -Fault loss -LossPercent 20
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('pg', 'rabbit', 'gateway')]
    [string]$Target,

    [Parameter(Mandatory = $true)]
    [ValidateSet('latency', 'down', 'loss', 'timeout', 'reset')]
    [string]$Fault,

    [int]$LatencyMs = 2000,
    [int]$LossPercent = 15,
    [int]$TimeoutMs = 5000,
    [string]$Api = $(if ($env:TOXIPROXY_URL) { $env:TOXIPROXY_URL } else { 'http://localhost:8474' }),
    [string]$ResultsRoot = 'chaos/results'
)

$ErrorActionPreference = 'Stop'
$proxy = $Target
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $ResultsRoot "$stamp-inject-$Target-$Fault"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

function Invoke-ProxyApi([string]$Method, [string]$Path, $Body = $null) {
    $uri = "$Api$Path"
    if ($null -eq $Body) {
        return Invoke-RestMethod -Uri $uri -Method $Method
    }
    $json = $Body | ConvertTo-Json -Depth 6 -Compress
    return Invoke-RestMethod -Uri $uri -Method $Method -ContentType 'application/json' -Body $json
}

# Ensure proxy exists (bootstrap may have created it)
$listen = switch ($Target) {
    'pg' { '0.0.0.0:15432' }
    'rabbit' { '0.0.0.0:15672' }
    'gateway' { '0.0.0.0:15000' }
}
$upstream = switch ($Target) {
    'pg' { 'postgresql:5432' }
    'rabbit' { 'rabbitmq:5672' }
    'gateway' { 'gateway:8080' }
}

try {
    Invoke-ProxyApi Get "/proxies/$proxy" | Out-Null
}
catch {
    Write-Host "Creating proxy $proxy ..."
    try {
        Invoke-ProxyApi Post '/proxies' @{
            name     = $proxy
            listen   = $listen
            upstream = $upstream
            enabled  = $true
        } | Out-Null
    }
    catch {
        Write-Warning "Proxy create may have failed (already exists?): $_"
    }
}

# Clear existing toxics first for deterministic inject
try {
    $toxics = Invoke-ProxyApi Get "/proxies/$proxy/toxics"
    foreach ($t in $toxics) {
        Invoke-ProxyApi Delete "/proxies/$proxy/toxics/$($t.name)" | Out-Null
    }
}
catch { }

switch ($Fault) {
    'reset' {
        Invoke-ProxyApi Post "/proxies/$proxy" @{
            name     = $proxy
            listen   = $listen
            upstream = $upstream
            enabled  = $true
        } | Out-Null
    }
    'down' {
        Invoke-ProxyApi Post "/proxies/$proxy" @{
            name     = $proxy
            listen   = $listen
            upstream = $upstream
            enabled  = $false
        } | Out-Null
    }
    'latency' {
        Invoke-ProxyApi Post "/proxies/$proxy" @{ name = $proxy; listen = $listen; upstream = $upstream; enabled = $true } | Out-Null
        Invoke-ProxyApi Post "/proxies/$proxy/toxics" @{
            name       = 'nfr_latency'
            type       = 'latency'
            stream     = 'downstream'
            toxicity   = 1.0
            attributes = @{ latency = $LatencyMs; jitter = [Math]::Max(50, [int]($LatencyMs / 10)) }
        } | Out-Null
    }
    'timeout' {
        Invoke-ProxyApi Post "/proxies/$proxy" @{ name = $proxy; listen = $listen; upstream = $upstream; enabled = $true } | Out-Null
        Invoke-ProxyApi Post "/proxies/$proxy/toxics" @{
            name       = 'nfr_timeout'
            type       = 'timeout'
            stream     = 'downstream'
            toxicity   = 1.0
            attributes = @{ timeout = $TimeoutMs }
        } | Out-Null
    }
    'loss' {
        Invoke-ProxyApi Post "/proxies/$proxy" @{ name = $proxy; listen = $listen; upstream = $upstream; enabled = $true } | Out-Null
        Invoke-ProxyApi Post "/proxies/$proxy/toxics" @{
            name       = 'nfr_loss'
            type       = 'slicer'
            stream     = 'downstream'
            toxicity   = [Math]::Min(1.0, $LossPercent / 100.0)
            attributes = @{ average_size = 64; size_variation = 32; delay = 10 }
        } | Out-Null
    }
}

$summary = [ordered]@{
    atUtc   = (Get-Date).ToUniversalTime().ToString('o')
    target  = $Target
    fault   = $Fault
    api     = $Api
    latencyMs = $LatencyMs
    status  = 'injected'
}
$summary | ConvertTo-Json | Set-Content (Join-Path $runDir 'summary.json') -Encoding UTF8
"Injected $Fault on $Target via $Api" | Set-Content (Join-Path $runDir 'notes.md') -Encoding UTF8
Write-Host "OK: $Fault on $Target → $runDir"
