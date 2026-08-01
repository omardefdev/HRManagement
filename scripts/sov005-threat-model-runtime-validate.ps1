# SOV-005: runtime/production threat-modeling binding validate.
param(
    [string]$MatrixPath = "docs/sov-threat/threat-model-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov005/out"
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

if ($m.card -ne "SOV-005") { $failures += "card must be SOV-005" }

foreach ($need in @(
    @{ Id = "STR-001"; Status = "Satisfied" },
    @{ Id = "TEN-001"; Status = "Satisfied" },
    @{ Id = "INT-003"; Status = "Satisfied" },
    @{ Id = "SOV-004"; Status = "Satisfied" },
    @{ Id = "live-independent-tm-review"; Status = "Blocked" },
    @{ Id = "live-attack-tree-walkthrough"; Status = "Blocked" },
    @{ Id = "live-misuse-backlog-sync"; Status = "Blocked" },
    @{ Id = "live-residual-risk-signoff"; Status = "Blocked" },
    @{ Id = "SOV-006"; Status = "Satisfied" },
    @{ Id = "NAT-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.methodology.strideRequired -ne $true) { $failures += "strideRequired must be true" }
if ($m.highThreatCoverage.controlRequired -ne $true) { $failures += "controlRequired must be true" }
if ($m.highThreatCoverage.testRequired -ne $true) { $failures += "testRequired must be true" }
if ($m.highThreatCoverage.ownerRequired -ne $true) { $failures += "ownerRequired must be true" }
if ($m.changeTrigger.materialChangeUpdateRequired -ne $true) { $failures += "materialChangeUpdateRequired must be true" }
if ($m.residualRisk.signedRequired -ne $true) { $failures += "signedRequired must be true" }
if ($m.mockSandbox.liveIndependentTmReviewForbidden -ne $true) { $failures += "liveIndependentTmReviewForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signing owner equals reviewer" }
    $version = $sig.threatModelPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.threatModelPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$ms = Get-Json "testdata/sov005/scenarios/missing-stride-fail.json"
if ($ms.expectedValidate -ne "FAIL" -or $ms.stridePresent -eq $true) { $failures += "missing-stride-fail invalid" }
$hc = Get-Json "testdata/sov005/scenarios/high-without-control-fail.json"
if ($hc.expectedValidate -ne "FAIL" -or $hc.controlPresent -eq $true) { $failures += "high-without-control-fail invalid" }
$ru = Get-Json "testdata/sov005/scenarios/residual-unsigned-fail.json"
if ($ru.expectedValidate -ne "FAIL" -or $ru.signed -eq $true) { $failures += "residual-unsigned-fail invalid" }
$lt = Get-Json "testdata/sov005/scenarios/live-threat-model-claim-fail.json"
if ($lt.expectedValidate -ne "FAIL" -or $lt.liveThreatModelClaim -ne $true) { $failures += "live-threat-model-claim-fail invalid" }

$ledger = Get-Json "testdata/sov005/harness/threat-model-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "threat-model-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveThreatModel -eq $true) { $failures += "ledger run $($r.runId) liveThreatModel must be false" }
}

# SOV-004 sync: SOV-005 Satisfied (threat-modeling binding; live TM review later)
$sov4 = Get-Json "docs/sov-zt/zero-trust-matrix.json"
$sov5b = @($sov4.blockers | Where-Object { $_.id -eq "SOV-005" } | Select-Object -First 1)
if ($sov5b.Count -eq 0) { $failures += "SOV-004 matrix missing SOV-005 blocker" }
elseif ([string]$sov5b[0].status -ne "Satisfied") {
    $failures += "SOV-004 matrix SOV-005 must be Satisfied (got $($sov5b[0].status))"
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
    $st = $cfg.SovThreat
    if ($null -eq $st) { $failures += "$rel missing SovThreat"; continue }
    if ($st.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($st.LiveThreatModelClaim -eq $true) { $failures += "$rel LiveThreatModelClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-005"
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

Write-Host "PASS: SOV-005 threat-model runtime validate"
exit 0
