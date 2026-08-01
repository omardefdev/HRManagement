# SOV-005: validate threat-model contract matrix and harness fixtures.
param(
    [string]$MatrixPath = "docs/sov-threat/threat-model-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov005/out"
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

if ($m.card -ne "SOV-005") { $failures += "card must be SOV-005" }

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("MethodologyBoundaries", "HighCoverage", "ChangeMisuseResidual", "ClaimGate")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.methodology.required -ne $true) { $failures += "methodology.required must be true" }
if ($m.methodology.strideRequired -ne $true) { $failures += "strideRequired must be true" }
if ($m.trustBoundaries.required -ne $true) { $failures += "trustBoundaries.required must be true" }
if ($m.trustBoundaries.inventoryRequired -ne $true) { $failures += "inventoryRequired must be true" }
if ($m.assetsActors.required -ne $true) { $failures += "assetsActors.required must be true" }
if ($m.threatCategories.required -ne $true) { $failures += "threatCategories.required must be true" }
$categories = @($m.threatCategories.categories)
foreach ($need in @("payrollFraud", "insider", "massExport", "tenantEscape", "messageReplay", "supplyChain", "migration", "recovery")) {
    if ($categories -notcontains $need) { $failures += "threatCategories missing $need" }
}
if ($m.highThreatCoverage.required -ne $true) { $failures += "highThreatCoverage.required must be true" }
if ($m.highThreatCoverage.controlRequired -ne $true) { $failures += "controlRequired must be true" }
if ($m.highThreatCoverage.testRequired -ne $true) { $failures += "testRequired must be true" }
if ($m.highThreatCoverage.ownerRequired -ne $true) { $failures += "ownerRequired must be true" }
if ($m.changeTrigger.required -ne $true) { $failures += "changeTrigger.required must be true" }
if ($m.changeTrigger.materialChangeUpdateRequired -ne $true) { $failures += "materialChangeUpdateRequired must be true" }
if ($m.misuseStories.required -ne $true) { $failures += "misuseStories.required must be true" }
if ($m.misuseStories.backlogLinkageRequired -ne $true) { $failures += "backlogLinkageRequired must be true" }
if ($m.residualRisk.required -ne $true) { $failures += "residualRisk.required must be true" }
if ($m.residualRisk.signedRequired -ne $true) { $failures += "signedRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("threatId", "category", "severity", "controlRef", "testRef", "ownerRole", "actor", "occurredAt", "evidenceRef", "outcome")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveIndependentTmReviewForbidden -ne $true) { $failures += "liveIndependentTmReviewForbidden must be true" }
if ($m.mockSandbox.liveAttackTreeWalkthroughForbidden -ne $true) { $failures += "liveAttackTreeWalkthroughForbidden must be true" }
if ($m.mockSandbox.liveMisuseBacklogSyncForbidden -ne $true) { $failures += "liveMisuseBacklogSyncForbidden must be true" }
if ($m.mockSandbox.liveResidualRiskSignoffForbidden -ne $true) { $failures += "liveResidualRiskSignoffForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

foreach ($need in @(
    @{ Id = "STR-001"; Status = "Satisfied" },
    @{ Id = "TEN-001"; Status = "Satisfied" },
    @{ Id = "INT-003"; Status = "Satisfied" },
    @{ Id = "SOV-004"; Status = "Satisfied" },
    @{ Id = "live-independent-tm-review"; Status = "Blocked" },
    @{ Id = "live-attack-tree-walkthrough"; Status = "Blocked" },
    @{ Id = "live-misuse-backlog-sync"; Status = "Blocked" },
    @{ Id = "live-residual-risk-signoff"; Status = "Blocked" },
    @{ Id = "SOV-006"; Status = "Satisfied" },
    @{ Id = "NAT-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($rel in @(
    "testdata/sov005/scenarios/missing-stride-fail.json",
    "testdata/sov005/scenarios/high-without-control-fail.json",
    "testdata/sov005/scenarios/high-without-test-fail.json",
    "testdata/sov005/scenarios/high-without-owner-fail.json",
    "testdata/sov005/scenarios/no-change-trigger-fail.json",
    "testdata/sov005/scenarios/misuse-unlinked-fail.json",
    "testdata/sov005/scenarios/residual-unsigned-fail.json",
    "testdata/sov005/scenarios/live-threat-model-claim-fail.json",
    "testdata/sov005/scenarios/stride-coverage-pass.json",
    "testdata/sov005/scenarios/trust-boundary-pass.json",
    "testdata/sov005/scenarios/high-threat-triad-pass.json",
    "testdata/sov005/scenarios/change-trigger-pass.json",
    "testdata/sov005/scenarios/misuse-linked-pass.json",
    "testdata/sov005/scenarios/residual-signed-pass.json",
    "testdata/sov005/scenarios/attack-tree-lab-pass.json",
    "testdata/sov005/audit/threat-model-audit-pass.json",
    "testdata/sov005/harness/threat-model-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$ms = Get-Json "testdata/sov005/scenarios/missing-stride-fail.json"
if ($ms.expectedValidate -ne "FAIL" -or $ms.stridePresent -eq $true) { $failures += "missing-stride-fail invalid" }
$hc = Get-Json "testdata/sov005/scenarios/high-without-control-fail.json"
if ($hc.expectedValidate -ne "FAIL" -or $hc.controlPresent -eq $true) { $failures += "high-without-control-fail invalid" }
$ht = Get-Json "testdata/sov005/scenarios/high-without-test-fail.json"
if ($ht.expectedValidate -ne "FAIL" -or $ht.testPresent -eq $true) { $failures += "high-without-test-fail invalid" }
$ho = Get-Json "testdata/sov005/scenarios/high-without-owner-fail.json"
if ($ho.expectedValidate -ne "FAIL" -or $ho.ownerPresent -eq $true) { $failures += "high-without-owner-fail invalid" }
$nc = Get-Json "testdata/sov005/scenarios/no-change-trigger-fail.json"
if ($nc.expectedValidate -ne "FAIL" -or $nc.materialChangeCovered -eq $true) { $failures += "no-change-trigger-fail invalid" }
$mu = Get-Json "testdata/sov005/scenarios/misuse-unlinked-fail.json"
if ($mu.expectedValidate -ne "FAIL" -or $mu.backlogLinked -eq $true) { $failures += "misuse-unlinked-fail invalid" }
$ru = Get-Json "testdata/sov005/scenarios/residual-unsigned-fail.json"
if ($ru.expectedValidate -ne "FAIL" -or $ru.signed -eq $true) { $failures += "residual-unsigned-fail invalid" }
$lt = Get-Json "testdata/sov005/scenarios/live-threat-model-claim-fail.json"
if ($lt.expectedValidate -ne "FAIL" -or $lt.liveThreatModelClaim -ne $true) { $failures += "live-threat-model-claim-fail invalid" }

$sc = Get-Json "testdata/sov005/scenarios/stride-coverage-pass.json"
if ($sc.expectedValidate -ne "PASS" -or $sc.stridePresent -ne $true) { $failures += "stride-coverage-pass invalid" }
$tb = Get-Json "testdata/sov005/scenarios/trust-boundary-pass.json"
if ($tb.expectedValidate -ne "PASS" -or $tb.inventoryPresent -ne $true) { $failures += "trust-boundary-pass invalid" }
$tri = Get-Json "testdata/sov005/scenarios/high-threat-triad-pass.json"
if ($tri.expectedValidate -ne "PASS" -or $tri.controlPresent -ne $true -or $tri.testPresent -ne $true -or $tri.ownerPresent -ne $true) { $failures += "high-threat-triad-pass invalid" }
$ct = Get-Json "testdata/sov005/scenarios/change-trigger-pass.json"
if ($ct.expectedValidate -ne "PASS" -or $ct.materialChangeCovered -ne $true) { $failures += "change-trigger-pass invalid" }
$ml = Get-Json "testdata/sov005/scenarios/misuse-linked-pass.json"
if ($ml.expectedValidate -ne "PASS" -or $ml.backlogLinked -ne $true) { $failures += "misuse-linked-pass invalid" }
$rs = Get-Json "testdata/sov005/scenarios/residual-signed-pass.json"
if ($rs.expectedValidate -ne "PASS" -or $rs.signed -ne $true) { $failures += "residual-signed-pass invalid" }
$at = Get-Json "testdata/sov005/scenarios/attack-tree-lab-pass.json"
if ($at.expectedValidate -ne "PASS" -or $at.attackTreeLabPresent -ne $true) { $failures += "attack-tree-lab-pass invalid" }

$audit = Get-Json "testdata/sov005/audit/threat-model-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "threat-model-audit-pass invalid" }
$ledger = Get-Json "testdata/sov005/harness/threat-model-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "threat-model-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveThreatModel -eq $true) { $failures += "ledger run $($r.runId) liveThreatModel must be false" }
}

foreach ($rel in @(
    "docs/ADR-SOV-005-threat-modeling.md",
    "docs/SOV_005_JOURNEY_MATRIX.md",
    "docs/SOV_005_STRIDE.md",
    "docs/SOV_005_TRUST_BOUNDARIES.md",
    "docs/SOV_005_ABUSE_CASES.md",
    "docs/SOV_005_HIGH_COVERAGE.md",
    "docs/SOV_005_CHANGE_TRIGGER.md",
    "docs/SOV_005_MISUSE_BACKLOG.md",
    "docs/SOV_005_RESIDUAL_RISK.md",
    "docs/SOV_005_READINESS_CHECKLIST.md",
    "docs/SOV_005_UAT_PACK.md",
    "docs/SOV_005_VERIFICATION.md"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-005"
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

Write-Host "PASS: SOV-005 threat-model pack validate"
exit 0
