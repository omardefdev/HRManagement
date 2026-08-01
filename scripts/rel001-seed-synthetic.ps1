# REL-001: generate pilot-scale synthetic seed (no production dump).
# Usage:
#   pwsh scripts/rel001-seed-synthetic.ps1
#   pwsh scripts/rel001-seed-synthetic.ps1 -Employees 2000 -Apply

param(
    [int]$Employees = 0,
    [int]$OrgUnits = 0,
    [int]$Seed = 42,
    [string]$OutDir = "data/rel001-seed",
    [string]$TenantId = "11111111-1111-1111-1111-111111111111",
    [string]$ManifestPath = "testdata/rel001/manifest.json",
    [switch]$Apply,
    [string]$ProdDumpPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($ProdDumpPath -and $ProdDumpPath.Trim().Length -gt 0) {
    Write-Error "REL-001 forbids production dump paths (got: $ProdDumpPath). Use synthetic generator only."
}

$manifest = Get-Content -Raw (Join-Path $root $ManifestPath) | ConvertFrom-Json
if ($Employees -le 0) { $Employees = [int]$manifest.seed.employees }
if ($OrgUnits -le 0) { $OrgUnits = [int]$manifest.seed.orgUnits }
$max = [int]$manifest.policy.maxEmployeesWithoutApproval
if ($Employees -gt $max) {
    Write-Error "Employees=$Employees exceeds maxEmployeesWithoutApproval=$max (REL-001 policy)."
}

if ($manifest.policy.forbidProductionDump -ne $true) {
    Write-Error "Manifest must set forbidProductionDump=true"
}

Write-Host "REL-001 seed: employees=$Employees orgUnits=$OrgUnits out=$OutDir"
$nfrArgs = @{
    Employees = $Employees
    OrgUnits  = $OrgUnits
    Seed      = $Seed
    OutDir    = $OutDir
    TenantId  = $TenantId
}
if ($Apply) { $nfrArgs.Apply = $true }

& (Join-Path $root "scripts/nfr-seed-synthetic.ps1") @nfrArgs

$relMeta = [ordered]@{
    card           = "REL-001"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    employees      = $Employees
    orgUnits       = $OrgUnits
    tenantId       = $TenantId
    outDir         = $OutDir
    e2eManifest    = $manifest.extends.e2eManifest
    keycloakRel001 = $manifest.extends.keycloakRel001
    forbidProdDump = $true
}
$relMeta | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $OutDir "rel001-manifest.json") -Encoding UTF8
Write-Host "REL-001 synthetic seed ready at $OutDir"
