# MIG-006: validate cutover/rehearsal matrix and harness fixtures.
# Usage:
#   powershell -File scripts/mig006-cutover-validate.ps1

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

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "MIG-006") { $failures += "card must be MIG-006" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("TimedRehearsals", "DeltaIntegrity", "SurpriseRollback", "GoNoGoGate", "LegacySecured", "PostCutoverRecon")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

if ([int]$m.minRehearsals -lt 3) { $failures += "minRehearsals must be >= 3" }
if (@($m.rehearsals).Count -lt [int]$m.minRehearsals) { $failures += "need >= minRehearsals entries" }
foreach ($r in $m.rehearsals) {
    if ($r.timed -ne $true) { $failures += "rehearsal $($r.id) must be timed" }
    if ($r.withinWindow -ne $true) { $failures += "rehearsal $($r.id) must be withinWindow" }
    if ($r.volumeOk -ne $true) { $failures += "rehearsal $($r.id) must be volumeOk" }
}

if (-not $m.changeFreeze.required) { $failures += "changeFreeze.required must be true" }
if (-not $m.deltaCapture.noLossRequired) { $failures += "noLossRequired must be true" }
if (-not $m.parallelMode.read -or -not $m.parallelMode.run) { $failures += "parallel read/run required" }
if (-not $m.userLock.requiredBeforeCutover) { $failures += "userLock required" }
if (-not $m.dnsCutover.labStub -or -not $m.dnsCutover.liveForbiddenInThisPack) {
    $failures += "dns must be labStub and liveForbidden"
}
if (-not $m.rollback.mustBeExercised -or -not $m.rollback.paperOnlyForbidden) {
    $failures += "rollback must be exercised; paper-only forbidden"
}
if (-not $m.goNoGo.decisionRequired) { $failures += "goNoGo decisionRequired" }
if (@($m.goNoGo.criteria).Count -lt 5) { $failures += "goNoGo criteria incomplete" }
if (-not $m.legacyAccess.readOnlyAfterCutover -or -not $m.legacyAccess.securedRequired) {
    $failures += "legacyAccess must require read-only secured"
}
$cps = @($m.postCutoverRecon.checkpoints)
foreach ($need in @("T+0", "T+1", "T+30")) {
    if ($cps -notcontains $need) { $failures += "missing recon checkpoint $need" }
}

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
if ($blockerMap["MIG-005"] -ne "Satisfied") { $failures += "MIG-005 must be Satisfied" }
if ($blockerMap["OPS-003"] -ne "Satisfied") { $failures += "OPS-003 must be Satisfied" }
if ($blockerMap["REL-001"] -ne "Satisfied") { $failures += "REL-001 must be Satisfied" }
if ($blockerMap["agency-extract-access"] -ne "Blocked") { $failures += "agency-extract-access must be Blocked" }
if ($blockerMap["live-cutover-perf"] -ne "Blocked") { $failures += "live-cutover-perf must be Blocked" }
if ($blockerMap["live-agency-recon"] -ne "Blocked") { $failures += "live-agency-recon must be Blocked" }
if ($blockerMap["live-rehearsal-at-scale"] -ne "Blocked") { $failures += "live-rehearsal-at-scale must be Blocked" }

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

$timed = Assert-FixtureExpected $m.simulationPaths.timedRehearsalPass "PASS"
$dPass = Assert-FixtureExpected $m.simulationPaths.deltaCapturePass "PASS"
$dFail = Assert-FixtureExpected $m.simulationPaths.deltaLossFail "FAIL"
$rbPass = Assert-FixtureExpected $m.simulationPaths.surpriseRollbackPass "PASS"
$rbFail = Assert-FixtureExpected $m.simulationPaths.paperRollbackFail "FAIL"
$go = Assert-FixtureExpected $m.simulationPaths.goNogoDecision "PASS"
$leg = Assert-FixtureExpected $m.simulationPaths.legacyReadonlyPass "PASS"
$t0 = Assert-FixtureExpected $m.simulationPaths.reconT0 "PASS"
$t1 = Assert-FixtureExpected $m.simulationPaths.reconT1 "PASS"
$t30 = Assert-FixtureExpected $m.simulationPaths.reconT30 "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.cutoverRunLedger $null

if ($null -ne $timed) {
    if (@($timed.rehearsalIds).Count -lt 3) { $failures += "timed rehearsal need >= 3 ids" }
    if ($timed.allTimed -ne $true -or $timed.allWithinWindow -ne $true -or $timed.allVolumeOk -ne $true) {
        $failures += "timed rehearsal flags incomplete"
    }
    if ($timed.liveAtAgencyScale -eq $true) { $failures += "liveAtAgencyScale must be false" }
}

if ($null -ne $dPass) {
    if ([int]$dPass.lost -ne 0) { $failures += "delta pass must have lost=0" }
    if ([int]$dPass.captured -ne [int]$dPass.changesDuringFreeze) { $failures += "delta pass capture mismatch" }
}
if ($null -ne $dFail) {
    if ([int]$dFail.lost -le 0) { $failures += "delta FAIL must have lost > 0" }
}

if ($null -ne $rbPass) {
    if ($rbPass.exercised -ne $true -or $rbPass.paperOnly -eq $true) {
        $failures += "surprise rollback must be exercised not paper"
    }
}
if ($null -ne $rbFail) {
    if ($rbFail.paperOnly -ne $true -or $rbFail.exercised -eq $true) {
        $failures += "paper rollback FAIL fixture invalid"
    }
}

if ($null -ne $go) {
    foreach ($c in @($m.goNoGo.criteria)) {
        if ($go.criteria.$c -ne $true) { $failures += "go-nogo missing criterion $c" }
    }
}

if ($null -ne $leg) {
    if ($leg.readOnlyAfterCutover -ne $true -or $leg.secured -ne $true) {
        $failures += "legacy readonly incomplete"
    }
    if ($leg.liveAgencyCutover -eq $true) { $failures += "liveAgencyCutover must be false" }
}

foreach ($pair in @(@($t0, "T+0"), @($t1, "T+1"), @($t30, "T+30"))) {
    $obj = $pair[0]; $cp = $pair[1]
    if ($null -eq $obj) { continue }
    if ($obj.checkpoint -ne $cp) { $failures += "recon checkpoint expected $cp" }
    if ([int]$obj.unexplainedGap -ne 0) { $failures += "recon $cp unexplainedGap must be 0" }
    if ($obj.mig005AccountedOk -ne $true) { $failures += "recon $cp mig005AccountedOk required" }
    if ($obj.liveAgencyRecon -eq $true) { $failures += "recon $cp liveAgencyRecon must be false" }
}

if ($null -ne $ledger) {
    if (@($ledger.runs).Count -lt 3) { $failures += "cutover ledger need >= 3 runs" }
    $anyRb = $false
    foreach ($run in $ledger.runs) {
        if ($run.rollbackExercised -eq $true) { $anyRb = $true }
    }
    if (-not $anyRb) { $failures += "ledger must include at least one exercised rollback" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-006"
    ok = ($failures.Count -eq 0)
    rehearsalCount = @($m.rehearsals).Count
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

Write-Host "PASS: MIG-006 cutover validate"
exit 0
