# STR-005: validate governance matrix, decision/risk logs, exceptions, simulations, Go-Live seats.
# Usage:
#   powershell -File scripts/str005-governance-validate.ps1

param(
    [string]$MatrixPath = "docs/str-governance/governance-matrix.json",
    [string]$DecisionLogPath = "docs/str-governance/decision-log.json",
    [string]$RiskRegisterPath = "docs/str-governance/risk-register.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str005/out",
    [string]$AsOfDate = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$matrixFull = Resolve-RepoPath $MatrixPath
$decFull = Resolve-RepoPath $DecisionLogPath
$riskFull = Resolve-RepoPath $RiskRegisterPath
foreach ($p in @($matrixFull, $decFull, $riskFull)) {
    if (-not (Test-Path $p)) { Write-Error "missing: $p" }
}

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$dec = Get-Content -Raw -Path $decFull | ConvertFrom-Json
$risk = Get-Content -Raw -Path $riskFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "STR-005") { $failures += "card must be STR-005" }
if ($dec.card -ne "STR-005") { $failures += "decision-log card must be STR-005" }
if ($risk.card -ne "STR-005") { $failures += "risk-register card must be STR-005" }

$asOf = if ($AsOfDate) { [datetime]::Parse($AsOfDate) } elseif ($m.asOfDate) { [datetime]::Parse($m.asOfDate) } else { Get-Date }

# Journeys
$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("DecisionRights", "BoardsAndAdr", "ExceptionExpiry", "GoLiveMultiSeat")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

# Boards
$boardIds = @($m.boards | ForEach-Object { $_.id })
foreach ($need in @("Architecture", "Security", "Data", "Change", "Program")) {
    if ($boardIds -notcontains $need) { $failures += "missing board $need" }
}

# Decision rights: owner != reviewer; types cover critical set
$rightTypes = @{}
foreach ($r in $m.decisionRights) {
    if ([string]::IsNullOrWhiteSpace($r.ownerRole) -or [string]::IsNullOrWhiteSpace($r.reviewerRole)) {
        $failures += "decisionRights $($r.type) missing owner/reviewer"
    }
    elseif ($r.ownerRole -eq $r.reviewerRole) {
        $failures += "decisionRights $($r.type) owner equals reviewer"
    }
    $rightTypes[$r.type] = $true
}
foreach ($t in $m.criticalDecisionTypes) {
    if (-not $rightTypes.ContainsKey($t)) { $failures += "missing decisionRights for $t" }
}

# Decision log rows
$fields = @($m.requiredDecisionFields)
$decIds = @{}
foreach ($d in $dec.decisions) {
    foreach ($f in $fields) {
        if ($f -eq "adrRef") { continue }
        if ($null -eq $d.$f -or ($d.$f -is [string] -and [string]::IsNullOrWhiteSpace($d.$f))) {
            $failures += "decision $($d.decisionId) missing $f"
        }
    }
    if ($d.ownerRole -eq $d.reviewerRole) {
        $failures += "decision $($d.decisionId) owner equals reviewer"
    }
    $decIds[$d.decisionId] = $true
}

# Risk register
foreach ($rk in $risk.risks) {
    foreach ($need in @("riskId", "likelihood", "impact", "ownerRole", "mitigation", "linkedDecisionId", "status")) {
        if ($null -eq $rk.$need -or ($rk.$need -is [string] -and [string]::IsNullOrWhiteSpace($rk.$need))) {
            $failures += "risk $($rk.riskId) missing $need"
        }
    }
    if ($rk.linkedDecisionId -and -not $decIds.ContainsKey($rk.linkedDecisionId)) {
        $failures += "risk $($rk.riskId) linkedDecisionId not in decision-log"
    }
}

# Exceptions
$exFields = @($m.requiredExceptionFields)
foreach ($ex in $m.exceptions) {
    foreach ($f in $exFields) {
        if ($null -eq $ex.$f -or ($ex.$f -is [string] -and [string]::IsNullOrWhiteSpace($ex.$f))) {
            $failures += "exception $($ex.exceptionId) missing $f"
        }
    }
    if ($ex.declaredBy -eq $ex.reviewerRole) {
        $failures += "exception $($ex.exceptionId) declaredBy equals reviewerRole"
    }
    if ($ex.expiresOn) {
        $exp = [datetime]::Parse($ex.expiresOn)
        if ($exp.Date -lt $asOf.Date -and $ex.status -ne "Renewed" -and $ex.status -ne "Closed") {
            $failures += "exception $($ex.exceptionId) expired without renew/close"
        }
    }
}

# Blockers
$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($blockerMap["SYD-017"] -ne "Blocked") { $failures += "SYD-017 must be Blocked" }
if ($blockerMap["PGM-001"] -ne "Satisfied") { $failures += "PGM-001 must be Satisfied" }

# Simulations
$simNeed = @{
    "lawChange" = @("scenarioId", "type", "ownerRole", "reviewerRole", "decisionId", "riskId")
    "incident" = @("scenarioId", "type", "ownerRole", "reviewerRole", "decisionId", "riskId", "exceptionId")
    "delay" = @("scenarioId", "type", "ownerRole", "reviewerRole", "decisionId", "riskId")
}
foreach ($key in @("lawChange", "incident", "delay")) {
    $rel = $m.simulationPaths.$key
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $failures += "missing simulation $rel"
        continue
    }
    $sim = Get-Content -Raw -Path $full | ConvertFrom-Json
    foreach ($f in $simNeed[$key]) {
        if ($null -eq $sim.$f -or ($sim.$f -is [string] -and [string]::IsNullOrWhiteSpace($sim.$f))) {
            $failures += "simulation $key missing $f"
        }
    }
    if ($sim.ownerRole -eq $sim.reviewerRole) {
        $failures += "simulation $key owner equals reviewer"
    }
    if ($sim.decisionId -and -not $decIds.ContainsKey($sim.decisionId)) {
        $failures += "simulation $key decisionId not in decision-log"
    }
}

# Go-Live fixtures
$engAliases = @($m.goLiveMultiSeat.engRoleAliases)
$requiredSeats = @($m.goLiveMultiSeat.requiredSeats)

$engRel = $m.goLiveFixturePaths.engOnly
$multiRel = $m.goLiveFixturePaths.multiSeat
$engFull = Resolve-RepoPath $engRel
$multiFull = Resolve-RepoPath $multiRel
if (-not (Test-Path $engFull)) { $failures += "missing eng-only fixture" }
if (-not (Test-Path $multiFull)) { $failures += "missing multi-seat fixture" }

function Test-MultiSeatOk($seats, $required, $engAliases) {
    $set = @($seats | ForEach-Object { $_.ToLowerInvariant() })
    $onlyEng = $true
    foreach ($s in $set) {
        if ($engAliases -notcontains $s) { $onlyEng = $false; break }
    }
    if ($onlyEng -or $set.Count -eq 0) { return $false }
    foreach ($r in $required) {
        if ($set -notcontains $r.ToLowerInvariant()) { return $false }
    }
    return $true
}

if (Test-Path $engFull) {
    $eng = Get-Content -Raw -Path $engFull | ConvertFrom-Json
    if ($eng.expectedValidate -ne "FAIL") {
        $failures += "eng-only fixture must declare expectedValidate FAIL"
    }
    $ok = Test-MultiSeatOk $eng.seats $requiredSeats $engAliases
    if ($ok) {
        $failures += "eng-only fixture incorrectly satisfies multi-seat"
    }
}

if (Test-Path $multiFull) {
    $ms = Get-Content -Raw -Path $multiFull | ConvertFrom-Json
    if ($ms.expectedValidate -ne "PASS") {
        $failures += "multi-seat fixture must declare expectedValidate PASS"
    }
    $ok = Test-MultiSeatOk $ms.seats $requiredSeats $engAliases
    if (-not $ok) {
        $failures += "multi-seat fixture missing required seats"
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-005"
    ok = ($failures.Count -eq 0)
    asOfDate = $asOf.ToString("yyyy-MM-dd")
    decisionCount = @($dec.decisions).Count
    riskCount = @($risk.risks).Count
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

Write-Host "PASS: STR-005 governance validate"
exit 0
