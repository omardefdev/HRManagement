# SOV-006: runtime/production pen-red binding validate.
param(
    [string]$MatrixPath = "docs/sov-pentest/pen-red-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov006/out"
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

if ($m.card -ne "SOV-006") { $failures += "card must be SOV-006" }

foreach ($need in @(
    @{ Id = "SOV-001"; Status = "Satisfied" },
    @{ Id = "SOV-004"; Status = "Satisfied" },
    @{ Id = "OBS-003"; Status = "Satisfied" },
    @{ Id = "REL-001"; Status = "Satisfied" },
    @{ Id = "SOV-005"; Status = "Satisfied" },
    @{ Id = "live-independent-pen-assessment"; Status = "Blocked" },
    @{ Id = "live-red-team-exercise"; Status = "Blocked" },
    @{ Id = "live-purple-detection-validation"; Status = "Blocked" },
    @{ Id = "live-remediation-retest-closure"; Status = "Blocked" },
    @{ Id = "NAT-001"; Status = "NotStarted" },
    @{ Id = "NAT-002"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.independence.assessorIndependentRequired -ne $true) { $failures += "assessorIndependentRequired must be true" }
if ($m.findingClosure.criticalHighZeroOpenRequired -ne $true) { $failures += "criticalHighZeroOpenRequired must be true" }
if ($m.findingClosure.retestRequired -ne $true) { $failures += "retestRequired must be true" }
if ($m.purpleDetection.socDetectionRequired -ne $true) { $failures += "socDetectionRequired must be true" }
if ($m.evidence.signedReportsRequired -ne $true) { $failures += "signedReportsRequired must be true" }
if ($m.mockSandbox.liveIndependentPenAssessmentForbidden -ne $true) { $failures += "liveIndependentPenAssessmentForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signing owner equals reviewer" }
    $version = $sig.penRedPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.penRedPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$oc = Get-Json "testdata/sov006/scenarios/open-critical-fail.json"
if ($oc.expectedValidate -ne "FAIL" -or $oc.openCritical -ne $true) { $failures += "open-critical-fail invalid" }
$ai = Get-Json "testdata/sov006/scenarios/assessor-not-independent-fail.json"
if ($ai.expectedValidate -ne "FAIL" -or $ai.assessorIndependent -eq $true) { $failures += "assessor-not-independent-fail invalid" }
$fr = Get-Json "testdata/sov006/scenarios/finding-without-retest-fail.json"
if ($fr.expectedValidate -ne "FAIL" -or $fr.retestPresent -eq $true) { $failures += "finding-without-retest-fail invalid" }
$pu = Get-Json "testdata/sov006/scenarios/purple-undetected-fail.json"
if ($pu.expectedValidate -ne "FAIL" -or $pu.socDetected -eq $true) { $failures += "purple-undetected-fail invalid" }
$lp = Get-Json "testdata/sov006/scenarios/live-pen-claim-fail.json"
if ($lp.expectedValidate -ne "FAIL" -or $lp.livePenClaim -ne $true) { $failures += "live-pen-claim-fail invalid" }

$ledger = Get-Json "testdata/sov006/harness/pen-red-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "pen-red-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.livePen -eq $true) { $failures += "ledger run $($r.runId) livePen must be false" }
}

# SOV-005 sync: SOV-006 Satisfied (pen-red binding; live pen later)
$sov5 = Get-Json "docs/sov-threat/threat-model-matrix.json"
$sov6b = @($sov5.blockers | Where-Object { $_.id -eq "SOV-006" } | Select-Object -First 1)
if ($sov6b.Count -eq 0) { $failures += "SOV-005 matrix missing SOV-006 blocker" }
elseif ([string]$sov6b[0].status -ne "Satisfied") {
    $failures += "SOV-005 matrix SOV-006 must be Satisfied (got $($sov6b[0].status))"
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
    $fullPath = Resolve-RepoPath $rel
    if (-not (Test-Path $fullPath)) { $failures += "missing $rel"; continue }
    $cfg = Get-Content -Raw -Path $fullPath | ConvertFrom-Json
    $sp = $cfg.SovPentest
    if ($null -eq $sp) { $failures += "$rel missing SovPentest"; continue }
    if ($sp.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($sp.LivePenClaim -eq $true) { $failures += "$rel LivePenClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-006"
    mode = "runtime-validate"
    ok = ($failures.Count -eq 0)
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

Write-Host "PASS: SOV-006 pen-red runtime validate"
exit 0
