# STR-003: validate module-catalog.json readiness, pilot slugs, ops join.
# Usage:
#   powershell -File scripts/str003-module-catalog-validate.ps1

param(
    [string]$CatalogPath = "docs/str-modules/module-catalog.json",
    [string]$RequiredPath = "testdata/str003/required-modules.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str003/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$catalogFull = Join-Path $RepoRoot $CatalogPath
$reqFull = Join-Path $RepoRoot $RequiredPath
if (-not (Test-Path $catalogFull)) { Write-Error "catalog not found: $catalogFull" }
if (-not (Test-Path $reqFull)) { Write-Error "required not found: $reqFull" }

$cat = Get-Content -Raw -Path $catalogFull | ConvertFrom-Json
$req = Get-Content -Raw -Path $reqFull | ConvertFrom-Json
$failures = @()

if ($cat.card -ne "STR-003") { $failures += "card must be STR-003" }

$fields = @($cat.requiredModuleFields)
$byId = @{}
$exceptionModuleIds = @{}
foreach ($ex in @($cat.exceptions)) {
    if ($ex.moduleId) { $exceptionModuleIds[[string]$ex.moduleId] = $true }
}

$opsIds = @{}
$opsPath = Join-Path $RepoRoot $req.opsCatalogPath
if (Test-Path $opsPath) {
    $ops = Get-Content -Raw -Path $opsPath | ConvertFrom-Json
    foreach ($s in $ops.services) { $opsIds[$s.id] = $true }
}
else {
    $failures += "ops catalog missing: $($req.opsCatalogPath)"
}

foreach ($m in $cat.modules) {
    if ($byId.ContainsKey($m.moduleId)) { $failures += "duplicate $($m.moduleId)" }
    $byId[$m.moduleId] = $m
    foreach ($f in $fields) {
        $val = $m.$f
        if ($null -eq $val) { $failures += "$($m.moduleId) missing $f"; continue }
        if ($f -in @("personas", "capabilityIds", "dependsOn")) { continue }
        if ($val -is [string] -and [string]::IsNullOrWhiteSpace($val)) {
            $failures += "$($m.moduleId) empty $f"
        }
    }
    if ($cat.allowedAvailability -notcontains $m.availability) {
        $failures += "$($m.moduleId) bad availability"
    }
    if ($cat.allowedServiceTiers -notcontains $m.serviceTier) {
        $failures += "$($m.moduleId) bad serviceTier"
    }
    if ($cat.allowedReadiness -notcontains $m.readinessStatus) {
        $failures += "$($m.moduleId) bad readinessStatus"
    }

    if ($m.readinessStatus -eq "Ready") {
        foreach ($refName in @("uatPackRef", "runbookRef", "sloRef")) {
            $rel = [string]$m.$refName
            if ([string]::IsNullOrWhiteSpace($rel)) {
                $failures += "$($m.moduleId) Ready missing $refName"
                continue
            }
            $full = Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
            if (-not (Test-Path $full)) {
                $failures += "$($m.moduleId) Ready $refName file missing: $rel"
            }
        }
    }

    if ($req.requireOpsJoinForAppModules -and ($m.serviceTier -ne "platform")) {
        if (-not $opsIds.ContainsKey([string]$m.opsCatalogId)) {
            $failures += "$($m.moduleId) opsCatalogId $($m.opsCatalogId) not in REL-003 catalog"
        }
    }
}

foreach ($id in $req.requiredAppModuleIds) {
    if (-not $byId.ContainsKey($id)) { $failures += "missing app module $id" }
}
foreach ($id in $req.requiredPlatformModuleIds) {
    if (-not $byId.ContainsKey($id)) { $failures += "missing platform module $id" }
}

foreach ($slug in $cat.pilotApprovedSlugs) {
    if (-not $byId.ContainsKey($slug)) {
        $failures += "pilot slug missing from catalog: $slug"
        continue
    }
    $m = $byId[$slug]
    if ($m.readinessStatus -ne "Ready" -and -not $exceptionModuleIds.ContainsKey($slug)) {
        $failures += "pilot slug $slug is $($m.readinessStatus) without exception"
    }
}

$journeyIds = @($cat.journeys | ForEach-Object { $_.id })
foreach ($need in @("ModuleInventory", "ReadinessGate", "AgencyContract", "FlagExposureAudit")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-003"
    ok = ($failures.Count -eq 0)
    moduleCount = $byId.Count
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: STR-003 module catalog validate"
exit 0
