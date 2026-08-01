# STR-006: validate ownership matrix, bus-factor, budget, vendors, simulations.
# Usage:
#   powershell -File scripts/str006-ownership-validate.ps1

param(
    [string]$MatrixPath = "docs/str-ownership/ownership-matrix.json",
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

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "STR-006") { $failures += "card must be STR-006" }

# Journeys
$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("OwnershipQuad", "BusFactor", "StaffingOnCall", "BudgetTco", "VendorSla")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

# Components
$compFields = @($m.requiredComponentFields)
$minBf = [int]$m.busFactorRule.criticalMinBusFactor
$compCount = 0
foreach ($c in $m.components) {
    $compCount++
    foreach ($f in $compFields) {
        if ($null -eq $c.$f) {
            $failures += "component $($c.id) missing $f"
        }
        elseif ($f -eq "backupRoles") {
            # array ok even if empty for non-critical; checked below
        }
        elseif ($c.$f -is [string] -and [string]::IsNullOrWhiteSpace($c.$f)) {
            $failures += "component $($c.id) empty $f"
        }
    }
    $backups = @($c.backupRoles)
    if ($c.critical) {
        if ([int]$c.busFactor -lt $minBf) {
            $failures += "component $($c.id) busFactor $($c.busFactor) < $minBf"
        }
        if ($backups.Count -lt 1) {
            $failures += "component $($c.id) critical without backupRoles"
        }
    }
}
if ($compCount -lt 6) { $failures += "need >= 6 components" }

# Skills
foreach ($sk in $m.skills) {
    foreach ($f in @($m.requiredSkillFields)) {
        if ($null -eq $sk.$f) { $failures += "skill $($sk.skillId) missing $f" }
    }
    $q = @($sk.qualifiedRoles)
    if ($sk.tier -eq "0" -or $sk.tier -eq 0) {
        if ($q.Count -lt [int]$sk.minQualified) {
            $failures += "skill $($sk.skillId) qualifiedRoles < minQualified"
        }
        if ([int]$sk.minQualified -lt 2) {
            $failures += "skill $($sk.skillId) tier0 minQualified must be >= 2"
        }
    }
}

# Staffing
if ($m.staffing.national24x7Claim -eq $true) {
    $failures += "national24x7Claim must be false until SRE-001"
}
foreach ($r in $m.staffing.rosters) {
    $b = @($r.backupRoles)
    if ([string]::IsNullOrWhiteSpace($r.primaryRole) -or $b.Count -lt 1) {
        $failures += "roster tier $($r.tier) missing primary/backup"
    }
}

# Budget
$fundedCats = @{}
foreach ($line in $m.budgetLines) {
    foreach ($f in @($m.requiredBudgetFields)) {
        if ($null -eq $line.$f) { $failures += "budget $($line.category) missing $f" }
    }
    if ($m.allowedBudgetCategories -notcontains $line.category) {
        $failures += "budget bad category $($line.category)"
    }
    $yrs = @($line.years)
    if ($yrs.Count -lt [int]$m.budgetRule.horizonYears) {
        $failures += "budget $($line.category) years < horizon"
    }
    if ($line.funded) { $fundedCats[$line.category] = $true }
}
foreach ($need in @($m.budgetRule.mustFundCategories)) {
    if (-not $fundedCats.ContainsKey($need)) {
        $failures += "must fund category $need"
    }
}
if ($m.budgetRule.forbidDevOnly) {
    $onlyDev = ($fundedCats.Keys.Count -eq 1 -and $fundedCats.ContainsKey("dev"))
    if ($onlyDev) { $failures += "dev-only budget forbidden" }
}

# Vendors
$reqClauses = @($m.requiredVendorClauses)
$maxRtoHours = [math]::Ceiling([double]$m.envelopeRtoMinutes / 60.0)
foreach ($v in $m.vendors) {
    foreach ($f in @($m.requiredVendorClauseFields)) {
        if ($null -eq $v.$f) { $failures += "vendor $($v.vendorId) missing $f" }
    }
    $clauses = @($v.clauses)
    foreach ($need in $reqClauses) {
        if ($clauses -notcontains $need) {
            $failures += "vendor $($v.vendorId) missing clause $need"
        }
    }
    if (-not $v.exitAssistance) {
        $failures += "vendor $($v.vendorId) exitAssistance must be true"
    }
    if ([double]$v.rtoHours -gt $maxRtoHours) {
        $failures += "vendor $($v.vendorId) rtoHours $($v.rtoHours) > envelope $maxRtoHours h"
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
if ($blockerMap["STR-005"] -ne "Satisfied") { $failures += "STR-005 must be Satisfied" }
if ($blockerMap["SOV-007"] -ne "Satisfied") { $failures += "SOV-007 must be Satisfied" }
if ($blockerMap["FIN-001"] -ne "NotStarted") { $failures += "FIN-001 must be NotStarted" }
if ($blockerMap["SRE-001"] -ne "Blocked") { $failures += "SRE-001 must be Blocked" }

# Simulations — existence and expected outcomes
function Assert-FixtureExpected([string]$rel, [string]$expect) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $script:failures += "missing fixture $rel"
        return $null
    }
    $obj = Get-Content -Raw -Path $full | ConvertFrom-Json
    if ($obj.expectedValidate -ne $expect) {
        $script:failures += "fixture $rel expectedValidate must be $expect"
    }
    return $obj
}

$bfPass = Assert-FixtureExpected $m.simulationPaths.busFactorPass "PASS"
$bfFail = Assert-FixtureExpected $m.simulationPaths.busFactorFail "FAIL"
$staff = Assert-FixtureExpected $m.simulationPaths.staffingOnCall "PASS"
$tcoBad = Assert-FixtureExpected $m.simulationPaths.tcoDevOnly "FAIL"
$tcoGood = Assert-FixtureExpected $m.simulationPaths.tcoFunded "PASS"
$vendBad = Assert-FixtureExpected $m.simulationPaths.vendorMissingExit "FAIL"

if ($null -ne $bfFail) {
    if ([int]$bfFail.busFactor -ge $minBf -or @($bfFail.backupRoles).Count -ge 1) {
        $failures += "bus-factor-fail fixture must violate bus-factor rule"
    }
}
if ($null -ne $bfPass) {
    if ([int]$bfPass.busFactor -lt $minBf -or @($bfPass.backupRoles).Count -lt 1) {
        $failures += "bus-factor-pass fixture must satisfy bus-factor rule"
    }
}
if ($null -ne $staff) {
    if ($staff.national24x7Claim -eq $true) {
        $failures += "staffing fixture must not claim national 24x7"
    }
    if (@($staff.coverageGaps).Count -gt 0) {
        $failures += "staffing PASS fixture must have empty coverageGaps"
    }
}
if ($null -ne $tcoBad) {
    $cats = @($tcoBad.lines | ForEach-Object { $_.category })
    $hasMust = $false
    foreach ($need in @($tcoBad.mustFundCategories)) {
        if ($cats -contains $need) { $hasMust = $true }
    }
    if ($hasMust -and $cats.Count -gt 1) {
        # still fail if all mustFund are present — then fixture is wrong
        $missing = @()
        foreach ($need in @($tcoBad.mustFundCategories)) {
            if ($cats -notcontains $need) { $missing += $need }
        }
        if ($missing.Count -eq 0) {
            $failures += "tco-dev-only fixture incorrectly funds mustFundCategories"
        }
    }
}
if ($null -ne $tcoGood) {
    foreach ($need in @($tcoGood.mustFundCategories)) {
        $found = $false
        foreach ($line in $tcoGood.lines) {
            if ($line.category -eq $need -and $line.funded) { $found = $true }
        }
        if (-not $found) { $failures += "tco-funded fixture missing $need" }
    }
}
if ($null -ne $vendBad) {
    $clauses = @($vendBad.clauses)
    if ($clauses -contains "Exit" -and $vendBad.exitAssistance) {
        $failures += "missing-exit fixture must lack Exit"
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-006"
    ok = ($failures.Count -eq 0)
    componentCount = $compCount
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

Write-Host "PASS: STR-006 ownership validate"
exit 0
