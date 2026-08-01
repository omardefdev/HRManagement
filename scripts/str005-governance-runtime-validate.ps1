# STR-005: runtime/production program governance binding validate.
# Usage:
#   powershell -File scripts/str005-governance-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/str-governance/governance-matrix.json",
    [string]$DecisionLogPath = "docs/str-governance/decision-log.json",
    [string]$RiskRegisterPath = "docs/str-governance/risk-register.json",
    [string]$MultiSeatPath = "testdata/str005/go-live/multi-seat.json",
    [string]$EngOnlyPath = "testdata/str005/go-live/eng-only.json",
    [string]$OperationalEvidencePath = "src/Shared/HRManagement.ServiceDefaults/ProgramGovernance/go-live-evidence.json",
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

function Get-Json([string]$rel) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { throw "missing $rel ($full)" }
    return Get-Content -Raw -Path $full | ConvertFrom-Json
}

$failures = @()
$now = if ($AsOfDate) { [datetime]::Parse($AsOfDate).ToUniversalTime().Date } else { [datetime]::UtcNow.Date }

$m = Get-Json $MatrixPath
$decisions = Get-Json $DecisionLogPath
$risks = Get-Json $RiskRegisterPath
$multi = Get-Json $MultiSeatPath
$engOnly = Get-Json $EngOnlyPath
$opsEvidence = Get-Json $OperationalEvidencePath

if ($m.card -ne "STR-005") { $failures += "card must be STR-005" }
if ($decisions.card -ne "STR-005") { $failures += "decision-log card must be STR-005" }
if ($risks.card -ne "STR-005") { $failures += "risk-register card must be STR-005" }

foreach ($need in @(
    @{ Id = "SYD-017"; Status = "Blocked" },
    @{ Id = "PGM-001"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($ex in @($m.exceptions)) {
    if ([string]$ex.status -ne "Declared") { continue }
    if (-not $ex.expiresOn) {
        $failures += "$($ex.exceptionId) Declared exception missing expiresOn"
        continue
    }
    $expires = [datetime]::Parse([string]$ex.expiresOn).ToUniversalTime().Date
    if ($expires -lt $now) {
        $failures += "$($ex.exceptionId) Declared exception expired on $($ex.expiresOn)"
    }
}

$critical = @($m.criticalDecisionTypes)
foreach ($d in @($decisions.decisions)) {
    $isCritical = ($critical.Count -eq 0) -or ($critical -contains [string]$d.type)
    if (-not $isCritical) { continue }
    if (-not $d.ownerRole -or -not $d.reviewerRole) {
        $failures += "$($d.decisionId) critical decision missing owner/reviewer"
        continue
    }
    if ([string]$d.ownerRole -eq [string]$d.reviewerRole) {
        $failures += "$($d.decisionId) ownerRole must differ from reviewerRole"
    }
}

function Test-GoLiveSeats($evidence, [string]$label) {
    $localFailures = @()
    $rules = $m.goLiveMultiSeat
    if ($null -eq $rules) {
        $localFailures += "${label} matrix missing goLiveMultiSeat"
        return $localFailures
    }
    $seats = @($evidence.seats | Where-Object { $_ -and "$_".Trim() })
    if ($seats.Count -eq 0) {
        $localFailures += "${label} no seats"
        return $localFailures
    }
    $engAliases = @($rules.engRoleAliases)
    if ($engAliases.Count -eq 0) { $engAliases = @("engineering", "eng", "developer", "dev") }
    $nonEng = @($seats | Where-Object {
            $seat = "$_"
            -not ($engAliases | Where-Object { $_ -eq $seat -or [string]::Equals($_, $seat, [StringComparison]::OrdinalIgnoreCase) })
        })
    if ($nonEng.Count -eq 0) {
        $localFailures += "${label} eng-only seats forbidden"
    }
    foreach ($required in @($rules.requiredSeats)) {
        $hit = @($seats | Where-Object { [string]::Equals($_, $required, [StringComparison]::OrdinalIgnoreCase) })
        if ($hit.Count -eq 0) { $localFailures += "${label} missing required seat '$required'" }
    }
    return $localFailures
}

$engFails = Test-GoLiveSeats $engOnly "eng-only"
if ($engFails.Count -eq 0) {
    $failures += "eng-only fixture must FAIL Go-Live seat rules"
}

$multiFails = Test-GoLiveSeats $multi "multi-seat"
foreach ($f in $multiFails) { $failures += $f }

$opsFails = Test-GoLiveSeats $opsEvidence "operational-evidence"
foreach ($f in $opsFails) { $failures += $f }

$prodFiles = @(
    "src/Gateway/HRManagement.Gateway/appsettings.Production.json",
    "src/Gateway/HRManagement.Gateway/appsettings.Staging.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Production.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Staging.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Production.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Staging.json"
)

foreach ($rel in $prodFiles) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { $failures += "missing $rel"; continue }
    $cfg = Get-Content -Raw -Path $full | ConvertFrom-Json
    $pg = $cfg.ProgramGovernance
    if ($null -eq $pg) { $failures += "$rel missing ProgramGovernance"; continue }
    if ($pg.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($pg.GoLiveDeclared -eq $true) {
        $evFails = Test-GoLiveSeats $opsEvidence "$rel GoLiveDeclared"
        foreach ($f in $evFails) { $failures += $f }
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-005"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    asOfDate = $now.ToString("yyyy-MM-dd")
    decisionCount = @($decisions.decisions).Count
    riskCount = @($risks.risks).Count
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

Write-Host "PASS: STR-005 governance runtime validate"
exit 0
