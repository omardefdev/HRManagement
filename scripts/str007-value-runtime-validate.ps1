# STR-007: runtime/production value KPI binding validate.
# Usage:
#   powershell -File scripts/str007-value-runtime-validate.ps1

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

function Get-Json([string]$rel) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { throw "missing $rel ($full)" }
    return Get-Content -Raw -Path $full | ConvertFrom-Json
}

function Test-WithinTolerance($reported, $source, $tolPct) {
    if ($null -eq $source -or [double]$source -eq 0) {
        return ([math]::Abs([double]$reported - [double]$source) -le 0.0001)
    }
    $rel = [math]::Abs([double]$reported - [double]$source) / [math]::Abs([double]$source) * 100.0
    return ($rel -le [double]$tolPct)
}

$failures = @()
$m = Get-Json $MatrixPath

if ($m.card -ne "STR-007") { $failures += "card must be STR-007" }

foreach ($need in @(
    @{ Id = "STR-001"; Status = "Satisfied" },
    @{ Id = "STR-003"; Status = "Satisfied" },
    @{ Id = "GOV-003"; Status = "Satisfied" },
    @{ Id = "FIN-001"; Status = "NotStarted" },
    @{ Id = "PROD-003"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

$kpiCount = @($m.kpis).Count
if ($kpiCount -lt 9) { $failures += "need >= 9 kpis" }

$fields = @($m.requiredKpiFields)
$baselineByKpi = @{}
foreach ($b in @($m.baselines)) { $baselineByKpi[$b.kpiId] = $b }

foreach ($k in @($m.kpis)) {
    foreach ($f in $fields) {
        if ($null -eq $k.$f) { $failures += "kpi $($k.id) missing $f" }
        elseif ($f -eq "antiGamingRules" -and @($k.antiGamingRules).Count -lt 1) {
            $failures += "kpi $($k.id) empty antiGamingRules"
        }
        elseif ($f -notin @("antiGamingRules", "moduleIds", "target", "baselineRequired") `
            -and $k.$f -is [string] -and [string]::IsNullOrWhiteSpace($k.$f)) {
            $failures += "kpi $($k.id) empty $f"
        }
    }
    if ([string]$k.ownerRole -eq [string]$k.auditorRole) {
        $failures += "kpi $($k.id) owner equals auditor"
    }
    if ($k.baselineRequired) {
        if (-not $baselineByKpi.ContainsKey($k.id)) {
            $failures += "kpi $($k.id) missing baseline"
        }
        elseif (-not $baselineByKpi[$k.id].prePilotFreeze) {
            $failures += "kpi $($k.id) baseline not prePilotFreeze"
        }
    }
}

if (-not $m.privacyRules.tenantScoped) { $failures += "privacyRules.tenantScoped must be true" }
if (-not $m.privacyRules.forbidCrossTenantRollupWithoutWaiver) {
    $failures += "forbidCrossTenantRollupWithoutWaiver must be true"
}
if ([int]$m.privacyRules.minCellSize -lt 1) { $failures += "minCellSize must be >= 1" }

$forbidden = @($m.forbiddenBenefitMeasures)
foreach ($need in @("screenCount", "pageCount", "widgetCount")) {
    if ($forbidden -notcontains $need) { $failures += "forbiddenBenefitMeasures missing $need" }
}

$reconFail = Get-Json "testdata/str007/scenarios/sample-reconciliation-fail.json"
$reconPass = Get-Json "testdata/str007/scenarios/sample-reconciliation-pass.json"
$screen = Get-Json "testdata/str007/scenarios/screen-count-banned.json"
$leak = Get-Json "testdata/str007/privacy/cross-tenant-leak.json"
$indep = Get-Json "testdata/str007/scenarios/independent-measurement.json"
$quarter = Get-Json "testdata/str007/scenarios/quarterly-benefit-review.json"

if ($reconFail.expectedValidate -ne "FAIL") { $failures += "reconcile-fail expectedValidate must be FAIL" }
if (Test-WithinTolerance $reconFail.reportedValue $reconFail.sourceValue $reconFail.tolerancePct) {
    $failures += "reconcile-fail fixture must be outside tolerance"
}

if ($reconPass.expectedValidate -ne "PASS") { $failures += "reconcile-pass expectedValidate must be PASS" }
if (-not (Test-WithinTolerance $reconPass.reportedValue $reconPass.sourceValue $reconPass.tolerancePct)) {
    $failures += "reconcile-pass fixture must be within tolerance"
}

if ($screen.expectedValidate -ne "FAIL") { $failures += "screen-count expectedValidate must be FAIL" }
if ($forbidden -notcontains $screen.claimedBenefitMeasure) {
    $failures += "screen-count fixture must claim a forbidden measure"
}

if ($leak.expectedValidate -ne "FAIL") { $failures += "cross-tenant-leak expectedValidate must be FAIL" }
if ($leak.tenantScoped -eq $true -and $leak.crossTenantRollup -ne $true) {
    $failures += "cross-tenant-leak fixture must violate privacy"
}
if ($leak.waiverPresent -eq $true) { $failures += "cross-tenant-leak must not have waiver" }

if ($indep.expectedValidate -ne "PASS") { $failures += "independent-measurement expectedValidate must be PASS" }
if ([string]$indep.ownerRole -eq [string]$indep.assessorRole) {
    $failures += "independent-measurement owner must differ from assessor"
}

if ($quarter.expectedValidate -ne "PASS") { $failures += "quarterly-review expectedValidate must be PASS" }
foreach ($metric in @($quarter.metricsReviewed)) {
    if ($forbidden -contains $metric) { $failures += "quarterly review uses forbidden measure $metric" }
}
if (@($quarter.forbiddenMeasuresUsed).Count -gt 0) {
    $failures += "quarterly review must have empty forbiddenMeasuresUsed"
}

$prodFiles = @(
    "src/Gateway/HRManagement.Gateway/appsettings.Production.json",
    "src/Gateway/HRManagement.Gateway/appsettings.Staging.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Production.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Staging.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Production.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Staging.json"
)

foreach ($rel in $prodFiles) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { $failures += "missing $rel"; continue }
    $cfg = Get-Content -Raw -Path $full | ConvertFrom-Json
    $pv = $cfg.ProgramValue
    if ($null -eq $pv) { $failures += "$rel missing ProgramValue"; continue }
    if ($pv.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($pv.LiveDashboardClaim -eq $true) { $failures += "$rel LiveDashboardClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-007"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    kpiCount = $kpiCount
    frozenBaselineCount = @($m.baselines | Where-Object { $_.prePilotFreeze -eq $true }).Count
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

Write-Host "PASS: STR-007 value runtime validate"
exit 0
