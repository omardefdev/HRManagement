# MIG-006: runtime/production cutover binding validate.
# Usage:
#   powershell -File scripts/mig006-cutover-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/mig-cutover/cutover-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/mig006/out"
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

if ($m.card -ne "MIG-006") { $failures += "card must be MIG-006" }

foreach ($need in @(
    @{ Id = "MIG-005"; Status = "Satisfied" },
    @{ Id = "OPS-003"; Status = "Satisfied" },
    @{ Id = "REL-001"; Status = "Satisfied" },
    @{ Id = "agency-extract-access"; Status = "Blocked" },
    @{ Id = "live-cutover-perf"; Status = "Blocked" },
    @{ Id = "live-agency-recon"; Status = "Blocked" },
    @{ Id = "live-rehearsal-at-scale"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ([int]$m.minRehearsals -lt 3) { $failures += "minRehearsals must be >= 3" }
$rehearsals = @($m.rehearsals)
if ($rehearsals.Count -lt 3) { $failures += "need >= 3 rehearsals" }
foreach ($r in $rehearsals) {
    if ($r.timed -ne $true) { $failures += "rehearsal $($r.id): timed must be true" }
    if ($r.withinWindow -ne $true) { $failures += "rehearsal $($r.id): withinWindow must be true" }
    if ($r.volumeOk -ne $true) { $failures += "rehearsal $($r.id): volumeOk must be true" }
}

if ($null -eq $m.changeFreeze) { $failures += "changeFreeze missing" }
elseif ($m.changeFreeze.required -ne $true) { $failures += "changeFreeze.required must be true" }

if ($null -eq $m.deltaCapture) { $failures += "deltaCapture missing" }
elseif ($m.deltaCapture.noLossRequired -ne $true) { $failures += "noLossRequired must be true" }

if ($null -eq $m.parallelMode) { $failures += "parallelMode missing" }
else {
    if ($m.parallelMode.read -ne $true) { $failures += "parallelMode.read must be true" }
    if ($m.parallelMode.run -ne $true) { $failures += "parallelMode.run must be true" }
}

if ($null -eq $m.userLock) { $failures += "userLock missing" }
elseif ($m.userLock.requiredBeforeCutover -ne $true) { $failures += "userLock.requiredBeforeCutover must be true" }

if ($null -eq $m.dnsCutover) { $failures += "dnsCutover missing" }
elseif ($m.dnsCutover.liveForbiddenInThisPack -ne $true) {
    $failures += "dnsCutover.liveForbiddenInThisPack must be true"
}

$rb = $m.rollback
if ($null -eq $rb) {
    $failures += "rollback missing"
}
else {
    if ($rb.mustBeExercised -ne $true) { $failures += "rollback.mustBeExercised must be true" }
    if ($rb.paperOnlyForbidden -ne $true) { $failures += "paperOnlyForbidden must be true" }
    if ([double]$rb.windowHours -le 0) { $failures += "rollback.windowHours must be > 0" }
}

$gn = $m.goNoGo
$requiredCriteria = @("rehearsalsComplete", "evidencePackageOk", "rollbackExercised", "deltaNoLoss", "legacySecuredPlan")
if ($null -eq $gn) {
    $failures += "goNoGo missing"
}
else {
    if ($gn.decisionRequired -ne $true) { $failures += "goNoGo.decisionRequired must be true" }
    if (@($gn.criteria).Count -lt 5) { $failures += "goNoGo needs >= 5 criteria" }
    foreach ($need in $requiredCriteria) {
        if (@($gn.criteria) -notcontains $need) { $failures += "goNoGo missing criterion $need" }
    }
}

$leg = $m.legacyAccess
if ($null -eq $leg) {
    $failures += "legacyAccess missing"
}
else {
    if ($leg.readOnlyAfterCutover -ne $true) { $failures += "readOnlyAfterCutover must be true" }
    if ($leg.securedRequired -ne $true) { $failures += "securedRequired must be true" }
}

$pc = $m.postCutoverRecon
if ($null -eq $pc) {
    $failures += "postCutoverRecon missing"
}
else {
    foreach ($need in @("T+0", "T+1", "T+30")) {
        if (@($pc.checkpoints) -notcontains $need) { $failures += "postCutoverRecon missing checkpoint $need" }
    }
}

$sig = $m.signing
if ($null -eq $sig) {
    $failures += "signing missing"
}
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.cutoverPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.cutoverPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative fixtures
$deltaFail = Get-Json "testdata/mig006/scenarios/delta-loss-fail.json"
$paperFail = Get-Json "testdata/mig006/scenarios/paper-rollback-fail.json"

if ($deltaFail.expectedValidate -ne "FAIL") { $failures += "delta-loss fixture expectedValidate must be FAIL" }
if ([int]$deltaFail.lost -le 0) { $failures += "delta-loss fixture must have lost > 0" }

if ($paperFail.expectedValidate -ne "FAIL") { $failures += "paper-rollback fixture expectedValidate must be FAIL" }
if ($paperFail.exercised -eq $true -or $paperFail.paperOnly -ne $true) {
    $failures += "paper-rollback fixture must be unexercised paper-only"
}

# Positive fixtures
$timed = Get-Json "testdata/mig006/rehearsals/timed-rehearsal-pass.json"
$deltaPass = Get-Json "testdata/mig006/scenarios/delta-capture-pass.json"
$rollbackPass = Get-Json "testdata/mig006/scenarios/surprise-rollback-pass.json"
$go = Get-Json "testdata/mig006/decision/go-nogo-decision.json"
$legacy = Get-Json "testdata/mig006/access/legacy-readonly-pass.json"
$t0 = Get-Json "testdata/mig006/recon/recon-t0.json"
$t1 = Get-Json "testdata/mig006/recon/recon-t1.json"
$t30 = Get-Json "testdata/mig006/recon/recon-t30.json"
$ledger = Get-Json "testdata/mig006/harness/cutover-run-ledger.json"

if ($timed.expectedValidate -ne "PASS") { $failures += "timed-rehearsal expectedValidate must be PASS" }
if ($timed.liveAtAgencyScale -eq $true) { $failures += "timed-rehearsal liveAtAgencyScale must be false" }
if ($timed.allTimed -ne $true -or $timed.allWithinWindow -ne $true -or $timed.allVolumeOk -ne $true) {
    $failures += "timed-rehearsal flags must all be true"
}

if ($deltaPass.expectedValidate -ne "PASS") { $failures += "delta-capture expectedValidate must be PASS" }
if ([int]$deltaPass.lost -ne 0) { $failures += "delta-capture lost must be 0" }

if ($rollbackPass.expectedValidate -ne "PASS") { $failures += "surprise-rollback expectedValidate must be PASS" }
if ($rollbackPass.exercised -ne $true -or $rollbackPass.paperOnly -eq $true) {
    $failures += "surprise-rollback must be exercised and not paper-only"
}

if ($go.expectedValidate -ne "PASS") { $failures += "go-nogo expectedValidate must be PASS" }
foreach ($need in $requiredCriteria) {
    if ($go.criteria.$need -ne $true) { $failures += "go-nogo criterion $need must be true" }
}

if ($legacy.expectedValidate -ne "PASS") { $failures += "legacy-readonly expectedValidate must be PASS" }
if ($legacy.liveAgencyCutover -eq $true) { $failures += "legacy-readonly liveAgencyCutover must be false" }
if ($legacy.readOnlyAfterCutover -ne $true -or $legacy.secured -ne $true) {
    $failures += "legacy-readonly must be read-only and secured"
}

foreach ($pair in @(
    @{ Fix = $t0; Cp = "T+0" },
    @{ Fix = $t1; Cp = "T+1" },
    @{ Fix = $t30; Cp = "T+30" }
)) {
    if ($pair.Fix.expectedValidate -ne "PASS") { $failures += "recon $($pair.Cp) expectedValidate must be PASS" }
    if ([string]$pair.Fix.checkpoint -ne $pair.Cp) { $failures += "recon checkpoint must be $($pair.Cp)" }
    if ($pair.Fix.liveAgencyRecon -eq $true) { $failures += "recon $($pair.Cp) liveAgencyRecon must be false" }
}

$runs = @($ledger.runs)
if ($runs.Count -lt 3) { $failures += "cutover-run-ledger needs >= 3 runs" }
$exercisedRuns = @($runs | Where-Object { $_.rollbackExercised -eq $true })
if ($exercisedRuns.Count -lt 1) { $failures += "cutover-run-ledger must include exercised rollback" }

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
    $mc = $cfg.MigCutover
    if ($null -eq $mc) { $failures += "$rel missing MigCutover"; continue }
    if ($mc.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($mc.LiveAgencyCutoverClaim -eq $true) { $failures += "$rel LiveAgencyCutoverClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-006"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    rehearsalCount = $rehearsals.Count
    criteriaCount = @($gn.criteria).Count
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

Write-Host "PASS: MIG-006 cutover runtime validate"
exit 0
