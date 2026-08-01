# SOV-001: validate control-baseline contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/sov001-control-baseline-validate.ps1

param(
    [string]$MatrixPath = "docs/sov-baseline/control-baseline-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov001/out"
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

if ($m.card -ne "SOV-001") { $failures += "card must be SOV-001" }

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("CrosswalkImpact", "CoverageOwnership", "PoamExceptions", "AtoBoundary")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.frameworks.required -ne $true) { $failures += "frameworks.required must be true" }
if ($m.frameworks.localLawTailoringRequired -ne $true) { $failures += "localLawTailoringRequired must be true" }
foreach ($need in @("NIST-CSF", "NIST-800-53B", "ISO-27001", "ASVS-5", "SSDF")) {
    if (@($m.frameworks.crosswalk) -notcontains $need) { $failures += "missing crosswalk $need" }
}
if ($m.impactCategorization.required -ne $true) { $failures += "impactCategorization.required must be true" }
if ($m.controlCoverage.required -ne $true) { $failures += "controlCoverage.required must be true" }
if ($m.controlCoverage.fullCoverageRequired -ne $true) { $failures += "fullCoverageRequired must be true" }
if ($m.controlCoverage.siteReasonRequiredForNa -ne $true) { $failures += "siteReasonRequiredForNa must be true" }
foreach ($need in @("applicable", "implemented", "partial", "not-applicable")) {
    if (@($m.controlCoverage.allowedStatuses) -notcontains $need) { $failures += "missing allowedStatus $need" }
}
if ($m.ownershipEvidence.required -ne $true) { $failures += "ownershipEvidence.required must be true" }
if ($m.ownershipEvidence.ownerRequired -ne $true) { $failures += "ownerRequired must be true" }
if ($m.ownershipEvidence.evidenceRequired -ne $true) { $failures += "evidenceRequired must be true" }
if ($m.ownershipEvidence.testRefRequired -ne $true) { $failures += "testRefRequired must be true" }
if ($m.ownershipEvidence.frequencyRequired -ne $true) { $failures += "frequencyRequired must be true" }
if ($m.poam.required -ne $true) { $failures += "poam.required must be true" }
if ($m.poam.datedRemediationRequired -ne $true) { $failures += "datedRemediationRequired must be true" }
if ($m.exceptions.required -ne $true) { $failures += "exceptions.required must be true" }
if ($m.exceptions.signedExceptionRequired -ne $true) { $failures += "signedExceptionRequired must be true" }
if ($m.authorizationBoundary.required -ne $true) { $failures += "authorizationBoundary.required must be true" }
if ($m.reassessment.required -ne $true) { $failures += "reassessment.required must be true" }
if ($m.reassessment.afterMaterialChangeRequired -ne $true) { $failures += "afterMaterialChangeRequired must be true" }
if ($m.independentAssessment.required -ne $true) { $failures += "independentAssessment.required must be true" }
if ($m.independentAssessment.labStubAllowed -ne $true) { $failures += "labStubAllowed must be true" }
if ($m.independentAssessment.liveAtoForbidden -ne $true) { $failures += "liveAtoForbidden must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("controlId", "owner", "evidenceRef", "status", "outcome", "actor", "occurredAt", "assessmentRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveAtoForbidden -ne $true) { $failures += "mockSandbox.liveAtoForbidden must be true" }
if ($m.mockSandbox.livePoamSystemForbidden -ne $true) { $failures += "livePoamSystemForbidden must be true" }
if ($m.mockSandbox.liveContinuousAssessmentForbidden -ne $true) { $failures += "liveContinuousAssessmentForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

foreach ($need in @(
    @{ Id = "STR-002"; Status = "Satisfied" },
    @{ Id = "SUP-001"; Status = "Satisfied" },
    @{ Id = "GOV-001"; Status = "Satisfied" },
    @{ Id = "live-independent-ato"; Status = "Blocked" },
    @{ Id = "continuous-assessment-workers"; Status = "Blocked" },
    @{ Id = "live-poam-system"; Status = "Blocked" },
    @{ Id = "SYR-001"; Status = "NotStarted" },
    @{ Id = "SOV-002"; Status = "Satisfied" },
    @{ Id = "NAT-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($rel in @(
    "testdata/sov001/scenarios/missing-owner-fail.json",
    "testdata/sov001/scenarios/missing-evidence-fail.json",
    "testdata/sov001/scenarios/coverage-gap-fail.json",
    "testdata/sov001/scenarios/na-without-site-reason-fail.json",
    "testdata/sov001/scenarios/undated-poam-fail.json",
    "testdata/sov001/scenarios/unsigned-exception-fail.json",
    "testdata/sov001/scenarios/missing-boundary-fail.json",
    "testdata/sov001/scenarios/live-ato-claim-fail.json",
    "testdata/sov001/scenarios/full-coverage-pass.json",
    "testdata/sov001/scenarios/owner-evidence-pass.json",
    "testdata/sov001/scenarios/dated-poam-pass.json",
    "testdata/sov001/scenarios/signed-exception-pass.json",
    "testdata/sov001/scenarios/boundary-pass.json",
    "testdata/sov001/scenarios/independent-sample-pass.json",
    "testdata/sov001/scenarios/reassessment-trigger-pass.json",
    "testdata/sov001/audit/baseline-audit-pass.json",
    "testdata/sov001/harness/baseline-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$mo = Get-Json "testdata/sov001/scenarios/missing-owner-fail.json"
if ($mo.expectedValidate -ne "FAIL" -or $mo.hasOwner -eq $true) { $failures += "missing-owner-fail invalid" }
$me = Get-Json "testdata/sov001/scenarios/missing-evidence-fail.json"
if ($me.expectedValidate -ne "FAIL" -or $me.hasEvidence -eq $true) { $failures += "missing-evidence-fail invalid" }
$cg = Get-Json "testdata/sov001/scenarios/coverage-gap-fail.json"
if ($cg.expectedValidate -ne "FAIL" -or $cg.statusKnown -eq $true) { $failures += "coverage-gap-fail invalid" }
$na = Get-Json "testdata/sov001/scenarios/na-without-site-reason-fail.json"
if ($na.expectedValidate -ne "FAIL" -or $na.siteReasonPresentIfNa -eq $true) { $failures += "na-without-site-reason-fail invalid" }
$up = Get-Json "testdata/sov001/scenarios/undated-poam-fail.json"
if ($up.expectedValidate -ne "FAIL" -or $up.datedRemediationPresent -eq $true) { $failures += "undated-poam-fail invalid" }
$ue = Get-Json "testdata/sov001/scenarios/unsigned-exception-fail.json"
if ($ue.expectedValidate -ne "FAIL" -or $ue.signed -eq $true) { $failures += "unsigned-exception-fail invalid" }
$mb = Get-Json "testdata/sov001/scenarios/missing-boundary-fail.json"
if ($mb.expectedValidate -ne "FAIL" -or $mb.boundaryPresent -eq $true) { $failures += "missing-boundary-fail invalid" }
$la = Get-Json "testdata/sov001/scenarios/live-ato-claim-fail.json"
if ($la.expectedValidate -ne "FAIL" -or $la.liveAtoClaim -ne $true) { $failures += "live-ato-claim-fail invalid" }

$fc = Get-Json "testdata/sov001/scenarios/full-coverage-pass.json"
if ($fc.expectedValidate -ne "PASS" -or $fc.statusKnown -ne $true) { $failures += "full-coverage-pass invalid" }
$oe = Get-Json "testdata/sov001/scenarios/owner-evidence-pass.json"
if ($oe.expectedValidate -ne "PASS" -or $oe.hasOwner -ne $true -or $oe.hasEvidence -ne $true) { $failures += "owner-evidence-pass invalid" }
$dp = Get-Json "testdata/sov001/scenarios/dated-poam-pass.json"
if ($dp.expectedValidate -ne "PASS" -or $dp.datedRemediationPresent -ne $true) { $failures += "dated-poam-pass invalid" }
$se = Get-Json "testdata/sov001/scenarios/signed-exception-pass.json"
if ($se.expectedValidate -ne "PASS" -or $se.signed -ne $true) { $failures += "signed-exception-pass invalid" }
$bp = Get-Json "testdata/sov001/scenarios/boundary-pass.json"
if ($bp.expectedValidate -ne "PASS" -or $bp.boundaryPresent -ne $true) { $failures += "boundary-pass invalid" }
$isp = Get-Json "testdata/sov001/scenarios/independent-sample-pass.json"
if ($isp.expectedValidate -ne "PASS" -or $isp.samplePresent -ne $true) { $failures += "independent-sample-pass invalid" }
$rt = Get-Json "testdata/sov001/scenarios/reassessment-trigger-pass.json"
if ($rt.expectedValidate -ne "PASS" -or $rt.triggeredAfterMaterialChange -ne $true) { $failures += "reassessment-trigger-pass invalid" }

$audit = Get-Json "testdata/sov001/audit/baseline-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "baseline-audit-pass invalid" }
$ledger = Get-Json "testdata/sov001/harness/baseline-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "baseline-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveAto -eq $true) { $failures += "ledger run $($r.runId) liveAto must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-001"
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

Write-Host "PASS: SOV-001 control-baseline pack validate"
exit 0
