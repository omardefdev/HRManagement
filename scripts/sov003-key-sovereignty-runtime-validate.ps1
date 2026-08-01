# SOV-003: runtime/production key-sovereignty binding validate.
param(
    [string]$MatrixPath = "docs/sov-keys/key-sovereignty-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov003/out"
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

if ($m.card -ne "SOV-003") { $failures += "card must be SOV-003" }

foreach ($need in @(
    @{ Id = "OPS-002"; Status = "Satisfied" },
    @{ Id = "STR-002"; Status = "Satisfied" },
    @{ Id = "SOV-002"; Status = "Satisfied" },
    @{ Id = "SEC-004"; Status = "Blocked" },
    @{ Id = "live-hsm-prod"; Status = "Blocked" },
    @{ Id = "live-key-ceremony-workers"; Status = "Blocked" },
    @{ Id = "live-trusted-time"; Status = "Blocked" },
    @{ Id = "EXT-004"; Status = "Satisfied" },
    @{ Id = "SOV-004"; Status = "Satisfied" },
    @{ Id = "SIG-001"; Status = "NotStarted" },
    @{ Id = "NAT-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.hsmKms.vendorAloneCannotDecrypt -ne $true) { $failures += "vendorAloneCannotDecrypt must be true" }
if ($m.dualControl.required -ne $true) { $failures += "dualControl.required must be true" }
if ($m.rotation.custodianLossDrillRequired -ne $true) { $failures += "custodianLossDrillRequired must be true" }
if ($m.trustedTime.clockDriftBoundRequired -ne $true) { $failures += "clockDriftBoundRequired must be true" }
if ($m.mockSandbox.liveHsmForbidden -ne $true) { $failures += "liveHsmForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signing owner equals reviewer" }
    $version = $sig.keySovereigntyPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.keySovereigntyPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$va = Get-Json "testdata/sov003/scenarios/vendor-alone-decrypt-fail.json"
if ($va.expectedValidate -ne "FAIL" -or $va.vendorAlone -ne $true) { $failures += "vendor-alone-decrypt-fail invalid" }
$nd = Get-Json "testdata/sov003/scenarios/no-dual-control-fail.json"
if ($nd.expectedValidate -ne "FAIL" -or $nd.dualControlPresent -eq $true) { $failures += "no-dual-control-fail invalid" }
$mr = Get-Json "testdata/sov003/scenarios/missing-rotation-fail.json"
if ($mr.expectedValidate -ne "FAIL" -or $mr.rotationDrillPresent -eq $true) { $failures += "missing-rotation-fail invalid" }
$cd = Get-Json "testdata/sov003/scenarios/clock-drift-unbounded-fail.json"
if ($cd.expectedValidate -ne "FAIL" -or $cd.clockDriftBoundPresent -eq $true) { $failures += "clock-drift-unbounded-fail invalid" }
$lk = Get-Json "testdata/sov003/scenarios/live-key-custody-claim-fail.json"
if ($lk.expectedValidate -ne "FAIL" -or $lk.liveKeyCustodyClaim -ne $true) { $failures += "live-key-custody-claim-fail invalid" }

$ledger = Get-Json "testdata/sov003/harness/key-sovereignty-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "key-sovereignty-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveKeyCustody -eq $true) { $failures += "ledger run $($r.runId) liveKeyCustody must be false" }
}

# SOV-002 sync: SOV-003 Satisfied
$sov2 = Get-Json "docs/sov-data/data-sovereignty-matrix.json"
$sov3b = @($sov2.blockers | Where-Object { $_.id -eq "SOV-003" } | Select-Object -First 1)
if ($sov3b.Count -eq 0) { $failures += "SOV-002 matrix missing SOV-003 blocker" }
elseif ([string]$sov3b[0].status -ne "Satisfied") {
    $failures += "SOV-002 matrix SOV-003 must be Satisfied (got $($sov3b[0].status))"
}

# EXT-004 sync: SOV-003 Satisfied; hsm-prod remains Blocked
$ext4 = Get-Json "docs/ext-pki/pki-signature-matrix.json"
$eSov3 = @($ext4.blockers | Where-Object { $_.id -eq "SOV-003" } | Select-Object -First 1)
if ($eSov3.Count -eq 0) { $failures += "EXT-004 matrix missing SOV-003 blocker" }
elseif ([string]$eSov3[0].status -ne "Satisfied") {
    $failures += "EXT-004 matrix SOV-003 must be Satisfied (got $($eSov3[0].status))"
}
$hsm = @($ext4.blockers | Where-Object { $_.id -eq "hsm-prod" } | Select-Object -First 1)
if ($hsm.Count -eq 0) { $failures += "EXT-004 matrix missing hsm-prod blocker" }
elseif ([string]$hsm[0].status -ne "Blocked") {
    $failures += "EXT-004 hsm-prod must remain Blocked (got $($hsm[0].status))"
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
    $sk = $cfg.SovKeys
    if ($null -eq $sk) { $failures += "$rel missing SovKeys"; continue }
    if ($sk.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($sk.LiveKeyCustodyClaim -eq $true) { $failures += "$rel LiveKeyCustodyClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-003"
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

Write-Host "PASS: SOV-003 key-sovereignty runtime validate"
exit 0
