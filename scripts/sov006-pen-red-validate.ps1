# SOV-006: validate pen-red contract matrix and harness fixtures.
param(
    [string]$MatrixPath = "docs/sov-pentest/pen-red-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov006/out"
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

if ($m.card -ne "SOV-006") { $failures += "card must be SOV-006" }

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("ScopeIndependence", "FindingClosure", "PurpleFraudEvidence", "ClaimGate")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.scopeSurfaces.required -ne $true) { $failures += "scopeSurfaces.required must be true" }
$surfaces = @($m.scopeSurfaces.surfaces)
foreach ($need in @("web", "api", "mobile", "identity", "infrastructure", "tenantEscape", "files", "socialAdmin")) {
    if ($surfaces -notcontains $need) { $failures += "scopeSurfaces missing $need" }
}
if ($m.independence.required -ne $true) { $failures += "independence.required must be true" }
if ($m.independence.assessorIndependentRequired -ne $true) { $failures += "assessorIndependentRequired must be true" }
if ($m.findingClosure.required -ne $true) { $failures += "findingClosure.required must be true" }
if ($m.findingClosure.criticalHighZeroOpenRequired -ne $true) { $failures += "criticalHighZeroOpenRequired must be true" }
if ($m.findingClosure.limitedAcceptanceAllowed -ne $true) { $failures += "limitedAcceptanceAllowed must be true" }
if ($m.findingClosure.retestRequired -ne $true) { $failures += "retestRequired must be true" }
if ($m.purpleDetection.required -ne $true) { $failures += "purpleDetection.required must be true" }
if ($m.purpleDetection.socDetectionRequired -ne $true) { $failures += "socDetectionRequired must be true" }
if ($m.purpleDetection.agreedScenariosRequired -ne $true) { $failures += "agreedScenariosRequired must be true" }
if ($m.fraudInsider.required -ne $true) { $failures += "fraudInsider.required must be true" }
if ($m.fraudInsider.payrollFraudRequired -ne $true) { $failures += "payrollFraudRequired must be true" }
if ($m.fraudInsider.insiderRequired -ne $true) { $failures += "insiderRequired must be true" }
if ($m.evidence.required -ne $true) { $failures += "evidence.required must be true" }
if ($m.evidence.signedReportsRequired -ne $true) { $failures += "signedReportsRequired must be true" }
if ($m.evidence.rawEvidenceProtectedRequired -ne $true) { $failures += "rawEvidenceProtectedRequired must be true" }
if ($m.remediation.required -ne $true) { $failures += "remediation.required must be true" }
if ($m.remediation.closureRequired -ne $true) { $failures += "closureRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("findingId", "severity", "surface", "assessorRef", "retestOutcome", "detectionRef", "actor", "occurredAt", "evidenceRef", "outcome")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveIndependentPenAssessmentForbidden -ne $true) { $failures += "liveIndependentPenAssessmentForbidden must be true" }
if ($m.mockSandbox.liveRedTeamExerciseForbidden -ne $true) { $failures += "liveRedTeamExerciseForbidden must be true" }
if ($m.mockSandbox.livePurpleDetectionValidationForbidden -ne $true) { $failures += "livePurpleDetectionValidationForbidden must be true" }
if ($m.mockSandbox.liveRemediationRetestClosureForbidden -ne $true) { $failures += "liveRemediationRetestClosureForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

foreach ($need in @(
    @{ Id = "SOV-001"; Status = "Satisfied" },
    @{ Id = "SOV-004"; Status = "Satisfied" },
    @{ Id = "OBS-003"; Status = "Satisfied" },
    @{ Id = "REL-001"; Status = "Satisfied" },
    @{ Id = "SOV-005"; Status = "Satisfied" },
    @{ Id = "live-independent-pen-assessment"; Status = "Blocked" },
    @{ Id = "live-red-team-exercise"; Status = "Blocked" },
    @{ Id = "live-purple-detection-validation"; Status = "Blocked" },
    @{ Id = "live-remediation-retest-closure"; Status = "Blocked" },
    @{ Id = "NAT-001"; Status = "NotStarted" },
    @{ Id = "NAT-002"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($rel in @(
    "testdata/sov006/scenarios/open-critical-fail.json",
    "testdata/sov006/scenarios/open-high-unaccepted-fail.json",
    "testdata/sov006/scenarios/finding-without-retest-fail.json",
    "testdata/sov006/scenarios/assessor-not-independent-fail.json",
    "testdata/sov006/scenarios/purple-undetected-fail.json",
    "testdata/sov006/scenarios/unsigned-report-fail.json",
    "testdata/sov006/scenarios/unprotected-evidence-fail.json",
    "testdata/sov006/scenarios/live-pen-claim-fail.json",
    "testdata/sov006/scenarios/scope-coverage-pass.json",
    "testdata/sov006/scenarios/independent-assessor-pass.json",
    "testdata/sov006/scenarios/critical-high-closed-pass.json",
    "testdata/sov006/scenarios/retest-pass.json",
    "testdata/sov006/scenarios/purple-detection-pass.json",
    "testdata/sov006/scenarios/fraud-insider-lab-pass.json",
    "testdata/sov006/scenarios/signed-protected-evidence-pass.json",
    "testdata/sov006/audit/pen-red-audit-pass.json",
    "testdata/sov006/harness/pen-red-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$oc = Get-Json "testdata/sov006/scenarios/open-critical-fail.json"
if ($oc.expectedValidate -ne "FAIL" -or $oc.openCritical -ne $true) { $failures += "open-critical-fail invalid" }
$oh = Get-Json "testdata/sov006/scenarios/open-high-unaccepted-fail.json"
if ($oh.expectedValidate -ne "FAIL" -or $oh.openHighUnaccepted -ne $true) { $failures += "open-high-unaccepted-fail invalid" }
$fr = Get-Json "testdata/sov006/scenarios/finding-without-retest-fail.json"
if ($fr.expectedValidate -ne "FAIL" -or $fr.retestPresent -eq $true) { $failures += "finding-without-retest-fail invalid" }
$ai = Get-Json "testdata/sov006/scenarios/assessor-not-independent-fail.json"
if ($ai.expectedValidate -ne "FAIL" -or $ai.assessorIndependent -eq $true) { $failures += "assessor-not-independent-fail invalid" }
$pu = Get-Json "testdata/sov006/scenarios/purple-undetected-fail.json"
if ($pu.expectedValidate -ne "FAIL" -or $pu.socDetected -eq $true) { $failures += "purple-undetected-fail invalid" }
$ur = Get-Json "testdata/sov006/scenarios/unsigned-report-fail.json"
if ($ur.expectedValidate -ne "FAIL" -or $ur.signedReport -eq $true) { $failures += "unsigned-report-fail invalid" }
$ue = Get-Json "testdata/sov006/scenarios/unprotected-evidence-fail.json"
if ($ue.expectedValidate -ne "FAIL" -or $ue.rawProtected -eq $true) { $failures += "unprotected-evidence-fail invalid" }
$lp = Get-Json "testdata/sov006/scenarios/live-pen-claim-fail.json"
if ($lp.expectedValidate -ne "FAIL" -or $lp.livePenClaim -ne $true) { $failures += "live-pen-claim-fail invalid" }

$sc = Get-Json "testdata/sov006/scenarios/scope-coverage-pass.json"
if ($sc.expectedValidate -ne "PASS") { $failures += "scope-coverage-pass invalid" }
$ia = Get-Json "testdata/sov006/scenarios/independent-assessor-pass.json"
if ($ia.expectedValidate -ne "PASS" -or $ia.assessorIndependent -ne $true) { $failures += "independent-assessor-pass invalid" }
$ch = Get-Json "testdata/sov006/scenarios/critical-high-closed-pass.json"
if ($ch.expectedValidate -ne "PASS" -or $ch.openCritical -eq $true -or $ch.openHighUnaccepted -eq $true) { $failures += "critical-high-closed-pass invalid" }
$rt = Get-Json "testdata/sov006/scenarios/retest-pass.json"
if ($rt.expectedValidate -ne "PASS" -or $rt.retestPresent -ne $true) { $failures += "retest-pass invalid" }
$pd = Get-Json "testdata/sov006/scenarios/purple-detection-pass.json"
if ($pd.expectedValidate -ne "PASS" -or $pd.socDetected -ne $true) { $failures += "purple-detection-pass invalid" }
$fi = Get-Json "testdata/sov006/scenarios/fraud-insider-lab-pass.json"
if ($fi.expectedValidate -ne "PASS" -or $fi.payrollFraudCovered -ne $true -or $fi.insiderCovered -ne $true) { $failures += "fraud-insider-lab-pass invalid" }
$se = Get-Json "testdata/sov006/scenarios/signed-protected-evidence-pass.json"
if ($se.expectedValidate -ne "PASS" -or $se.signedReport -ne $true -or $se.rawProtected -ne $true) { $failures += "signed-protected-evidence-pass invalid" }

$audit = Get-Json "testdata/sov006/audit/pen-red-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "pen-red-audit-pass invalid" }
$ledger = Get-Json "testdata/sov006/harness/pen-red-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "pen-red-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.livePen -eq $true) { $failures += "ledger run $($r.runId) livePen must be false" }
}

foreach ($rel in @(
    "docs/ADR-SOV-006-pen-red.md",
    "docs/SOV_006_JOURNEY_MATRIX.md",
    "docs/SOV_006_SCOPE.md",
    "docs/SOV_006_INDEPENDENCE.md",
    "docs/SOV_006_FINDING_CLOSURE.md",
    "docs/SOV_006_PURPLE.md",
    "docs/SOV_006_FRAUD_INSIDER.md",
    "docs/SOV_006_EVIDENCE.md",
    "docs/SOV_006_REMEDIATION.md",
    "docs/SOV_006_READINESS_CHECKLIST.md",
    "docs/SOV_006_UAT_PACK.md",
    "docs/SOV_006_VERIFICATION.md"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-006"
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

Write-Host "PASS: SOV-006 pen-red pack validate"
exit 0
