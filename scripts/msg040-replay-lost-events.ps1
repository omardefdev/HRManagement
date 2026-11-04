# PLAN-40 (D-40-4): lists, and on request replays, the events lost before the fix: executed promotions and delegations,
# finished custody workflows, and the org-unit re-sync. Dry run unless -Apply is given. Run it by hand, once, after the
# deploy; it is never automatic. The caller's tenant decides what is read: run it once per tenant, with a token of a user
# holding hr-admin in that tenant. The report holds ids and counts only.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/msg040-replay-lost-events.ps1 -DryRun
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/msg040-replay-lost-events.ps1 -Apply
#
# Authentication: -AccessToken or HRM_REPAIR_ACCESS_TOKEN (a bearer token), or -CookieHeader or HRM_REPAIR_COOKIE (a signed-in
# gateway session where the password grant is off). A second run after -Apply lists zero replayable events.
[CmdletBinding()]
param(
    [string]$GatewayUrl = $(if ($env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL } else { 'http://localhost:6200' }),
    [string]$AccessToken = $env:HRM_REPAIR_ACCESS_TOKEN,
    [string]$CookieHeader = $env:HRM_REPAIR_COOKIE,
    [switch]$DryRun,
    [switch]$Apply,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if ($DryRun -and $Apply) { throw 'Choose -DryRun or -Apply, not both.' }
if ([string]::IsNullOrWhiteSpace($AccessToken) -and [string]::IsNullOrWhiteSpace($CookieHeader)) {
    throw 'No credentials: set -AccessToken / HRM_REPAIR_ACCESS_TOKEN or -CookieHeader / HRM_REPAIR_COOKIE.'
}

$isDryRun = -not $Apply
$dryRunValue = if ($isDryRun) { 'true' } else { 'false' }
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path (Join-Path $root 'reports') "msg040-replay-$stamp.json"
}

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($AccessToken)) { $headers['Authorization'] = "Bearer $AccessToken" }

# Cookies go through a session: Windows PowerShell 5.1 drops a Cookie entry in -Headers without a word.
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
if (-not [string]::IsNullOrWhiteSpace($CookieHeader)) {
    $gatewayUri = [Uri]$GatewayUrl
    foreach ($part in ($CookieHeader -split ';\s*' | Where-Object { $_ })) {
        $pair = $part -split '=', 2
        $session.Cookies.Add($gatewayUri, (New-Object Net.Cookie($pair[0].Trim(), $pair[1], '/')))
    }
}

$targets = @(
    @{ Service = 'hr-movements'; Path = 'replay/promotions'; Replayable = $true },
    @{ Service = 'hr-movements'; Path = 'replay/delegations'; Replayable = $true },
    @{ Service = 'hr-core'; Path = 'replay/asset-outcomes'; Replayable = $true },
    @{ Service = 'hr-core'; Path = 'resync/org-units'; Replayable = $false }
)

# Every replay POST needs an antiforgery token, issued per service behind the same policy. The token is sent only as a
# header: it never reaches the report. The service scopes its cookie to its UI path (/ui/<service>) and replay goes through
# /api/<service>, so the session keeps a copy at /; the gateway forwards it to the service.
function Get-RepairHeaders([string]$Service) {
    $tokenUrl = '{0}/api/{1}/api/admin/messaging/antiforgery' -f $GatewayUrl.TrimEnd('/'), $Service
    $issued = Invoke-WebRequest -UseBasicParsing -Method Get -Uri $tokenUrl -Headers $headers -WebSession $session
    $antiforgery = $issued.Content | ConvertFrom-Json
    $cookie = [regex]::Match(($issued.Headers['Set-Cookie'] -join ','), '(\.AspNetCore\.Antiforgery\.[^=;,\s]+)=([^;,\s]+)')
    if ($cookie.Success) {
        $session.Cookies.Add([Uri]$GatewayUrl, (New-Object Net.Cookie($cookie.Groups[1].Value, $cookie.Groups[2].Value, '/')))
    }
    $postHeaders = $headers.Clone()
    $postHeaders[[string]$antiforgery.headerName] = [string]$antiforgery.requestToken
    $postHeaders
}

$results = @()
$lost = 0
$failure = $null
$serviceHeaders = @{}
try {
    foreach ($target in $targets) {
        if (-not $serviceHeaders.ContainsKey($target.Service)) { $serviceHeaders[$target.Service] = Get-RepairHeaders $target.Service }
        $url = '{0}/api/{1}/api/admin/messaging/{2}?dryRun={3}' -f $GatewayUrl.TrimEnd('/'), $target.Service, $target.Path, $dryRunValue
        $response = Invoke-RestMethod -Method Post -Uri $url -Headers $serviceHeaders[$target.Service] -WebSession $session -ContentType 'application/json' -Body '{}'
        $count = [int]$response.count
        if ($target.Replayable) { $lost += $count }
        $results += [pscustomobject]@{
            kind = [string]$response.kind
            dryRun = [bool]$response.dryRun
            count = $count
            ids = @($response.ids | ForEach-Object { [string]$_ })
        }
        Write-Host ('{0,-16} {1,6}  {2}' -f $response.kind, $count, $(if ($isDryRun) { 'would be published' } else { 'published' }))
    }
}
catch {
    $failure = [string]$_.Exception.Message
    throw
}
finally {
    # Written even when a later target fails: with -Apply the earlier ones are already published.
    $report = [pscustomobject]@{
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        gateway = $GatewayUrl
        dryRun = $isDryRun
        lostEvents = $lost
        failure = $failure
        results = $results
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
    [System.IO.File]::WriteAllText($ReportPath, ($report | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Report: $ReportPath"
}
