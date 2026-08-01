# ARC-001: validate criticality contract matrix and harness fixtures.
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

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("TierClassification", "DependenciesPlatform", "SpofTelemetryRecovery", "ClaimGate")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.tierClassification.required -ne $true) { $failures += "tierClassification.required must be true" }
if ($m.tierClassification.allServicesClassifiedRequired -ne $true) { $failures += "allServicesClassifiedRequired must be true" }
if ($m.tierClassification.allDependenciesClassifiedRequired -ne $true) { $failures += "allDependenciesClassifiedRequired must be true" }
$tiers = @($m.tierClassification.tiers | ForEach-Object { [string]$_ })
foreach ($need in @("0", "1", "2")) {
    if ($tiers -notcontains $need) { $failures += "tierClassification missing tier $need" }
}

if ($m.journeyDependencyMap.required -ne $true) { $failures += "journeyDependencyMap.required must be true" }
if ($m.journeyDependencyMap.criticalJourneysMappedRequired -ne $true) { $failures += "criticalJourneysMappedRequired must be true" }
if ($m.sharedPlatformDeps.required -ne $true) { $failures += "sharedPlatformDeps.required must be true" }
$surfaces = @($m.sharedPlatformDeps.surfaces)
foreach ($need in @("dns", "pki", "kms", "registry", "ci", "telemetry")) {
    if ($surfaces -notcontains $need) { $failures += "sharedPlatformDeps missing $need" }
}
if ($m.failureDomains.required -ne $true) { $failures += "failureDomains.required must be true" }
if ($m.failureDomains.domainsDocumentedRequired -ne $true) { $failures += "domainsDocumentedRequired must be true" }
if ($m.numericTargetsTelemetry.required -ne $true) { $failures += "numericTargetsTelemetry.required must be true" }
if ($m.numericTargetsTelemetry.everyNumericTargetHasTelemetryRequired -ne $true) { $failures += "everyNumericTargetHasTelemetryRequired must be true" }
if ($m.spofPolicy.required -ne $true) { $failures += "spofPolicy.required must be true" }
if ($m.spofPolicy.noUnacceptableSpofInTier01Required -ne $true) { $failures += "noUnacceptableSpofInTier01Required must be true" }
if ($m.recoveryOrder.required -ne $true) { $failures += "recoveryOrder.required must be true" }
if ($m.recoveryOrder.orderedStepsRequired -ne $true) { $failures += "orderedStepsRequired must be true" }
if ($m.recoveryOrder.drillEvidenceRequired -ne $true) { $failures += "drillEvidenceRequired must be true" }
if ($m.gracefulDegradation.required -ne $true) { $failures += "gracefulDegradation.required must be true" }
if ($m.gracefulDegradation.tier01DegradationDocumentedRequired -ne $true) { $failures += "tier01DegradationDocumentedRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("componentId", "tier", "dependencyRef", "failureDomainRef", "sloRef", "telemetryRef", "recoveryStepRef", "actor", "occurredAt", "evidenceRef", "outcome", "spofExceptionRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveArchitectureFailureWalkthroughForbidden -ne $true) { $failures += "liveArchitectureFailureWalkthroughForbidden must be true" }
if ($m.mockSandbox.liveControlledDependencyShutdownForbidden -ne $true) { $failures += "liveControlledDependencyShutdownForbidden must be true" }
if ($m.mockSandbox.liveRecoveryOrderDrillForbidden -ne $true) { $failures += "liveRecoveryOrderDrillForbidden must be true" }
if ($m.mockSandbox.liveFullDependencyDiscoveryForbidden -ne $true) { $failures += "liveFullDependencyDiscoveryForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

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

$components = @($m.components)
if ($components.Count -lt 8) { $failures += "components need >= 8 lab entries" }
foreach ($needId in @("gateway", "hr-core", "payroll", "auth-directory", "file-storage", "postgres", "rabbitmq", "keycloak")) {
    if (-not ($components | Where-Object { $_.id -eq $needId })) { $failures += "component $needId missing" }
}

foreach ($rel in @(
    "testdata/arc001/scenarios/unclassified-service-fail.json",
    "testdata/arc001/scenarios/unclassified-dependency-fail.json",
    "testdata/arc001/scenarios/tier01-spof-fail.json",
    "testdata/arc001/scenarios/target-without-telemetry-fail.json",
    "testdata/arc001/scenarios/recovery-order-missing-fail.json",
    "testdata/arc001/scenarios/shared-platform-gap-fail.json",
    "testdata/arc001/scenarios/journey-unmapped-fail.json",
    "testdata/arc001/scenarios/live-criticality-claim-fail.json",
    "testdata/arc001/scenarios/full-classification-pass.json",
    "testdata/arc001/scenarios/journey-map-pass.json",
    "testdata/arc001/scenarios/shared-platform-pass.json",
    "testdata/arc001/scenarios/telemetry-bound-pass.json",
    "testdata/arc001/scenarios/spof-mitigated-pass.json",
    "testdata/arc001/scenarios/recovery-order-lab-pass.json",
    "testdata/arc001/scenarios/failure-domain-pass.json",
    "testdata/arc001/audit/criticality-audit-pass.json",
    "testdata/arc001/harness/criticality-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$us = Get-Json "testdata/arc001/scenarios/unclassified-service-fail.json"
if ($us.expectedValidate -ne "FAIL" -or $us.allServicesClassified -ne $false) { $failures += "unclassified-service-fail invalid" }
$ud = Get-Json "testdata/arc001/scenarios/unclassified-dependency-fail.json"
if ($ud.expectedValidate -ne "FAIL" -or $ud.allDependenciesClassified -ne $false) { $failures += "unclassified-dependency-fail invalid" }
$sp = Get-Json "testdata/arc001/scenarios/tier01-spof-fail.json"
if ($sp.expectedValidate -ne "FAIL" -or $sp.unacceptableSpofInTier01 -ne $true) { $failures += "tier01-spof-fail invalid" }
$tt = Get-Json "testdata/arc001/scenarios/target-without-telemetry-fail.json"
if ($tt.expectedValidate -ne "FAIL" -or $tt.targetWithoutTelemetry -ne $true) { $failures += "target-without-telemetry-fail invalid" }
$ro = Get-Json "testdata/arc001/scenarios/recovery-order-missing-fail.json"
if ($ro.expectedValidate -ne "FAIL" -or $ro.orderedStepsPresent -ne $false) { $failures += "recovery-order-missing-fail invalid" }
$sg = Get-Json "testdata/arc001/scenarios/shared-platform-gap-fail.json"
if ($sg.expectedValidate -ne "FAIL" -or @($sg.missingSurfaces).Count -lt 1) { $failures += "shared-platform-gap-fail invalid" }
$ju = Get-Json "testdata/arc001/scenarios/journey-unmapped-fail.json"
if ($ju.expectedValidate -ne "FAIL" -or $ju.criticalJourneysMapped -ne $false) { $failures += "journey-unmapped-fail invalid" }
$lc = Get-Json "testdata/arc001/scenarios/live-criticality-claim-fail.json"
if ($lc.expectedValidate -ne "FAIL" -or $lc.liveCriticalityClaim -ne $true) { $failures += "live-criticality-claim-fail invalid" }

$fc = Get-Json "testdata/arc001/scenarios/full-classification-pass.json"
if ($fc.expectedValidate -ne "PASS" -or $fc.allServicesClassified -ne $true) { $failures += "full-classification-pass invalid" }
$jm = Get-Json "testdata/arc001/scenarios/journey-map-pass.json"
if ($jm.expectedValidate -ne "PASS" -or $jm.criticalJourneysMapped -ne $true) { $failures += "journey-map-pass invalid" }
$spp = Get-Json "testdata/arc001/scenarios/shared-platform-pass.json"
if ($spp.expectedValidate -ne "PASS") { $failures += "shared-platform-pass invalid" }
$tb = Get-Json "testdata/arc001/scenarios/telemetry-bound-pass.json"
if ($tb.expectedValidate -ne "PASS" -or $tb.targetWithoutTelemetry -eq $true) { $failures += "telemetry-bound-pass invalid" }
$sm = Get-Json "testdata/arc001/scenarios/spof-mitigated-pass.json"
if ($sm.expectedValidate -ne "PASS" -or $sm.unacceptableSpofInTier01 -eq $true) { $failures += "spof-mitigated-pass invalid" }
$rl = Get-Json "testdata/arc001/scenarios/recovery-order-lab-pass.json"
if ($rl.expectedValidate -ne "PASS" -or $rl.orderedStepsPresent -ne $true) { $failures += "recovery-order-lab-pass invalid" }
$fd = Get-Json "testdata/arc001/scenarios/failure-domain-pass.json"
if ($fd.expectedValidate -ne "PASS" -or $fd.domainsDocumented -ne $true) { $failures += "failure-domain-pass invalid" }

$audit = Get-Json "testdata/arc001/audit/criticality-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "criticality-audit-pass invalid" }
$ledger = Get-Json "testdata/arc001/harness/criticality-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "criticality-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveCriticality -eq $true) { $failures += "ledger run $($r.runId) liveCriticality must be false" }
}

foreach ($rel in @(
    "docs/ADR-ARC-001-criticality.md",
    "docs/ARC_001_JOURNEY_MATRIX.md",
    "docs/ARC_001_TIERS.md",
    "docs/ARC_001_DEPENDENCIES.md",
    "docs/ARC_001_FAILURE_DOMAINS.md",
    "docs/ARC_001_TELEMETRY.md",
    "docs/ARC_001_SPOF.md",
    "docs/ARC_001_RECOVERY.md",
    "docs/ARC_001_READINESS_CHECKLIST.md",
    "docs/ARC_001_UAT_PACK.md",
    "docs/ARC_001_VERIFICATION.md"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "ARC-001"
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

Write-Host "PASS: ARC-001 criticality pack validate"
exit 0
