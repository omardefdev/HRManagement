# MIG-007: runtime/production onboarding binding validate.
# Usage:
#   powershell -File scripts/mig007-onboarding-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/mig-onboarding/onboarding-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/mig007/out"
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

if ($m.card -ne "MIG-007") { $failures += "card must be MIG-007" }

foreach ($need in @(
    @{ Id = "MIG-006"; Status = "Satisfied" },
    @{ Id = "STR-004"; Status = "Satisfied" },
    @{ Id = "EXT-006"; Status = "NotStarted" },
    @{ Id = "PROD-002"; Status = "Satisfied" },
    @{ Id = "ONB-001"; Status = "Satisfied" },
    @{ Id = "agency-extract-access"; Status = "Blocked" },
    @{ Id = "live-dual-agency-onboard"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

$requiredGates = @("tenant", "config", "roles", "integrations", "data", "training", "support")
$gates = @($m.readinessGates)
if ($gates.Count -lt 7) { $failures += "need >= 7 readinessGates" }
foreach ($need in $requiredGates) {
    $g = @($gates | Where-Object { $_.id -eq $need } | Select-Object -First 1)
    if ($g.Count -eq 0) { $failures += "missing readiness gate $need"; continue }
    if ($g[0].mandatory -ne $true) { $failures += "gate ${need}: mandatory must be true" }
}

if ($null -eq $m.playbook) { $failures += "playbook missing" }
else {
    if ($m.playbook.noforkRequired -ne $true) { $failures += "noforkRequired must be true" }
    if (@($m.playbook.standardSteps).Count -lt 5) { $failures += "playbook steps too few" }
}

if ($null -eq $m.sandboxCertification) { $failures += "sandboxCertification missing" }
else {
    if ($m.sandboxCertification.labStub -ne $true) { $failures += "sandboxCertification.labStub must be true" }
    if ($m.sandboxCertification.livePortalForbidden -ne $true) { $failures += "livePortalForbidden must be true" }
}

$requiredSignOffRoles = @("agency-data-owner", "security-owner", "ops-owner", "onboarding-office")
if ($null -eq $m.waveCriteria) { $failures += "waveCriteria missing" }
else {
    if ($m.waveCriteria.signOffsRequiredBeforeWave -ne $true) { $failures += "signOffsRequiredBeforeWave must be true" }
    if (@($m.waveCriteria.requiredSignOffRoles).Count -lt 3) { $failures += "wave sign-off roles incomplete" }
    foreach ($need in $requiredSignOffRoles) {
        if (@($m.waveCriteria.requiredSignOffRoles) -notcontains $need) {
            $failures += "wave missing sign-off role $need"
        }
    }
}

foreach ($need in @("data-owner", "security-owner", "ops-owner")) {
    if (@($m.requiredOwners) -notcontains $need) { $failures += "missing required owner $need" }
}

if ($null -eq $m.telemetry) { $failures += "telemetry missing" }
else {
    if ([int]$m.telemetry.leadTimeTargetDays -le 0) { $failures += "leadTimeTargetDays must be > 0" }
    if ([int]$m.telemetry.defectBudget -lt 0) { $failures += "defectBudget must be >= 0" }
}

if ($null -eq $m.exitRollback) { $failures += "exitRollback missing" }
else {
    if ($m.exitRollback.required -ne $true) { $failures += "exitRollback.required must be true" }
    foreach ($need in @("exit-incomplete", "rollback-to-pre-wave")) {
        if (@($m.exitRollback.modes) -notcontains $need) { $failures += "exitRollback missing mode $need" }
    }
}

$agencies = @($m.agencies)
if ($agencies.Count -lt 2) { $failures += "need >= 2 lab agencies" }
elseif ([string]$agencies[0].id -eq [string]$agencies[1].id) { $failures += "agencies must be distinct" }

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
    $version = $sig.onboardingPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.onboardingPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative fixtures
$forkFail = Get-Json "testdata/mig007/scenarios/fork-attempt-fail.json"
$waveFail = Get-Json "testdata/mig007/scenarios/wave-without-signoff-fail.json"

if ($forkFail.expectedValidate -ne "FAIL") { $failures += "fork-attempt fixture expectedValidate must be FAIL" }
if ($forkFail.proposedFork -ne $true) { $failures += "fork-attempt fixture must propose fork" }

if ($waveFail.expectedValidate -ne "FAIL") { $failures += "wave-without-signoff fixture expectedValidate must be FAIL" }
if ($waveFail.waveStarted -ne $true -or $waveFail.signOffsComplete -eq $true) {
    $failures += "wave-without-signoff fixture must start wave without complete sign-offs"
}

# Positive fixtures
$checklist = Get-Json "testdata/mig007/checklist/readiness-checklist-pass.json"
$agencyA = Get-Json "testdata/mig007/agencies/agency-a-e2e.json"
$agencyB = Get-Json "testdata/mig007/agencies/agency-b-e2e.json"
$lead = Get-Json "testdata/mig007/metrics/lead-time-pass.json"
$defects = Get-Json "testdata/mig007/metrics/defects-telemetry.json"
$exitPass = Get-Json "testdata/mig007/scenarios/exit-rollback-pass.json"
$ledger = Get-Json "testdata/mig007/harness/onboarding-run-ledger.json"

if ($checklist.expectedValidate -ne "PASS") { $failures += "readiness-checklist expectedValidate must be PASS" }
if ($checklist.allMandatoryOk -ne $true) { $failures += "readiness-checklist allMandatoryOk must be true" }

foreach ($pair in @(
    @{ Fix = $agencyA; Id = "lab-agency-a" },
    @{ Fix = $agencyB; Id = "lab-agency-b" }
)) {
    if ($pair.Fix.expectedValidate -ne "PASS") { $failures += "$($pair.Id) expectedValidate must be PASS" }
    if ([string]$pair.Fix.agencyId -ne $pair.Id) { $failures += "agency fixture id must be $($pair.Id)" }
    if ($pair.Fix.liveOnboard -eq $true) { $failures += "$($pair.Id) liveOnboard must be false" }
    if ($pair.Fix.forkUsed -eq $true) { $failures += "$($pair.Id) forkUsed must be false" }
    if ($pair.Fix.signOffsComplete -ne $true) { $failures += "$($pair.Id) signOffsComplete must be true" }
}

if ($lead.expectedValidate -ne "PASS") { $failures += "lead-time expectedValidate must be PASS" }
if ($lead.liveMeasured -eq $true) { $failures += "lead-time liveMeasured must be false" }
if ($lead.withinTarget -ne $true) { $failures += "lead-time withinTarget must be true" }

if ($defects.expectedValidate -ne "PASS") { $failures += "defects-telemetry expectedValidate must be PASS" }
if ($defects.withinBudget -ne $true) { $failures += "defects-telemetry withinBudget must be true" }
if ($defects.liveMeasured -eq $true) { $failures += "defects-telemetry liveMeasured must be false" }

if ($exitPass.expectedValidate -ne "PASS") { $failures += "exit-rollback expectedValidate must be PASS" }
if ($exitPass.exercised -ne $true) { $failures += "exit-rollback must be exercised" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "onboarding-run-ledger needs >= 2 runs" }
$runAgencies = @($runs | ForEach-Object { $_.agencyId } | Select-Object -Unique)
if ($runAgencies.Count -lt 2) { $failures += "onboarding-run-ledger must cover >= 2 agencies" }

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
    $mo = $cfg.MigOnboarding
    if ($null -eq $mo) { $failures += "$rel missing MigOnboarding"; continue }
    if ($mo.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($mo.LiveDualAgencyOnboardClaim -eq $true) { $failures += "$rel LiveDualAgencyOnboardClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-007"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    gateCount = $gates.Count
    agencyCount = $agencies.Count
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

Write-Host "PASS: MIG-007 onboarding runtime validate"
exit 0
