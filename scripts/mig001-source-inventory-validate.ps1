# MIG-001: validate source inventory, field SoT, volumes, access risks, fixtures.
# Usage:
#   powershell -File scripts/mig001-source-inventory-validate.ps1

param(
    [string]$MatrixPath = "docs/mig-inventory/source-inventory.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/mig001/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "MIG-001") { $failures += "card must be MIG-001" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("SourceCatalog", "FieldSot", "MeasuredVolumeQuality", "AccessRisks", "SamplingProfiling", "CoverageGate")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

# Sources
$sourceMap = @{}
$fields = @($m.requiredSourceFields)
$srcCount = 0
foreach ($s in $m.sources) {
    $srcCount++
    foreach ($f in $fields) {
        if ($null -eq $s.$f) { $failures += "source $($s.sourceId) missing $f" }
    }
    if ($m.allowedSourceTypes -notcontains $s.type) {
        $failures += "source $($s.sourceId) bad type $($s.type)"
    }
    if ($m.allowedSourceStatuses -notcontains $s.status) {
        $failures += "source $($s.sourceId) bad status $($s.status)"
    }
    if ($s.status -eq "Inventoried") {
        if (@($s.accessRisks).Count -lt 1) {
            $failures += "source $($s.sourceId) Inventoried without accessRisks"
        }
        if ($s.volume.estimateOnly -eq $true) {
            $failures += "source $($s.sourceId) Inventoried with estimateOnly"
        }
        if ([string]::IsNullOrWhiteSpace($s.volume.measuredAt) -or [string]::IsNullOrWhiteSpace($s.volume.method)) {
            $failures += "source $($s.sourceId) missing volume measuredAt/method"
        }
    }
    $sourceMap[$s.sourceId] = $s
}
if ($srcCount -lt 6) { $failures += "need >= 6 sources" }

# Universe coverage
$exBySource = @{}
foreach ($ex in $m.exceptions) {
    $exBySource[$ex.sourceId] = $ex
    if ([string]::IsNullOrWhiteSpace($ex.signedByRole) -or [string]::IsNullOrWhiteSpace($ex.expiresOn)) {
        $failures += "exception $($ex.exceptionId) missing signedByRole/expiresOn"
    }
}
foreach ($uid in @($m.knownSourceUniverse)) {
    if (-not $sourceMap.ContainsKey($uid)) {
        $failures += "universe source $uid missing from sources"
        continue
    }
    $s = $sourceMap[$uid]
    if ($s.status -eq "Unsupported" -or $s.status -eq "ExceptionSigned") {
        if (-not $exBySource.ContainsKey($uid)) {
            $failures += "universe source $uid status $($s.status) without signed exception"
        }
    }
}

# Field SoT
$sotFields = @{}
foreach ($t in $m.targetFieldSot) {
    foreach ($f in @($m.requiredTargetFieldSotFields)) {
        if ($null -eq $t.$f -or ($t.$f -is [string] -and [string]::IsNullOrWhiteSpace($t.$f))) {
            $failures += "targetFieldSot $($t.targetField) missing $f"
        }
    }
    if (-not $sourceMap.ContainsKey($t.sourceOfTruthSourceId)) {
        $failures += "SoT source $($t.sourceOfTruthSourceId) not in inventory"
    }
    $sotFields[$t.targetField] = $true
}
foreach ($need in @($m.requiredTargetFields)) {
    if (-not $sotFields.ContainsKey($need)) {
        $failures += "required target field $need missing SoT"
    }
}

# Blockers
$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($blockerMap["STR-001"] -ne "Satisfied") { $failures += "STR-001 must be Satisfied" }
if ($blockerMap["GOV-003"] -ne "Satisfied") { $failures += "GOV-003 must be Satisfied" }
if ($blockerMap["agency-extract-access"] -ne "Blocked") { $failures += "agency-extract-access must be Blocked" }
if ($blockerMap["SYR-002"] -ne "Blocked") { $failures += "SYR-002 must be Blocked" }

function Assert-FixtureExpected([string]$rel, [string]$expect) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $script:failures += "missing fixture $rel"
        return $null
    }
    $obj = Get-Content -Raw -Path $full | ConvertFrom-Json
    if ($obj.expectedValidate -ne $expect) {
        $script:failures += "fixture $rel expectedValidate must be $expect"
    }
    return $obj
}

$sampling = Assert-FixtureExpected $m.simulationPaths.sampling "PASS"
$counts = Assert-FixtureExpected $m.simulationPaths.rowFileCounts "PASS"
$profile = Assert-FixtureExpected $m.simulationPaths.profilingReport "PASS"
$accessFail = Assert-FixtureExpected $m.simulationPaths.accessRiskFail "FAIL"
$estFail = Assert-FixtureExpected $m.simulationPaths.estimateOnlyFail "FAIL"
$sotFail = Assert-FixtureExpected $m.simulationPaths.missingSotFail "FAIL"
$covEx = Assert-FixtureExpected $m.simulationPaths.coverageException "PASS"

if ($null -ne $profile -and $profile.agencySigned -ne $true) {
    $failures += "profiling report must be agencySigned"
}
if ($null -ne $accessFail) {
    if (@($accessFail.accessRisks).Count -gt 0) {
        $failures += "access-risk-fail fixture must have empty accessRisks"
    }
}
if ($null -ne $estFail) {
    if ($estFail.volume.estimateOnly -ne $true -or $estFail.status -ne "Inventoried") {
        $failures += "estimate-only-fail fixture must be Inventoried + estimateOnly"
    }
}
if ($null -ne $sotFail) {
    if ($null -ne $sotFail.sourceOfTruthSourceId -and -not [string]::IsNullOrWhiteSpace([string]$sotFail.sourceOfTruthSourceId)) {
        $failures += "missing-sot fixture must have null sourceOfTruthSourceId"
    }
}
if ($null -ne $covEx) {
    if (-not $covEx.coversUniverseGap) {
        $failures += "signed-unsupported fixture must cover universe gap"
    }
}
if ($null -ne $sampling -and @($sampling.samples).Count -lt 3) {
    $failures += "sampling manifest needs >= 3 samples"
}
if ($null -ne $counts -and @($counts.counts).Count -lt 3) {
    $failures += "counts fixture needs >= 3 rows"
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-001"
    ok = ($failures.Count -eq 0)
    sourceCount = $srcCount
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

Write-Host "PASS: MIG-001 source inventory validate"
exit 0
