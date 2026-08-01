# STR-004: runtime/production nofork binding validate.
# Usage:
#   powershell -File scripts/str004-nofork-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/str-product/nofork-matrix.json",
    [string]$AgenciesDir = "testdata/str004/agencies",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str004/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()
$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }
$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json

if ($m.card -ne "STR-004") { $failures += "card must be STR-004" }
if (-not $m.releaseDigest) { $failures += "matrix releaseDigest missing" }

$prod001 = @($m.blockers | Where-Object { $_.id -eq "PROD-001" } | Select-Object -First 1)
if ($prod001.Count -eq 0) { $failures += "blocker PROD-001 missing" }
elseif ([string]$prod001[0].status -ne "Satisfied") { $failures += "blocker PROD-001 must be Satisfied" }

$onb001 = @($m.blockers | Where-Object { $_.id -eq "ONB-001" } | Select-Object -First 1)
if ($onb001.Count -eq 0) { $failures += "blocker ONB-001 missing" }
elseif ([string]$onb001[0].status -ne "Satisfied") { $failures += "blocker ONB-001 must be Satisfied" }

$packsDir = Resolve-RepoPath $AgenciesDir
$packs = @()
if (-not (Test-Path $packsDir)) {
    $failures += "agencies dir missing"
}
else {
    foreach ($f in Get-ChildItem -Path $packsDir -Filter "*.json") {
        $p = Get-Content -Raw -Path $f.FullName | ConvertFrom-Json
        $packs += $p
        foreach ($req in @("packId", "agencyId", "releaseDigest", "tenantId", "rollbackRef")) {
            if (-not $p.$req) { $failures += "$($f.Name) missing $req" }
        }
        if ($p.releaseDigest -ne $m.releaseDigest) {
            $failures += "$($f.Name) digest $($p.releaseDigest) != matrix $($m.releaseDigest)"
        }
    }
}

if ($packs.Count -ge 2) {
    $digests = @($packs | ForEach-Object { $_.releaseDigest } | Select-Object -Unique)
    if ($digests.Count -gt 1) { $failures += "agency packs must share one releaseDigest" }
}

# Forbidden code patterns
$scanRoots = @(
    (Resolve-RepoPath "src/Gateway"),
    (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults"),
    (Resolve-RepoPath "src/Services")
)
foreach ($pattern in @($m.forbiddenCodePatterns)) {
    foreach ($root in $scanRoots) {
        if (-not (Test-Path $root)) { continue }
        $hits = Get-ChildItem -Path $root -Recurse -Include *.cs -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
            Select-String -Pattern $pattern -SimpleMatch:$false -ErrorAction SilentlyContinue
        foreach ($hit in @($hits)) {
            $failures += "forbidden pattern /$pattern/ at $($hit.Path):$($hit.LineNumber)"
        }
    }
}

$prodFiles = @(
    "src/Gateway/HRManagement.Gateway/appsettings.Production.json",
    "src/Gateway/HRManagement.Gateway/appsettings.Staging.json"
)
foreach ($rel in $prodFiles) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { $failures += "missing $rel"; continue }
    $cfg = Get-Content -Raw -Path $full | ConvertFrom-Json
    $nf = $cfg.NoFork
    if ($null -eq $nf) { $failures += "$rel missing NoFork"; continue }
    if ($nf.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($nf.ResolveAgencyPacks -ne $true) { $failures += "$rel ResolveAgencyPacks must be true" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-004"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    packCount = $packs.Count
    releaseDigest = $m.releaseDigest
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "runtime-validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: STR-004 nofork runtime validate"
exit 0
