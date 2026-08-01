# PROD-006: runtime/production semantic-layer binding validate.
# Usage:
#   powershell -File scripts/prod006-semantic-layer-runtime-validate.ps1

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
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.semanticLayerPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.semanticLayerPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$uk = Get-Json "testdata/prod006/scenarios/undefined-kpi-fail.json"
$rf = Get-Json "testdata/prod006/scenarios/reconciliation-fail.json"
$ct = Get-Json "testdata/prod006/scenarios/cross-tenant-rollup-fail.json"
$sc = Get-Json "testdata/prod006/scenarios/small-cell-attack-fail.json"
$mp = Get-Json "testdata/prod006/scenarios/missing-provenance-fail.json"
$st = Get-Json "testdata/prod006/scenarios/stale-without-freshness-fail.json"

if ($uk.expectedValidate -ne "FAIL" -or $uk.definitionPresent -eq $true) { $failures += "undefined-kpi-fail invalid" }
if ($rf.expectedValidate -ne "FAIL" -or $rf.reconciledWithSource -eq $true) { $failures += "reconciliation-fail invalid" }
if ($ct.expectedValidate -ne "FAIL" -or $ct.withinTenant -eq $true) { $failures += "cross-tenant-rollup-fail invalid" }
if ($sc.expectedValidate -ne "FAIL" -or $sc.smallCellSuppressed -eq $true) { $failures += "small-cell-attack-fail invalid" }
if ($mp.expectedValidate -ne "FAIL" -or $mp.hasVersion -eq $true) { $failures += "missing-provenance-fail invalid" }
if ($st.expectedValidate -ne "FAIL" -or $st.withinSla -eq $true) { $failures += "stale-without-freshness-fail invalid" }

# Positive
$kd = Get-Json "testdata/prod006/scenarios/kpi-definition-pass.json"
$gr = Get-Json "testdata/prod006/scenarios/golden-report-pass.json"
$ts = Get-Json "testdata/prod006/scenarios/tenant-scoped-pass.json"
$pt = Get-Json "testdata/prod006/scenarios/privacy-threshold-pass.json"
$vr = Get-Json "testdata/prod006/scenarios/versioned-report-pass.json"
$lf = Get-Json "testdata/prod006/scenarios/lineage-freshness-pass.json"
$audit = Get-Json "testdata/prod006/audit/semantic-audit-pass.json"
$ledger = Get-Json "testdata/prod006/harness/semantic-run-ledger.json"

if ($kd.expectedValidate -ne "PASS") { $failures += "kpi-definition-pass invalid" }
if ($gr.expectedValidate -ne "PASS") { $failures += "golden-report-pass invalid" }
if ($ts.expectedValidate -ne "PASS") { $failures += "tenant-scoped-pass invalid" }
if ($pt.expectedValidate -ne "PASS") { $failures += "privacy-threshold-pass invalid" }
if ($vr.expectedValidate -ne "PASS") { $failures += "versioned-report-pass invalid" }
if ($lf.expectedValidate -ne "PASS") { $failures += "lineage-freshness-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "semantic-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "semantic-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveSemantic -eq $true) { $failures += "ledger run $($r.runId) liveSemantic must be false" }
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
    $fullPath = Resolve-RepoPath $rel
    if (-not (Test-Path $fullPath)) { $failures += "missing $rel"; continue }
    $cfg = Get-Content -Raw -Path $fullPath | ConvertFrom-Json
    $ps = $cfg.ProdSemantic
    if ($null -eq $ps) { $failures += "$rel missing ProdSemantic"; continue }
    if ($ps.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($ps.LiveSemanticLayerClaim -eq $true) { $failures += "$rel LiveSemanticLayerClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-006"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    auditFieldCount = @($m.audit.requiredFields).Count
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

Write-Host "PASS: PROD-006 semantic-layer runtime validate"
exit 0
