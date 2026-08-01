# STR-006: runtime/production ownership binding validate.
# Usage:
#   powershell -File scripts/str006-ownership-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/str-ownership/ownership-matrix.json",
    [string]$BusFactorPass = "testdata/str006/scenarios/bus-factor-pass.json",
    [string]$BusFactorFail = "testdata/str006/scenarios/bus-factor-fail.json",
    [string]$TcoFunded = "testdata/str006/scenarios/tco-ops-security-backup.json",
    [string]$TcoDevOnly = "testdata/str006/scenarios/tco-dev-only.json",
    [string]$StaffingOnCall = "testdata/str006/scenarios/staffing-oncall.json",
    [string]$VendorMissingExit = "testdata/str006/vendors/missing-exit-clause.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str006/out"
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

function Test-BusFactorComponent($c, $minBf) {
    $local = @()
    if ($c.critical -eq $true) {
        if ([int]$c.busFactor -lt $minBf) {
            $local += "busFactor $($c.busFactor) < $minBf"
        }
        $backups = @($c.backupRoles)
        if ($backups.Count -lt 1) {
            $local += "critical without backupRoles"
        }
    }
    return $local
}

function Test-BudgetLines($lines, $mustFund, $forbidDevOnly) {
    $local = @()
    $funded = @()
    foreach ($line in @($lines)) {
        if ($line.funded -eq $true) { $funded += [string]$line.category }
    }
    $fundedSet = @($funded | Select-Object -Unique)
    if ($forbidDevOnly -and $fundedSet.Count -eq 1 -and $fundedSet[0] -eq "dev") {
        $local += "budget forbidDevOnly only dev funded"
    }
    foreach ($cat in @($mustFund)) {
        if ($fundedSet -notcontains $cat) {
            $local += "budget missing funded category $cat"
        }
    }
    return $local
}

function Test-Vendor($v, $requiredClauses) {
    $local = @()
    foreach ($clause in @($requiredClauses)) {
        $hit = @($v.clauses | Where-Object { [string]::Equals($_, $clause, [StringComparison]::OrdinalIgnoreCase) })
        if ($hit.Count -eq 0) { $local += "vendor $($v.vendorId) missing clause $clause" }
    }
    if (($requiredClauses -contains "Exit") -and ($v.exitAssistance -ne $true)) {
        $local += "vendor $($v.vendorId) Exit requires exitAssistance"
    }
    return $local
}

$failures = @()
$m = Get-Json $MatrixPath
$bfPass = Get-Json $BusFactorPass
$bfFail = Get-Json $BusFactorFail
$tcoOk = Get-Json $TcoFunded
$tcoBad = Get-Json $TcoDevOnly
$staff = Get-Json $StaffingOnCall
$vendorBad = Get-Json $VendorMissingExit

if ($m.card -ne "STR-006") { $failures += "card must be STR-006" }

foreach ($need in @(
    @{ Id = "STR-005"; Status = "Satisfied" },
    @{ Id = "SOV-007"; Status = "Satisfied" },
    @{ Id = "FIN-001"; Status = "NotStarted" },
    @{ Id = "SRE-001"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.staffing.national24x7Claim -eq $true) {
    $failures += "matrix staffing national24x7Claim must be false"
}

$minBf = [int]$m.busFactorRule.criticalMinBusFactor
if ($minBf -le 0) { $minBf = 2 }
$mustFund = @($m.budgetRule.mustFundCategories)
if ($mustFund.Count -eq 0) { $mustFund = @("ops", "security", "backup") }
$requiredClauses = @($m.requiredVendorClauses)
if ($requiredClauses.Count -eq 0) { $requiredClauses = @("RTO", "IncidentDisclosure", "Exit") }

foreach ($c in @($m.components)) {
    foreach ($f in (Test-BusFactorComponent $c $minBf)) {
        $failures += "matrix component $($c.id) $f"
    }
}

foreach ($f in (Test-BudgetLines $m.budgetLines $mustFund $m.budgetRule.forbidDevOnly)) {
    $failures += "matrix $f"
}

foreach ($v in @($m.vendors)) {
    foreach ($f in (Test-Vendor $v $requiredClauses)) { $failures += "matrix $f" }
}

# Negative fixtures must fail evaluation
$bfFailErrs = Test-BusFactorComponent $bfFail $minBf
if ($bfFailErrs.Count -eq 0) { $failures += "bus-factor-fail fixture must FAIL" }

$tcoBadErrs = Test-BudgetLines $tcoBad.lines $tcoBad.mustFundCategories $true
if ($tcoBadErrs.Count -eq 0) { $failures += "tco-dev-only fixture must FAIL" }

$vendorBadErrs = Test-Vendor $vendorBad $requiredClauses
if ($vendorBadErrs.Count -eq 0) { $failures += "missing-exit-clause fixture must FAIL" }

# Positive fixtures must pass
$bfPassErrs = Test-BusFactorComponent $bfPass $minBf
foreach ($f in $bfPassErrs) { $failures += "bus-factor-pass $f" }

$tcoOkErrs = Test-BudgetLines $tcoOk.lines $tcoOk.mustFundCategories $true
foreach ($f in $tcoOkErrs) { $failures += "tco-funded $f" }

if ($staff.national24x7Claim -eq $true) {
    $failures += "staffing-oncall national24x7Claim must be false"
}
foreach ($tier in @($staff.tiers)) {
    $b = @($tier.backupRoles)
    if (-not $tier.primaryRole -or $b.Count -lt 1) {
        $failures += "staffing-oncall tier $($tier.tier) missing primary/backup"
    }
}

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
    $po = $cfg.ProgramOwnership
    if ($null -eq $po) { $failures += "$rel missing ProgramOwnership"; continue }
    if ($po.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($po.National24x7Claim -eq $true) { $failures += "$rel National24x7Claim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-006"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    criticalComponentCount = @($m.components | Where-Object { $_.critical -eq $true }).Count
    vendorCount = @($m.vendors).Count
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

Write-Host "PASS: STR-006 ownership runtime validate"
exit 0
