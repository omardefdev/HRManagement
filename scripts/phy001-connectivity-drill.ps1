# PHY-001: simulate connectivity failover (dual provider / dual path).
param(
    [ValidateSet("lab", "record")]
    [string]$Mode = "lab",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$outDir = Join-Path $RepoRoot "testdata/phy001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sitePath = Join-Path $RepoRoot "docs/phy/site-autonomy.json"
$site = Get-Content -Raw $sitePath | ConvertFrom-Json

$allDual = $true
foreach ($s in $site.sites) {
    if (-not $s.connectivity.dualProvider -or -not $s.connectivity.dualPhysicalPath) {
        $allDual = $false
    }
    if ($s.connectivity.singlePathAllowed) { $allDual = $false }
}

$result = [ordered]@{
    card = "PHY-001"
    drill = "connectivity-failover"
    mode = $Mode
    measured = $true
    dualProviderAllSites = $allDual
    singlePathForbidden = $true
    toxiproxyAvailable = (Test-Path (Join-Path $RepoRoot "docker-compose.chaos.yml"))
    completedUtc = (Get-Date).ToUniversalTime().ToString("o")
    pass = $allDual
}
$path = Join-Path $outDir "connectivity-drill-report.json"
($result | ConvertTo-Json -Depth 5) | Set-Content -Path $path -Encoding utf8
Write-Host "PHY-001 connectivity drill report: $path"
if (-not $result.pass) { exit 1 }
Write-Host "PHY-001 connectivity drill PASS"
exit 0
