# SOV-004: validate zero-trust contract matrix and harness fixtures.
param(
    [string]$MatrixPath = "docs/sov-zt/zero-trust-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov004/out"
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

if ($m.card -ne "SOV-004") { $failures += "card must be SOV-004" }

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("StandingJit", "WorkstationMfa", "BreakGlassRecert", "ClaimGate")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.noStandingPrivilege.required -ne $true) { $failures += "noStandingPrivilege.required must be true" }
if ($m.noStandingPrivilege.sharedRootForbidden -ne $true) { $failures += "sharedRootForbidden must be true" }
if ($m.noStandingPrivilege.permanentAdminForbidden -ne $true) { $failures += "permanentAdminForbidden must be true" }
if ($m.pamJit.required -ne $true) { $failures += "pamJit.required must be true" }
if ($m.pamJit.temporaryOnly -ne $true) { $failures += "temporaryOnly must be true" }
if ($m.pamJit.reasonRequired -ne $true) { $failures += "reasonRequired must be true" }
if ($m.pamJit.approvalRequired -ne $true) { $failures += "approvalRequired must be true" }
if ($m.pamJit.jeaRequired -ne $true) { $failures += "jeaRequired must be true" }
if ($m.phishingResistantMfa.required -ne $true) { $failures += "phishingResistantMfa.required must be true" }
if ($m.phishingResistantMfa.adminPathsRequired -ne $true) { $failures += "adminPathsRequired must be true" }
if ($m.adminWorkstations.required -ne $true) { $failures += "adminWorkstations.required must be true" }
if ($m.adminWorkstations.ordinaryDeviceForbidden -ne $true) { $failures += "ordinaryDeviceForbidden must be true" }
if ($m.sessionRecord.required -ne $true) { $failures += "sessionRecord.required must be true" }
if ($m.approvals.required -ne $true) { $failures += "approvals.required must be true" }
if ($m.breakGlass.required -ne $true) { $failures += "breakGlass.required must be true" }
if ($m.breakGlass.immediateAlertRequired -ne $true) { $failures += "immediateAlertRequired must be true" }
if ($m.breakGlass.postUseReviewRequired -ne $true) { $failures += "postUseReviewRequired must be true" }
if ($m.serviceAdminSeparation.required -ne $true) { $failures += "serviceAdminSeparation.required must be true" }
if ($m.privilegedTargets.required -ne $true) { $failures += "privilegedTargets.required must be true" }
$targets = @($m.privilegedTargets.targets)
foreach ($need in @("database", "cloud", "k8s", "keycloak")) {
    if ($targets -notcontains $need) { $failures += "privilegedTargets missing $need" }
}
if ($m.accessRecertification.required -ne $true) { $failures += "accessRecertification.required must be true" }
if ($m.accessRecertification.quarterlyRequired -ne $true) { $failures += "quarterlyRequired must be true" }
if ($m.accessRecertification.dormantOrphanZeroRequired -ne $true) { $failures += "dormantOrphanZeroRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("principalId", "privilege", "reason", "approver", "actor", "occurredAt", "evidenceRef", "outcome", "sessionRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.livePamForbidden -ne $true) { $failures += "livePamForbidden must be true" }
if ($m.mockSandbox.liveAdminWorkstationForbidden -ne $true) { $failures += "liveAdminWorkstationForbidden must be true" }
if ($m.mockSandbox.liveBreakGlassSocForbidden -ne $true) { $failures += "liveBreakGlassSocForbidden must be true" }
if ($m.mockSandbox.liveAccessRecertificationForbidden -ne $true) { $failures += "liveAccessRecertificationForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

foreach ($need in @(
    @{ Id = "IAM-003"; Status = "Satisfied" },
    @{ Id = "IAM-004"; Status = "Satisfied" },
    @{ Id = "SOV-003"; Status = "Satisfied" },
    @{ Id = "live-pam-workers"; Status = "Blocked" },
    @{ Id = "live-admin-workstation-enforcement"; Status = "Blocked" },
    @{ Id = "live-break-glass-soc"; Status = "Blocked" },
    @{ Id = "live-access-recertification"; Status = "Blocked" },
    @{ Id = "SOV-005"; Status = "Satisfied" },
    @{ Id = "NAT-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($rel in @(
    "testdata/sov004/scenarios/standing-privilege-fail.json",
    "testdata/sov004/scenarios/shared-root-fail.json",
    "testdata/sov004/scenarios/jit-without-approval-fail.json",
    "testdata/sov004/scenarios/jit-without-reason-fail.json",
    "testdata/sov004/scenarios/admin-from-ordinary-device-fail.json",
    "testdata/sov004/scenarios/break-glass-no-alert-fail.json",
    "testdata/sov004/scenarios/dormant-access-fail.json",
    "testdata/sov004/scenarios/live-pam-claim-fail.json",
    "testdata/sov004/scenarios/no-standing-pass.json",
    "testdata/sov004/scenarios/jit-approved-pass.json",
    "testdata/sov004/scenarios/phishing-mfa-pass.json",
    "testdata/sov004/scenarios/admin-workstation-pass.json",
    "testdata/sov004/scenarios/break-glass-alert-review-pass.json",
    "testdata/sov004/scenarios/session-record-pass.json",
    "testdata/sov004/scenarios/quarterly-recert-pass.json",
    "testdata/sov004/audit/zero-trust-audit-pass.json",
    "testdata/sov004/harness/zero-trust-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$sp = Get-Json "testdata/sov004/scenarios/standing-privilege-fail.json"
if ($sp.expectedValidate -ne "FAIL" -or $sp.hasStanding -ne $true) { $failures += "standing-privilege-fail invalid" }
$sr = Get-Json "testdata/sov004/scenarios/shared-root-fail.json"
if ($sr.expectedValidate -ne "FAIL" -or $sr.sharedRoot -ne $true) { $failures += "shared-root-fail invalid" }
$ja = Get-Json "testdata/sov004/scenarios/jit-without-approval-fail.json"
if ($ja.expectedValidate -ne "FAIL" -or $ja.approved -eq $true) { $failures += "jit-without-approval-fail invalid" }
$jr = Get-Json "testdata/sov004/scenarios/jit-without-reason-fail.json"
if ($jr.expectedValidate -ne "FAIL" -or $jr.reasonPresent -eq $true) { $failures += "jit-without-reason-fail invalid" }
$od = Get-Json "testdata/sov004/scenarios/admin-from-ordinary-device-fail.json"
if ($od.expectedValidate -ne "FAIL" -or $od.fromOrdinaryDevice -ne $true) { $failures += "admin-from-ordinary-device-fail invalid" }
$bg = Get-Json "testdata/sov004/scenarios/break-glass-no-alert-fail.json"
if ($bg.expectedValidate -ne "FAIL" -or $bg.alerted -eq $true) { $failures += "break-glass-no-alert-fail invalid" }
$da = Get-Json "testdata/sov004/scenarios/dormant-access-fail.json"
if ($da.expectedValidate -ne "FAIL" -or $da.dormantOrphanPresent -ne $true) { $failures += "dormant-access-fail invalid" }
$lp = Get-Json "testdata/sov004/scenarios/live-pam-claim-fail.json"
if ($lp.expectedValidate -ne "FAIL" -or $lp.livePamClaim -ne $true) { $failures += "live-pam-claim-fail invalid" }

$ns = Get-Json "testdata/sov004/scenarios/no-standing-pass.json"
if ($ns.expectedValidate -ne "PASS" -or $ns.hasStanding -eq $true -or $ns.sharedRoot -eq $true) { $failures += "no-standing-pass invalid" }
$jp = Get-Json "testdata/sov004/scenarios/jit-approved-pass.json"
if ($jp.expectedValidate -ne "PASS" -or $jp.approved -ne $true -or $jp.reasonPresent -ne $true) { $failures += "jit-approved-pass invalid" }
$pm = Get-Json "testdata/sov004/scenarios/phishing-mfa-pass.json"
if ($pm.expectedValidate -ne "PASS" -or $pm.phishingResistant -ne $true) { $failures += "phishing-mfa-pass invalid" }
$aw = Get-Json "testdata/sov004/scenarios/admin-workstation-pass.json"
if ($aw.expectedValidate -ne "PASS" -or $aw.fromOrdinaryDevice -eq $true) { $failures += "admin-workstation-pass invalid" }
$bp = Get-Json "testdata/sov004/scenarios/break-glass-alert-review-pass.json"
if ($bp.expectedValidate -ne "PASS" -or $bp.alerted -ne $true -or $bp.reviewed -ne $true) { $failures += "break-glass-alert-review-pass invalid" }
$ss = Get-Json "testdata/sov004/scenarios/session-record-pass.json"
if ($ss.expectedValidate -ne "PASS" -or $ss.recorded -ne $true) { $failures += "session-record-pass invalid" }
$qr = Get-Json "testdata/sov004/scenarios/quarterly-recert-pass.json"
if ($qr.expectedValidate -ne "PASS" -or $qr.dormantOrphanPresent -eq $true) { $failures += "quarterly-recert-pass invalid" }

$audit = Get-Json "testdata/sov004/audit/zero-trust-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "zero-trust-audit-pass invalid" }
$ledger = Get-Json "testdata/sov004/harness/zero-trust-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "zero-trust-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.livePam -eq $true) { $failures += "ledger run $($r.runId) livePam must be false" }
}

foreach ($rel in @(
    "docs/ADR-SOV-004-zero-trust.md",
    "docs/SOV_004_JOURNEY_MATRIX.md",
    "docs/SOV_004_NO_STANDING.md",
    "docs/SOV_004_PAM_JIT.md",
    "docs/SOV_004_MFA.md",
    "docs/SOV_004_ADMIN_WORKSTATION.md",
    "docs/SOV_004_BREAK_GLASS.md",
    "docs/SOV_004_RECERTIFICATION.md",
    "docs/SOV_004_READINESS_CHECKLIST.md",
    "docs/SOV_004_UAT_PACK.md",
    "docs/SOV_004_VERIFICATION.md"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-004"
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

Write-Host "PASS: SOV-004 zero-trust pack validate"
exit 0
