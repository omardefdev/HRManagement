# MIG-007: validate agency onboarding matrix and harness fixtures.
# Usage:
#   powershell -File scripts/mig007-onboarding-validate.ps1

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

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "MIG-007") { $failures += "card must be MIG-007" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("ReadinessChecklist", "NoForkPlaybook", "WaveSignOffGate", "DualAgencyE2E", "LeadTimeDefects", "ExitRollback")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

$gateIds = @($m.readinessGates | ForEach-Object { $_.id })
foreach ($need in @("tenant", "config", "roles", "integrations", "data", "training", "support")) {
    if ($gateIds -notcontains $need) { $failures += "missing readiness gate $need" }
}
foreach ($g in $m.readinessGates) {
    if ($g.mandatory -ne $true) { $failures += "gate $($g.id) must be mandatory" }
}

if (-not $m.playbook.noforkRequired) { $failures += "noforkRequired must be true" }
if (@($m.playbook.standardSteps).Count -lt 5) { $failures += "playbook steps too few" }
if (-not $m.sandboxCertification.labStub -or -not $m.sandboxCertification.livePortalForbidden) {
    $failures += "sandbox must be labStub and livePortalForbidden"
}
if (-not $m.waveCriteria.signOffsRequiredBeforeWave) { $failures += "signOffsRequiredBeforeWave must be true" }
if (@($m.waveCriteria.requiredSignOffRoles).Count -lt 3) { $failures += "wave sign-off roles incomplete" }

$owners = @($m.requiredOwners)
foreach ($need in @("data-owner", "security-owner", "ops-owner")) {
    if ($owners -notcontains $need) { $failures += "missing required owner $need" }
}

if ([int]$m.telemetry.leadTimeTargetDays -le 0) { $failures += "leadTimeTargetDays invalid" }
if ([int]$m.telemetry.defectBudget -lt 0) { $failures += "defectBudget invalid" }
if (-not $m.exitRollback.required) { $failures += "exitRollback.required must be true" }

if (@($m.agencies).Count -lt 2) { $failures += "need >= 2 lab agencies" }
$agencyIds = @($m.agencies | ForEach-Object { $_.id })
if ($agencyIds[0] -eq $agencyIds[1]) { $failures += "agencies must be distinct" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace($sig.contentHash)) { $failures += "signing contentHash required" }
if ($sig.signedByRole -eq $sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($blockerMap["MIG-006"] -ne "Satisfied") { $failures += "MIG-006 must be Satisfied" }
if ($blockerMap["STR-004"] -ne "Satisfied") { $failures += "STR-004 must be Satisfied" }
if ($blockerMap["EXT-006"] -ne "NotStarted") { $failures += "EXT-006 must be NotStarted" }
if ($blockerMap["PROD-002"] -ne "Satisfied") { $failures += "PROD-002 must be Satisfied" }
if ($blockerMap["ONB-001"] -ne "Satisfied") { $failures += "ONB-001 must be Satisfied" }
if ($blockerMap["agency-extract-access"] -ne "Blocked") { $failures += "agency-extract-access must be Blocked" }
if ($blockerMap["live-dual-agency-onboard"] -ne "Blocked") { $failures += "live-dual-agency-onboard must be Blocked" }

function Assert-FixtureExpected([string]$rel, [string]$expect) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $script:failures += "missing fixture $rel"
        return $null
    }
    $obj = Get-Content -Raw -Path $full | ConvertFrom-Json
    if (-not [string]::IsNullOrEmpty($expect) -and $obj.expectedValidate -ne $expect) {
        $script:failures += "fixture $rel expectedValidate must be $expect"
    }
    return $obj
}

$chk = Assert-FixtureExpected $m.simulationPaths.readinessChecklistPass "PASS"
$wave = Assert-FixtureExpected $m.simulationPaths.waveWithoutSignoffFail "FAIL"
$fork = Assert-FixtureExpected $m.simulationPaths.forkAttemptFail "FAIL"
$a = Assert-FixtureExpected $m.simulationPaths.agencyAE2E "PASS"
$b = Assert-FixtureExpected $m.simulationPaths.agencyBE2E "PASS"
$lt = Assert-FixtureExpected $m.simulationPaths.leadTimePass "PASS"
$def = Assert-FixtureExpected $m.simulationPaths.defectsTelemetry "PASS"
$exit = Assert-FixtureExpected $m.simulationPaths.exitRollbackPass "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.onboardingRunLedger $null

if ($null -ne $chk) {
    if ($chk.allMandatoryOk -ne $true) { $failures += "checklist allMandatoryOk required" }
    foreach ($gid in $gateIds) {
        if ($chk.gates.$gid -ne $true) { $failures += "checklist gate $gid not ok" }
    }
}

if ($null -ne $wave) {
    if ($wave.waveStarted -ne $true -or $wave.signOffsComplete -eq $true) {
        $failures += "wave FAIL fixture must start wave without complete signoffs"
    }
}

if ($null -ne $fork) {
    if ($fork.proposedFork -ne $true) { $failures += "fork FAIL must propose fork" }
}

function Assert-AgencyE2E($obj, $expectedId) {
    if ($null -eq $obj) { return }
    if ($obj.agencyId -ne $expectedId) { $script:failures += "agency id expected $expectedId" }
    if ($obj.forkUsed -eq $true) { $script:failures += "$expectedId must not use fork" }
    if ($obj.readinessAllOk -ne $true -or $obj.signOffsComplete -ne $true) {
        $script:failures += "$expectedId readiness/signoffs incomplete"
    }
    if ($obj.liveOnboard -eq $true) { $script:failures += "$expectedId liveOnboard must be false" }
    foreach ($o in $script:owners) {
        $val = $obj.owners.$o
        if ([string]::IsNullOrWhiteSpace([string]$val)) {
            $script:failures += "$expectedId missing owner $o"
        }
    }
}

Assert-AgencyE2E $a "lab-agency-a"
Assert-AgencyE2E $b "lab-agency-b"
if ($null -ne $a -and $null -ne $b -and $a.profile -eq $b.profile) {
    $failures += "agency profiles must differ"
}

if ($null -ne $lt) {
    if ([int]$lt.leadTimeDays -gt [int]$m.telemetry.leadTimeTargetDays) {
        $failures += "lead time exceeds target"
    }
    if ($lt.withinTarget -ne $true) { $failures += "lead time withinTarget required" }
    if ($lt.liveMeasured -eq $true) { $failures += "lead time liveMeasured must be false" }
}

if ($null -ne $def) {
    if ([int]$def.defectCount -gt [int]$m.telemetry.defectBudget) {
        $failures += "defects exceed budget"
    }
    if ($def.withinBudget -ne $true) { $failures += "defects withinBudget required" }
    if ($def.liveMeasured -eq $true) { $failures += "defects liveMeasured must be false" }
}

if ($null -ne $exit) {
    if ($exit.exercised -ne $true -or $exit.auditTrailKept -ne $true) {
        $failures += "exit rollback must be exercised with audit trail"
    }
}

if ($null -ne $ledger) {
    if (@($ledger.runs).Count -lt 2) { $failures += "onboarding ledger need >= 2 runs" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-007"
    ok = ($failures.Count -eq 0)
    agencyCount = @($m.agencies).Count
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: MIG-007 onboarding validate"
exit 0
