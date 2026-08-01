# STR-003: runtime/production module binding validate.
# Usage:
#   powershell -File scripts/str003-module-runtime-validate.ps1

param(
    [string]$CatalogPath = "docs/str-modules/module-catalog.json",
    [string]$GatewayCatalogCs = "src/Gateway/HRManagement.Gateway/Services/GatewayModuleCatalog.cs",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str003/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()
$catalogFull = Resolve-RepoPath $CatalogPath
if (-not (Test-Path $catalogFull)) { Write-Error "catalog not found: $catalogFull" }
$cat = Get-Content -Raw -Path $catalogFull | ConvertFrom-Json

if ($cat.card -ne "STR-003") { $failures += "card must be STR-003" }

$exceptionModules = @{}
foreach ($ex in @($cat.exceptions)) {
    if ($ex.moduleId -and [string]$ex.status -eq "Active") {
        $exceptionModules[[string]$ex.moduleId] = $true
    }
}

$byId = @{}
foreach ($m in @($cat.modules)) {
    $byId[[string]$m.moduleId] = $m
    if ([string]$m.readinessStatus -eq "Ready") {
        foreach ($refName in @("uatPackRef", "runbookRef", "sloRef")) {
            $rel = [string]$m.$refName
            if ([string]::IsNullOrWhiteSpace($rel)) {
                $failures += "$($m.moduleId) Ready missing $refName"
                continue
            }
            $full = Resolve-RepoPath $rel
            if (-not (Test-Path $full)) {
                $failures += "$($m.moduleId) Ready $refName missing: $rel"
            }
        }
    }
}

foreach ($slug in @($cat.pilotApprovedSlugs)) {
    if (-not $byId.ContainsKey([string]$slug)) {
        $failures += "pilot slug missing from modules: $slug"
        continue
    }
    $m = $byId[[string]$slug]
    $ready = [string]$m.readinessStatus -eq "Ready"
    $waived = $exceptionModules.ContainsKey([string]$slug)
    if (-not $ready -and -not $waived) {
        $failures += "pilot slug not Ready (and no Active exception): $slug"
    }
}

# Drift: GatewayModuleCatalog hardcoded slugs vs pilotApprovedSlugs
$csFull = Resolve-RepoPath $GatewayCatalogCs
if (-not (Test-Path $csFull)) {
    $failures += "missing GatewayModuleCatalog.cs"
}
else {
    $csText = Get-Content -Raw -Path $csFull
    $gatewaySlugs = [regex]::Matches($csText, 'new\("([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    $pilot = @($cat.pilotApprovedSlugs)
    foreach ($s in $pilot) {
        if ($gatewaySlugs -notcontains $s) {
            $failures += "pilot slug '$s' missing from GatewayModuleCatalog hardcoded list"
        }
    }
    foreach ($s in $gatewaySlugs) {
        if ($pilot -notcontains $s) {
            $failures += "GatewayModuleCatalog slug '$s' not in pilotApprovedSlugs"
        }
    }
    if ($gatewaySlugs.Count -ne 14) {
        $failures += "GatewayModuleCatalog expected 14 modules, found $($gatewaySlugs.Count)"
    }
}

$prodFiles = @(
    "src/Gateway/HRManagement.Gateway/appsettings.Production.json",
    "src/Gateway/HRManagement.Gateway/appsettings.Staging.json"
)

foreach ($rel in $prodFiles) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $failures += "missing $rel"
        continue
    }
    $cfg = Get-Content -Raw -Path $full | ConvertFrom-Json
    $mc = $cfg.ModuleCatalog
    if ($null -eq $mc) {
        $failures += "$rel missing ModuleCatalog section"
        continue
    }
    if ($mc.EnforceOnStartup -ne $true) {
        $failures += "$rel EnforceOnStartup must be true"
    }
    if ($rel -match "Gateway" -and $mc.EnforcePathEntitlement -ne $true) {
        $failures += "$rel EnforcePathEntitlement must be true for Gateway"
    }

    $enabled = @()
    if ($cfg.Gateway -and $cfg.Gateway.EnabledModules) {
        $enabled = @($cfg.Gateway.EnabledModules)
    }
    if ($enabled.Count -gt 0) {
        foreach ($slug in $enabled) {
            if (-not $byId.ContainsKey([string]$slug)) {
                $failures += "$rel EnabledModules unknown slug $slug"
                continue
            }
            $m = $byId[[string]$slug]
            if ([string]$m.readinessStatus -ne "Ready" -and -not $exceptionModules.ContainsKey([string]$slug)) {
                $failures += "$rel EnabledModules non-Ready $slug"
            }
        }
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-003"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    moduleCount = @($cat.modules).Count
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

Write-Host "PASS: STR-003 module runtime validate"
exit 0
