# STR-007: validate value KPI matrix, baselines, privacy, simulation fixtures.
# Usage:
#   powershell -File scripts/str007-value-kpi-validate.ps1

param(
    [string]$MatrixPath = "docs/str-value/kpi-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str007/out"
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

if ($m.card -ne "STR-007") { $failures += "card must be STR-007" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("KpiCatalog", "BaselineFreeze", "IndependentMeasure", "SampleReconcile", "QuarterlyReview", "PrivacyGate")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

# KPIs
$kpiIds = @{}
$fields = @($m.requiredKpiFields)
$kpiCount = 0
foreach ($k in $m.kpis) {
    $kpiCount++
    foreach ($f in $fields) {
        if ($null -eq $k.$f) {
            $failures += "kpi $($k.id) missing $f"
        }
        elseif ($f -eq "antiGamingRules") {
            if (@($k.antiGamingRules).Count -lt 1) {
                $failures += "kpi $($k.id) empty antiGamingRules"
            }
        }
        elseif ($f -eq "moduleIds") {
            # array may be empty for platform KPIs
        }
        elseif ($f -eq "target") {
            # object ok
        }
        elseif ($k.$f -is [string] -and [string]::IsNullOrWhiteSpace($k.$f)) {
            $failures += "kpi $($k.id) empty $f"
        }
    }
    if ($k.ownerRole -eq $k.auditorRole) {
        $failures += "kpi $($k.id) owner equals auditor"
    }
    $kpiIds[$k.id] = $k
}
if ($kpiCount -lt 9) { $failures += "need >= 9 kpis" }

# Baselines for baselineRequired
$baselineByKpi = @{}
foreach ($b in $m.baselines) {
    $baselineByKpi[$b.kpiId] = $b
    if (-not $kpiIds.ContainsKey($b.kpiId)) {
        $failures += "baseline for unknown kpi $($b.kpiId)"
    }
}
foreach ($k in $m.kpis) {
    if ($k.baselineRequired) {
        if (-not $baselineByKpi.ContainsKey($k.id)) {
            $failures += "kpi $($k.id) missing baseline"
        }
        else {
            $b = $baselineByKpi[$k.id]
            if (-not $b.prePilotFreeze) {
                $failures += "kpi $($k.id) baseline not prePilotFreeze"
            }
        }
    }
}

# Privacy
if (-not $m.privacyRules.tenantScoped) {
    $failures += "privacyRules.tenantScoped must be true"
}
if (-not $m.privacyRules.forbidCrossTenantRollupWithoutWaiver) {
    $failures += "forbidCrossTenantRollupWithoutWaiver must be true"
}
if ([int]$m.privacyRules.minCellSize -lt 1) {
    $failures += "minCellSize must be >= 1"
}

# Forbidden measures list
$forbidden = @($m.forbiddenBenefitMeasures)
foreach ($need in @("screenCount", "pageCount", "widgetCount")) {
    if ($forbidden -notcontains $need) { $failures += "forbiddenBenefitMeasures missing $need" }
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
if ($blockerMap["STR-003"] -ne "Satisfied") { $failures += "STR-003 must be Satisfied" }
if ($blockerMap["GOV-003"] -ne "Satisfied") { $failures += "GOV-003 must be Satisfied" }
if ($blockerMap["FIN-001"] -ne "NotStarted") { $failures += "FIN-001 must be NotStarted" }
if ($blockerMap["PROD-003"] -ne "Satisfied") { $failures += "PROD-003 must be Satisfied" }

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

$legacy = Assert-FixtureExpected $m.simulationPaths.legacyBaseline "PASS"
$indep = Assert-FixtureExpected $m.simulationPaths.independentMeasurement "PASS"
$reconPass = Assert-FixtureExpected $m.simulationPaths.sampleReconcilePass "PASS"
$reconFail = Assert-FixtureExpected $m.simulationPaths.sampleReconcileFail "FAIL"
$quarter = Assert-FixtureExpected $m.simulationPaths.quarterlyReview "PASS"
$screen = Assert-FixtureExpected $m.simulationPaths.screenCountBanned "FAIL"
$leak = Assert-FixtureExpected $m.simulationPaths.crossTenantLeak "FAIL"

if ($null -ne $legacy -and -not $legacy.prePilotFreeze) {
    $failures += "legacy-sample must have prePilotFreeze"
}
if ($null -ne $indep) {
    if ($indep.ownerRole -eq $indep.assessorRole) {
        $failures += "independent-measurement owner must differ from assessor"
    }
}
function Test-WithinTolerance($reported, $source, $tolPct) {
    if ($null -eq $source -or [double]$source -eq 0) {
        return ([math]::Abs([double]$reported - [double]$source) -le 0.0001)
    }
    $rel = [math]::Abs([double]$reported - [double]$source) / [math]::Abs([double]$source) * 100.0
    return ($rel -le [double]$tolPct)
}
if ($null -ne $reconPass) {
    if (-not (Test-WithinTolerance $reconPass.reportedValue $reconPass.sourceValue $reconPass.tolerancePct)) {
        $failures += "reconcile-pass fixture not within tolerance"
    }
}
if ($null -ne $reconFail) {
    if (Test-WithinTolerance $reconFail.reportedValue $reconFail.sourceValue $reconFail.tolerancePct) {
        $failures += "reconcile-fail fixture incorrectly within tolerance"
    }
}
if ($null -ne $quarter) {
    foreach ($metric in @($quarter.metricsReviewed)) {
        if ($forbidden -contains $metric) {
            $failures += "quarterly review uses forbidden measure $metric"
        }
    }
    if (@($quarter.forbiddenMeasuresUsed).Count -gt 0) {
        $failures += "quarterly review must have empty forbiddenMeasuresUsed for PASS"
    }
}
if ($null -ne $screen) {
    if ($forbidden -notcontains $screen.claimedBenefitMeasure) {
        $failures += "screen-count fixture must claim a forbidden measure"
    }
}
if ($null -ne $leak) {
    if ($leak.tenantScoped -eq $true -and $leak.crossTenantRollup -ne $true) {
        $failures += "cross-tenant-leak fixture must violate privacy"
    }
    if ($leak.waiverPresent -eq $true) {
        $failures += "cross-tenant-leak fixture must not have waiver"
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-007"
    ok = ($failures.Count -eq 0)
    kpiCount = $kpiCount
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

Write-Host "PASS: STR-007 value kpi validate"
exit 0
