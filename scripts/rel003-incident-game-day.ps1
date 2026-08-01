# REL-003: synthetic incident game day — compute MTTA/MTTR from scenario.json
# Exit 1 if ack/resolve missing or MTTA/MTTR miss target without waiverId.
# Usage:
#   powershell -File scripts/rel003-incident-game-day.ps1
#   pwsh -File scripts/rel003-incident-game-day.ps1

param(
    [string]$ScenarioPath = "testdata/rel003/game-day/scenario.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/rel003/game-day/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$scenarioFull = Join-Path $RepoRoot $ScenarioPath
if (-not (Test-Path $scenarioFull)) {
    Write-Error "scenario not found: $scenarioFull"
}

$s = Get-Content -Raw -Path $scenarioFull | ConvertFrom-Json
$failures = @()

if (-not $s.timeline.detectedAt) { $failures += "missing detectedAt" }
if (-not $s.timeline.acknowledgedAt) { $failures += "missing acknowledgedAt" }
if (-not $s.timeline.resolvedAt) { $failures += "missing resolvedAt" }

$detected = [datetime]::Parse($s.timeline.detectedAt, $null, [System.Globalization.DateTimeStyles]::RoundtripKind)
$acked = [datetime]::Parse($s.timeline.acknowledgedAt, $null, [System.Globalization.DateTimeStyles]::RoundtripKind)
$resolved = [datetime]::Parse($s.timeline.resolvedAt, $null, [System.Globalization.DateTimeStyles]::RoundtripKind)

$mtta = ($acked - $detected).TotalMinutes
$mttr = ($resolved - $detected).TotalMinutes

if ($mtta -lt 0) { $failures += "acknowledgedAt before detectedAt" }
if ($mttr -lt 0) { $failures += "resolvedAt before detectedAt" }
if ($mttr -lt $mtta) { $failures += "resolvedAt before acknowledgedAt span inconsistent" }

$mttaTarget = [double]$s.mttaTargetMinutes
$mttrTarget = [double]$s.mttrTargetMinutes
$mttaPass = $mtta -le $mttaTarget
$mttrPass = $mttr -le $mttrTarget
$waiver = $s.waiverId

if (-not $mttaPass -and -not $waiver) {
    $failures += ("MTTA {0:N1}m exceeds target {1}m without waiver" -f $mtta, $mttaTarget)
}
if (-not $mttrPass -and -not $waiver) {
    $failures += ("MTTR {0:N1}m exceeds target {1}m without waiver" -f $mttr, $mttrTarget)
}

$ok = ($failures.Count -eq 0)
$ledger = [ordered]@{
    card               = "REL-003"
    scenarioId         = $s.scenarioId
    severity           = $s.severity
    serviceId          = $s.serviceId
    detectedAt         = $s.timeline.detectedAt
    acknowledgedAt     = $s.timeline.acknowledgedAt
    resolvedAt         = $s.timeline.resolvedAt
    mttaMinutes        = [math]::Round($mtta, 2)
    mttrMinutes        = [math]::Round($mttr, 2)
    mttaTargetMinutes  = $mttaTarget
    mttrTargetMinutes  = $mttrTarget
    mttaPass           = [bool]$mttaPass
    mttrPass           = [bool]$mttrPass
    waiverId           = $waiver
    ok                 = $ok
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$ledgerPath = Join-Path $outFull "mtta-mttr-ledger.json"
($ledger | ConvertTo-Json -Depth 5) | Set-Content -Path $ledgerPath -Encoding utf8
Write-Host "Wrote $ledgerPath"

if (-not $ok) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: REL-003 game day MTTA/MTTR"
exit 0
