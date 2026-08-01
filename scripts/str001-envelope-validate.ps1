# STR-001: validate envelope-matrix.json against required indicators and floors.
# Exit 1 on missing fields, missing ids, floor regressions, or syria without ASS-SYR-CENSUS.
# Usage:
#   powershell -File scripts/str001-envelope-validate.ps1

param(
    [string]$MatrixPath = "docs/str-scope/envelope-matrix.json",
    [string]$RequiredPath = "testdata/str001/required-indicators.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str001/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$matrixFull = Join-Path $RepoRoot $MatrixPath
$reqFull = Join-Path $RepoRoot $RequiredPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }
if (-not (Test-Path $reqFull)) { Write-Error "required list not found: $reqFull" }

$matrix = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$req = Get-Content -Raw -Path $reqFull | ConvertFrom-Json
$failures = @()

if ($matrix.card -ne "STR-001") { $failures += "card must be STR-001" }

$fields = @($matrix.requiredIndicatorFields)
$tiers = @($matrix.allowedSourceTiers)
$envs = @($matrix.allowedEnvelopes)

$byId = @{}
foreach ($ind in $matrix.indicators) {
    if ($byId.ContainsKey($ind.id)) { $failures += "duplicate indicator $($ind.id)" }
    $byId[$ind.id] = $ind
    foreach ($f in $fields) {
        $val = $ind.$f
        if ($null -eq $val -or ($val -is [string] -and [string]::IsNullOrWhiteSpace($val))) {
            $failures += "$($ind.id) missing $f"
        }
    }
    if ($tiers -notcontains $ind.sourceTier) { $failures += "$($ind.id) bad sourceTier $($ind.sourceTier)" }
    if ($envs -notcontains $ind.envelope) { $failures += "$($ind.id) bad envelope $($ind.envelope)" }
}

foreach ($id in $req.requiredIndicatorIds) {
    if (-not $byId.ContainsKey($id)) { $failures += "missing required indicator $id" }
}

$syrAssumptions = @()
foreach ($ind in $matrix.indicators) {
    if ($ind.envelope -eq "syria-interim" -and $ind.assumptionId) {
        $syrAssumptions += $ind.assumptionId
    }
}
if ($syrAssumptions -notcontains $req.requireSyr002Assumption) {
    $failures += "syria-interim must reference assumption $($req.requireSyr002Assumption)"
}

$blockerIds = @($matrix.blockers | ForEach-Object { $_.id })
if ($blockerIds -notcontains "SYR-002") { $failures += "blockers must include SYR-002" }

foreach ($prop in $req.floors.PSObject.Properties) {
    $id = $prop.Name
    $floor = [double]$prop.Value
    if (-not $byId.ContainsKey($id)) { continue }
    $raw = $byId[$id].value
    $num = 0.0
    if (-not [double]::TryParse([string]$raw, [ref]$num)) {
        $failures += "$id value not numeric for floor check"
        continue
    }
    if ($num -lt $floor) {
        $failures += "$id value $num below floor $floor (lowering forbidden without EXCEPTION)"
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-001"
    ok = ($failures.Count -eq 0)
    indicatorCount = $byId.Count
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

Write-Host "PASS: STR-001 envelope validate"
exit 0
