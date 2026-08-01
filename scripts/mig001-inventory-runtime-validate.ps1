# MIG-001: runtime/production inventory binding validate.
# Usage:
#   powershell -File scripts/mig001-inventory-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/mig-inventory/source-inventory.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/mig001/out"
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

if ($m.card -ne "MIG-001") { $failures += "card must be MIG-001" }

foreach ($need in @(
    @{ Id = "STR-001"; Status = "Satisfied" },
    @{ Id = "GOV-003"; Status = "Satisfied" },
    @{ Id = "agency-extract-access"; Status = "Blocked" },
    @{ Id = "SYR-002"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

$sourceMap = @{}
$srcCount = 0
foreach ($s in @($m.sources)) {
    $srcCount++
    $sourceMap[$s.sourceId] = $s
    if ($s.status -eq "Inventoried") {
        if (@($s.accessRisks).Count -lt 1) {
            $failures += "source $($s.sourceId) Inventoried without accessRisks"
        }
        if ($s.volume.estimateOnly -eq $true) {
            $failures += "source $($s.sourceId) Inventoried with estimateOnly"
        }
    }
}
if ($srcCount -lt 6) { $failures += "need >= 6 sources" }

$exBySource = @{}
foreach ($ex in @($m.exceptions)) {
    $exBySource[$ex.sourceId] = $ex
    if ([string]::IsNullOrWhiteSpace($ex.signedByRole) -or [string]::IsNullOrWhiteSpace($ex.expiresOn)) {
        $failures += "exception $($ex.exceptionId) missing signedByRole/expiresOn"
    }
}

foreach ($uid in @($m.knownSourceUniverse)) {
    if (-not $sourceMap.ContainsKey($uid)) {
        $failures += "universe source $uid missing from sources"
        continue
    }
    $s = $sourceMap[$uid]
    if ($s.status -eq "Unsupported" -or $s.status -eq "ExceptionSigned") {
        if (-not $exBySource.ContainsKey($uid)) {
            $failures += "universe source $uid status $($s.status) without signed exception"
        }
    }
}

$sotFields = @{}
foreach ($t in @($m.targetFieldSot)) {
    if ([string]::IsNullOrWhiteSpace($t.targetField) -or
        [string]::IsNullOrWhiteSpace($t.sourceOfTruthSourceId) -or
        [string]::IsNullOrWhiteSpace($t.sourceField) -or
        [string]::IsNullOrWhiteSpace($t.gov003Join)) {
        $failures += "targetFieldSot $($t.targetField) missing required fields"
    }
    if (-not $sourceMap.ContainsKey($t.sourceOfTruthSourceId)) {
        $failures += "SoT source $($t.sourceOfTruthSourceId) not in inventory"
    }
    $sotFields[$t.targetField] = $true
}
foreach ($need in @($m.requiredTargetFields)) {
    if (-not $sotFields.ContainsKey($need)) {
        $failures += "required target field $need missing SoT"
    }
}

# Negative fixtures must fail semantics
$estFail = Get-Json "testdata/mig001/scenarios/estimate-only-volume.json"
$sotFail = Get-Json "testdata/mig001/scenarios/target-field-no-sot.json"
$accessFail = Get-Json "testdata/mig001/scenarios/undocumented-access-risk.json"

if ($estFail.expectedValidate -ne "FAIL") { $failures += "estimate-only fixture expectedValidate must be FAIL" }
if ($estFail.volume.estimateOnly -ne $true -or $estFail.status -ne "Inventoried") {
    $failures += "estimate-only fixture must be Inventoried + estimateOnly"
}

if ($sotFail.expectedValidate -ne "FAIL") { $failures += "missing-sot fixture expectedValidate must be FAIL" }
if ($null -ne $sotFail.sourceOfTruthSourceId -and -not [string]::IsNullOrWhiteSpace([string]$sotFail.sourceOfTruthSourceId)) {
    $failures += "missing-sot fixture must have null sourceOfTruthSourceId"
}

if ($accessFail.expectedValidate -ne "FAIL") { $failures += "access-risk fixture expectedValidate must be FAIL" }
if (@($accessFail.accessRisks).Count -gt 0) {
    $failures += "access-risk-fail fixture must have empty accessRisks"
}

# Positive fixtures
$sampling = Get-Json "testdata/mig001/sampling/source-sample-manifest.json"
$counts = Get-Json "testdata/mig001/counts/row-file-counts.json"
$profile = Get-Json "testdata/mig001/profiling/agency-signed-profile.json"
$covEx = Get-Json "testdata/mig001/exceptions/signed-unsupported.json"

if ($sampling.expectedValidate -ne "PASS") { $failures += "sampling expectedValidate must be PASS" }
if (@($sampling.samples).Count -lt 3) { $failures += "sampling manifest needs >= 3 samples" }

if ($counts.expectedValidate -ne "PASS") { $failures += "counts expectedValidate must be PASS" }
if (@($counts.counts).Count -lt 3) { $failures += "counts fixture needs >= 3 rows" }

if ($profile.expectedValidate -ne "PASS") { $failures += "profiling expectedValidate must be PASS" }
if ($profile.agencySigned -ne $true) { $failures += "profiling report must be agencySigned" }

if ($covEx.expectedValidate -ne "PASS") { $failures += "signed-unsupported expectedValidate must be PASS" }
if (-not $covEx.coversUniverseGap) { $failures += "signed-unsupported fixture must cover universe gap" }

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
    $mi = $cfg.MigInventory
    if ($null -eq $mi) { $failures += "$rel missing MigInventory"; continue }
    if ($mi.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($mi.LiveAgencyExtractClaim -eq $true) { $failures += "$rel LiveAgencyExtractClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-001"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    sourceCount = $srcCount
    sotCount = @($m.targetFieldSot).Count
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

Write-Host "PASS: MIG-001 inventory runtime validate"
exit 0
