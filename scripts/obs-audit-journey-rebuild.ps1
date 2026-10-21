# PLAN-13 AC-37: PowerShell wrapper for journey rebuild CLI.
param(
    [Parameter(Mandatory = $true)][string]$CorrelationId,
    [string]$GatewayUrl = $(if ($env:GATEWAY_URL) { $env:GATEWAY_URL } else { "http://localhost:5200" }),
    [string]$OutputPath = "journey.json",
    [string]$Token = $env:GATEWAY_TOKEN
)

$ErrorActionPreference = "Stop"
$headers = @{ Accept = "application/json" }
if ($Token) { $headers.Authorization = "Bearer $Token" }

$uri = "$GatewayUrl/api/platform/audit/journey/$CorrelationId"
try {
    $resp = Invoke-WebRequest -Method Get -Uri $uri -Headers $headers
    $resp.Content | Set-Content $OutputPath -Encoding UTF8
    Write-Host "HTTP $($resp.StatusCode) -> $OutputPath"
    exit 0
} catch {
    if ($_.Exception.Response) {
        $code = [int]$_.Exception.Response.StatusCode.value__
        Write-Error "Journey rebuild failed HTTP $code"
    }
    throw
}
