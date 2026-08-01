# PROD-006: validate semantic-layer contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/prod006-semantic-layer-validate.ps1

param(
    [string]$MatrixPath = "docs/prod-semantic/semantic-layer-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/prod006/out"
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

$failures = @()
$m = Get-Json $MatrixPath

if ($m.card -ne "PROD-006") { $failures += "card must be PROD-006" }

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("KpiDefinitions", "Reconciliation", "TenantPrivacy", "Provenance")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.warehouseDecision.required -ne $true) { $failures += "warehouseDecision.required must be true" }
if ($m.warehouseDecision.labStubAllowed -ne $true) { $failures += "labStubAllowed must be true" }
if ($m.warehouseDecision.liveWarehouseForbidden -ne $true) { $failures += "liveWarehouseForbidden must be true" }
if ($m.semanticMetrics.required -ne $true) { $failures += "semanticMetrics.required must be true" }
if ($m.semanticMetrics.unifiedDefinitionRequired -ne $true) { $failures += "unifiedDefinitionRequired must be true" }
if ($m.scdDimensions.required -ne $true) { $failures += "scdDimensions.required must be true" }
if ($m.tenantRegionRollups.required -ne $true) { $failures += "tenantRegionRollups.required must be true" }
if ($m.tenantRegionRollups.rlsRequired -ne $true) { $failures += "rlsRequired must be true" }
if ($m.tenantRegionRollups.crossTenantForbidden -ne $true) { $failures += "crossTenantForbidden must be true" }
if ($m.statutoryReports.required -ne $true) { $failures += "statutoryReports.required must be true" }
if ($m.freshness.required -ne $true) { $failures += "freshness.required must be true" }
if ($m.lineage.required -ne $true) { $failures += "lineage.required must be true" }
if ($m.lineage.versionAsOfSourceRequired -ne $true) { $failures += "versionAsOfSourceRequired must be true" }
if ($m.privacyThresholds.required -ne $true) { $failures += "privacyThresholds.required must be true" }
if ($m.privacyThresholds.smallCellSuppressionRequired -ne $true) { $failures += "smallCellSuppressionRequired must be true" }
if ($m.privacyThresholds.reidentificationForbidden -ne $true) { $failures += "reidentificationForbidden must be true" }
if ($m.openDataAggregates.required -ne $true) { $failures += "openDataAggregates.required must be true" }
if ($m.openDataAggregates.labStubAllowed -ne $true) { $failures += "openDataAggregates.labStubAllowed must be true" }
if ($m.openDataAggregates.liveFeedForbidden -ne $true) { $failures += "liveFeedForbidden must be true" }
if ($m.governedBi.required -ne $true) { $failures += "governedBi.required must be true" }
if ($m.governedBi.labStubAllowed -ne $true) { $failures += "governedBi.labStubAllowed must be true" }
if ($m.governedBi.liveDashboardForbidden -ne $true) { $failures += "liveDashboardForbidden must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("reportId", "kpiId", "tenantId", "asOf", "sourceRef", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveWarehouseForbidden -ne $true) { $failures += "mockSandbox.liveWarehouseForbidden must be true" }
if ($m.mockSandbox.liveBiDashboardForbidden -ne $true) { $failures += "liveBiDashboardForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

foreach ($need in @(
    @{ Id = "GOV-003"; Status = "Satisfied" },
    @{ Id = "FUN-006"; Status = "Satisfied" },
    @{ Id = "PERF-004"; Status = "Satisfied" },
    @{ Id = "TEN-003"; Status = "Satisfied" },
    @{ Id = "live-warehouse"; Status = "Blocked" },
    @{ Id = "live-bi-dashboard"; Status = "Blocked" },
    @{ Id = "live-open-data-feed"; Status = "Blocked" },
    @{ Id = "ANA-001"; Status = "NotStarted" },
    @{ Id = "REP-001"; Status = "NotStarted" },
    @{ Id = "PROD-007"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($rel in @(
    "testdata/prod006/scenarios/undefined-kpi-fail.json",
    "testdata/prod006/scenarios/reconciliation-fail.json",
    "testdata/prod006/scenarios/cross-tenant-rollup-fail.json",
    "testdata/prod006/scenarios/small-cell-attack-fail.json",
    "testdata/prod006/scenarios/missing-provenance-fail.json",
    "testdata/prod006/scenarios/stale-without-freshness-fail.json",
    "testdata/prod006/scenarios/kpi-definition-pass.json",
    "testdata/prod006/scenarios/golden-report-pass.json",
    "testdata/prod006/scenarios/tenant-scoped-pass.json",
    "testdata/prod006/scenarios/privacy-threshold-pass.json",
    "testdata/prod006/scenarios/versioned-report-pass.json",
    "testdata/prod006/scenarios/lineage-freshness-pass.json",
    "testdata/prod006/audit/semantic-audit-pass.json",
    "testdata/prod006/harness/semantic-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$uk = Get-Json "testdata/prod006/scenarios/undefined-kpi-fail.json"
if ($uk.expectedValidate -ne "FAIL" -or $uk.definitionPresent -eq $true) { $failures += "undefined-kpi-fail invalid" }
$rf = Get-Json "testdata/prod006/scenarios/reconciliation-fail.json"
if ($rf.expectedValidate -ne "FAIL" -or $rf.reconciledWithSource -eq $true) { $failures += "reconciliation-fail invalid" }
$ct = Get-Json "testdata/prod006/scenarios/cross-tenant-rollup-fail.json"
if ($ct.expectedValidate -ne "FAIL" -or $ct.withinTenant -eq $true) { $failures += "cross-tenant-rollup-fail invalid" }
$sc = Get-Json "testdata/prod006/scenarios/small-cell-attack-fail.json"
if ($sc.expectedValidate -ne "FAIL" -or $sc.smallCellSuppressed -eq $true) { $failures += "small-cell-attack-fail invalid" }
$mp = Get-Json "testdata/prod006/scenarios/missing-provenance-fail.json"
if ($mp.expectedValidate -ne "FAIL" -or $mp.hasVersion -eq $true) { $failures += "missing-provenance-fail invalid" }
$st = Get-Json "testdata/prod006/scenarios/stale-without-freshness-fail.json"
if ($st.expectedValidate -ne "FAIL" -or $st.withinSla -eq $true) { $failures += "stale-without-freshness-fail invalid" }

$kd = Get-Json "testdata/prod006/scenarios/kpi-definition-pass.json"
if ($kd.expectedValidate -ne "PASS" -or $kd.definitionPresent -ne $true) { $failures += "kpi-definition-pass invalid" }
$gr = Get-Json "testdata/prod006/scenarios/golden-report-pass.json"
if ($gr.expectedValidate -ne "PASS" -or $gr.reconciledWithSource -ne $true) { $failures += "golden-report-pass invalid" }
$ts = Get-Json "testdata/prod006/scenarios/tenant-scoped-pass.json"
if ($ts.expectedValidate -ne "PASS" -or $ts.withinTenant -ne $true) { $failures += "tenant-scoped-pass invalid" }
$pt = Get-Json "testdata/prod006/scenarios/privacy-threshold-pass.json"
if ($pt.expectedValidate -ne "PASS" -or $pt.smallCellSuppressed -ne $true) { $failures += "privacy-threshold-pass invalid" }
$vr = Get-Json "testdata/prod006/scenarios/versioned-report-pass.json"
if ($vr.expectedValidate -ne "PASS" -or $vr.hasVersion -ne $true -or $vr.hasAsOf -ne $true -or $vr.hasSource -ne $true) { $failures += "versioned-report-pass invalid" }
$lf = Get-Json "testdata/prod006/scenarios/lineage-freshness-pass.json"
if ($lf.expectedValidate -ne "PASS" -or $lf.withinSla -ne $true) { $failures += "lineage-freshness-pass invalid" }

$audit = Get-Json "testdata/prod006/audit/semantic-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "semantic-audit-pass invalid" }
$ledger = Get-Json "testdata/prod006/harness/semantic-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "semantic-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveSemantic -eq $true) { $failures += "ledger run $($r.runId) liveSemantic must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-006"
    mode = "pack-validate"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "pack-validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: PROD-006 semantic-layer pack validate"
exit 0
