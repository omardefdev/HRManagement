# SOV-001: runtime/production control-baseline binding validate.
# Usage:
#   powershell -File scripts/sov001-control-baseline-runtime-validate.ps1

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

if ($m.frameworks.required -ne $true) { $failures += "frameworks.required must be true" }
if ($m.frameworks.localLawTailoringRequired -ne $true) { $failures += "localLawTailoringRequired must be true" }
foreach ($need in @("NIST-CSF", "NIST-800-53B", "ISO-27001", "ASVS-5", "SSDF")) {
    if (@($m.frameworks.crosswalk) -notcontains $need) { $failures += "missing crosswalk $need" }
}
if ($m.impactCategorization.required -ne $true) { $failures += "impactCategorization.required must be true" }
if ($m.controlCoverage.required -ne $true) { $failures += "controlCoverage.required must be true" }
if ($m.controlCoverage.fullCoverageRequired -ne $true) { $failures += "fullCoverageRequired must be true" }
if ($m.controlCoverage.siteReasonRequiredForNa -ne $true) { $failures += "siteReasonRequiredForNa must be true" }
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
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.controlBaselinePolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.controlBaselinePolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$mo = Get-Json "testdata/sov001/scenarios/missing-owner-fail.json"
$me = Get-Json "testdata/sov001/scenarios/missing-evidence-fail.json"
$cg = Get-Json "testdata/sov001/scenarios/coverage-gap-fail.json"
$na = Get-Json "testdata/sov001/scenarios/na-without-site-reason-fail.json"
$up = Get-Json "testdata/sov001/scenarios/undated-poam-fail.json"
$ue = Get-Json "testdata/sov001/scenarios/unsigned-exception-fail.json"
$mb = Get-Json "testdata/sov001/scenarios/missing-boundary-fail.json"
$la = Get-Json "testdata/sov001/scenarios/live-ato-claim-fail.json"

if ($mo.expectedValidate -ne "FAIL" -or $mo.hasOwner -eq $true) { $failures += "missing-owner-fail invalid" }
if ($me.expectedValidate -ne "FAIL" -or $me.hasEvidence -eq $true) { $failures += "missing-evidence-fail invalid" }
if ($cg.expectedValidate -ne "FAIL" -or $cg.statusKnown -eq $true) { $failures += "coverage-gap-fail invalid" }
if ($na.expectedValidate -ne "FAIL" -or $na.siteReasonPresentIfNa -eq $true) { $failures += "na-without-site-reason-fail invalid" }
if ($up.expectedValidate -ne "FAIL" -or $up.datedRemediationPresent -eq $true) { $failures += "undated-poam-fail invalid" }
if ($ue.expectedValidate -ne "FAIL" -or $ue.signed -eq $true) { $failures += "unsigned-exception-fail invalid" }
if ($mb.expectedValidate -ne "FAIL" -or $mb.boundaryPresent -eq $true) { $failures += "missing-boundary-fail invalid" }
if ($la.expectedValidate -ne "FAIL" -or $la.liveAtoClaim -ne $true) { $failures += "live-ato-claim-fail invalid" }

# Positive
$fc = Get-Json "testdata/sov001/scenarios/full-coverage-pass.json"
$oe = Get-Json "testdata/sov001/scenarios/owner-evidence-pass.json"
$dp = Get-Json "testdata/sov001/scenarios/dated-poam-pass.json"
$se = Get-Json "testdata/sov001/scenarios/signed-exception-pass.json"
$bp = Get-Json "testdata/sov001/scenarios/boundary-pass.json"
$isp = Get-Json "testdata/sov001/scenarios/independent-sample-pass.json"
$rt = Get-Json "testdata/sov001/scenarios/reassessment-trigger-pass.json"
$audit = Get-Json "testdata/sov001/audit/baseline-audit-pass.json"
$ledger = Get-Json "testdata/sov001/harness/baseline-run-ledger.json"

if ($fc.expectedValidate -ne "PASS") { $failures += "full-coverage-pass invalid" }
if ($oe.expectedValidate -ne "PASS") { $failures += "owner-evidence-pass invalid" }
if ($dp.expectedValidate -ne "PASS") { $failures += "dated-poam-pass invalid" }
if ($se.expectedValidate -ne "PASS") { $failures += "signed-exception-pass invalid" }
if ($bp.expectedValidate -ne "PASS") { $failures += "boundary-pass invalid" }
if ($isp.expectedValidate -ne "PASS") { $failures += "independent-sample-pass invalid" }
if ($rt.expectedValidate -ne "PASS") { $failures += "reassessment-trigger-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "baseline-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "baseline-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveAto -eq $true) { $failures += "ledger run $($r.runId) liveAto must be false" }
}

# STR-002 catalog: SOV-001 Satisfied; CTRL Blocked-Legal must remain
$catalog = Get-Json "docs/str-legal/control-catalog.json"
$sovBlocker = @($catalog.blockers | Where-Object { $_.id -eq "SOV-001" } | Select-Object -First 1)
if ($sovBlocker.Count -eq 0) { $failures += "STR-002 catalog missing SOV-001 blocker" }
elseif ([string]$sovBlocker[0].status -ne "Satisfied") {
    $failures += "STR-002 catalog SOV-001 must be Satisfied (got $($sovBlocker[0].status))"
}
$blockedLegal = @($catalog.controls | Where-Object { $_.status -eq "Blocked-Legal" })
if ($blockedLegal.Count -lt 1) { $failures += "STR-002 catalog must retain Blocked-Legal controls (not flipped by SOV-001 binding)" }

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
    $sb = $cfg.SovBaseline
    if ($null -eq $sb) { $failures += "$rel missing SovBaseline"; continue }
    if ($sb.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($sb.LiveAtoClaim -eq $true) { $failures += "$rel LiveAtoClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-001"
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

Write-Host "PASS: SOV-001 control-baseline runtime validate"
exit 0
