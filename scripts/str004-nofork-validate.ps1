# STR-004: validate nofork-matrix, dual-agency digest, blockers, forbidden patterns.
# Usage:
#   powershell -File scripts/str004-nofork-validate.ps1

param(
    [string]$MatrixPath = "docs/str-product/nofork-matrix.json",
    [string]$RequiredPath = "testdata/str004/required-surfaces.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str004/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$matrixFull = Join-Path $RepoRoot $MatrixPath
$reqFull = Join-Path $RepoRoot $RequiredPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }
if (-not (Test-Path $reqFull)) { Write-Error "required not found: $reqFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$req = Get-Content -Raw -Path $reqFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "STR-004") { $failures += "card must be STR-004" }

$fields = @($m.requiredSurfaceFields)
$kindsSeen = @{}
foreach ($s in $m.surfaces) {
    foreach ($f in $fields) {
        if ($null -eq $s.$f -and $f -ne "prod001Dependency") {
            $failures += "$($s.surfaceId) missing $f"
        }
        elseif ($f -ne "prod001Dependency" -and $s.$f -is [string] -and [string]::IsNullOrWhiteSpace($s.$f)) {
            $failures += "$($s.surfaceId) empty $f"
        }
    }
    if ($m.allowedKinds -notcontains $s.kind) {
        $failures += "$($s.surfaceId) bad kind $($s.kind)"
    }
    $kindsSeen[$s.kind] = $true
}

if (@($m.surfaces).Count -lt [int]$req.minSurfaces) {
    $failures += "need >= $($req.minSurfaces) surfaces"
}
foreach ($k in $req.requiredKinds) {
    if (-not $kindsSeen.ContainsKey($k)) { $failures += "missing kind $k" }
}

$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($req.requireProd001Satisfied -and $blockerMap["PROD-001"] -ne "Satisfied") {
    $failures += "PROD-001 must be Satisfied"
}
if ($req.requireOnb001Blocked -and $blockerMap["ONB-001"] -ne "Blocked") {
    $failures += "ONB-001 must be Blocked"
}
if ($blockerMap["STR-002"] -ne "Satisfied") { $failures += "STR-002 should be Satisfied" }
if ($blockerMap["TEN-003"] -ne "Satisfied") { $failures += "TEN-003 should be Satisfied" }

$aPath = Join-Path $RepoRoot $m.agencyFixtures.agencyAPath
$bPath = Join-Path $RepoRoot $m.agencyFixtures.agencyBPath
if (-not (Test-Path $aPath)) { $failures += "missing agency-a fixture" }
if (-not (Test-Path $bPath)) { $failures += "missing agency-b fixture" }
if ((Test-Path $aPath) -and (Test-Path $bPath)) {
    $a = Get-Content -Raw -Path $aPath | ConvertFrom-Json
    $b = Get-Content -Raw -Path $bPath | ConvertFrom-Json
    if ($m.agencyFixtures.requireSameReleaseDigest) {
        if ($a.releaseDigest -ne $b.releaseDigest) {
            $failures += "agency fixtures must share releaseDigest"
        }
        if ($a.releaseDigest -ne $m.releaseDigest) {
            $failures += "fixture digest must match matrix releaseDigest"
        }
    }
    if ($a.agencyId -eq $b.agencyId) { $failures += "agencies must differ" }
}

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("ConfigTaxonomy", "ExtensionPoints", "DualAgencySameRelease", "PromotionRollback", "RepoNoForkRule")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

# Lightweight forbidden-pattern scan (sample roots)
foreach ($rootRel in $req.srcScanRoots) {
    $scanRoot = Join-Path $RepoRoot ($rootRel -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $scanRoot)) { continue }
    $files = Get-ChildItem -Path $scanRoot -Recurse -Include *.cs -File -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        $text = Get-Content -Raw -Path $file.FullName -ErrorAction SilentlyContinue
        if (-not $text) { continue }
        foreach ($pat in $m.forbiddenCodePatterns) {
            if ([regex]::IsMatch($text, $pat)) {
                $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
                $failures += "forbidden pattern /$pat/ in $rel"
            }
        }
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-004"
    ok = ($failures.Count -eq 0)
    surfaceCount = @($m.surfaces).Count
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

Write-Host "PASS: STR-004 nofork validate"
exit 0
