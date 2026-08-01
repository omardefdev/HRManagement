# NFR-002: remove toxics and re-enable proxy.
param(
    [ValidateSet('pg', 'rabbit', 'gateway', 'all')]
    [string]$Target = 'all',
    [string]$Api = $(if ($env:TOXIPROXY_URL) { $env:TOXIPROXY_URL } else { 'http://localhost:8474' }),
    [string]$ResultsRoot = 'chaos/results'
)

$ErrorActionPreference = 'Stop'
$targets = if ($Target -eq 'all') { @('pg', 'rabbit', 'gateway') } else { @($Target) }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $ResultsRoot "$stamp-restore-$Target"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

foreach ($proxy in $targets) {
    try {
        $toxics = Invoke-RestMethod -Uri "$Api/proxies/$proxy/toxics" -Method Get
        foreach ($t in $toxics) {
            Invoke-RestMethod -Uri "$Api/proxies/$proxy/toxics/$($t.name)" -Method Delete | Out-Null
        }
        $meta = Invoke-RestMethod -Uri "$Api/proxies/$proxy" -Method Get
        $body = @{
            name     = $proxy
            listen   = $meta.listen
            upstream = $meta.upstream
            enabled  = $true
        } | ConvertTo-Json -Compress
        Invoke-RestMethod -Uri "$Api/proxies/$proxy" -Method Post -ContentType 'application/json' -Body $body | Out-Null
        Write-Host "Restored $proxy"
    }
    catch {
        Write-Warning "Skip $proxy : $_"
    }
}

@{ atUtc = (Get-Date).ToUniversalTime().ToString('o'); target = $Target; status = 'restored' } |
    ConvertTo-Json | Set-Content (Join-Path $runDir 'summary.json') -Encoding UTF8
Write-Host "Restore notes → $runDir"
