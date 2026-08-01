# ARC-001: runtime/production criticality binding validate.
param(
    [string]$MatrixPath = "docs/arc-criticality/criticality-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/arc001/out"
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

if ($m.card -ne "ARC-001") { $failures += "card must be ARC-001" }

foreach ($need in @(
    @{ Id = "STR-001"; Status = "Satisfied" },
    @{ Id = "OBS-001"; Status = "Satisfied" },
    @{ Id = "OPS-001"; Status = "Satisfied" },
    @{ Id = "SOV-007"; Status = "Satisfied" },
    @{ Id = "live-architecture-failure-walkthrough"; Status = "Blocked" },
    @{ Id = "live-controlled-dependency-shutdown"; Status = "Blocked" },
    @{ Id = "live-recovery-order-drill"; Status = "Blocked" },
    @{ Id = "live-full-dependency-discovery"; Status = "Blocked" },
    @{ Id = "ARC-002"; Status = "Satisfied" },
    @{ Id = "PHY-001"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.tierClassification.allServicesClassifiedRequired -ne $true) { $failures += "allServicesClassifiedRequired must be true" }
if ($m.spofPolicy.noUnacceptableSpofInTier01Required -ne $true) { $failures += "noUnacceptableSpofInTier01Required must be true" }
if ($m.numericTargetsTelemetry.everyNumericTargetHasTelemetryRequired -ne $true) { $failures += "everyNumericTargetHasTelemetryRequired must be true" }
if ($m.recoveryOrder.orderedStepsRequired -ne $true) { $failures += "orderedStepsRequired must be true" }
if ($m.mockSandbox.liveArchitectureFailureWalkthroughForbidden -ne $true) { $failures += "liveArchitectureFailureWalkthroughForbidden must be true" }
if ($m.mockSandbox.liveControlledDependencyShutdownForbidden -ne $true) { $failures += "liveControlledDependencyShutdownForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signing owner equals reviewer" }
    $version = $sig.criticalityPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.criticalityPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$us = Get-Json "testdata/arc001/scenarios/unclassified-service-fail.json"
if ($us.expectedValidate -ne "FAIL" -or $us.allServicesClassified -ne $false) { $failures += "unclassified-service-fail invalid" }
$sp = Get-Json "testdata/arc001/scenarios/tier01-spof-fail.json"
if ($sp.expectedValidate -ne "FAIL" -or $sp.unacceptableSpofInTier01 -ne $true) { $failures += "tier01-spof-fail invalid" }
$tt = Get-Json "testdata/arc001/scenarios/target-without-telemetry-fail.json"
if ($tt.expectedValidate -ne "FAIL" -or $tt.targetWithoutTelemetry -ne $true) { $failures += "target-without-telemetry-fail invalid" }
$ro = Get-Json "testdata/arc001/scenarios/recovery-order-missing-fail.json"
if ($ro.expectedValidate -ne "FAIL" -or $ro.orderedStepsPresent -ne $false) { $failures += "recovery-order-missing-fail invalid" }
$lc = Get-Json "testdata/arc001/scenarios/live-criticality-claim-fail.json"
if ($lc.expectedValidate -ne "FAIL" -or $lc.liveCriticalityClaim -ne $true) { $failures += "live-criticality-claim-fail invalid" }

$ledger = Get-Json "testdata/arc001/harness/criticality-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "criticality-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveCriticality -eq $true) { $failures += "ledger run $($r.runId) liveCriticality must be false" }
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
    $ac = $cfg.ArcCriticality
    if ($null -eq $ac) { $failures += "$rel missing ArcCriticality"; continue }
    if ($ac.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($ac.LiveCriticalityClaim -eq $true) { $failures += "$rel LiveCriticalityClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "ARC-001"
    mode = "runtime-validate"
    ok = ($failures.Count -eq 0)
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

Write-Host "PASS: ARC-001 criticality runtime validate"
exit 0
