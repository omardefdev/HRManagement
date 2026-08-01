# SOV-004: runtime/production zero-trust binding validate.
param(
    [string]$MatrixPath = "docs/sov-zt/zero-trust-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov004/out"
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

if ($m.card -ne "SOV-004") { $failures += "card must be SOV-004" }

foreach ($need in @(
    @{ Id = "IAM-003"; Status = "Satisfied" },
    @{ Id = "IAM-004"; Status = "Satisfied" },
    @{ Id = "SOV-003"; Status = "Satisfied" },
    @{ Id = "live-pam-workers"; Status = "Blocked" },
    @{ Id = "live-admin-workstation-enforcement"; Status = "Blocked" },
    @{ Id = "live-break-glass-soc"; Status = "Blocked" },
    @{ Id = "live-access-recertification"; Status = "Blocked" },
    @{ Id = "SOV-005"; Status = "Satisfied" },
    @{ Id = "NAT-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.noStandingPrivilege.sharedRootForbidden -ne $true) { $failures += "sharedRootForbidden must be true" }
if ($m.pamJit.approvalRequired -ne $true) { $failures += "approvalRequired must be true" }
if ($m.adminWorkstations.ordinaryDeviceForbidden -ne $true) { $failures += "ordinaryDeviceForbidden must be true" }
if ($m.breakGlass.immediateAlertRequired -ne $true) { $failures += "immediateAlertRequired must be true" }
if ($m.accessRecertification.dormantOrphanZeroRequired -ne $true) { $failures += "dormantOrphanZeroRequired must be true" }
if ($m.mockSandbox.livePamForbidden -ne $true) { $failures += "livePamForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signing owner equals reviewer" }
    $version = $sig.zeroTrustPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.zeroTrustPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$sp = Get-Json "testdata/sov004/scenarios/standing-privilege-fail.json"
if ($sp.expectedValidate -ne "FAIL" -or $sp.hasStanding -ne $true) { $failures += "standing-privilege-fail invalid" }
$ja = Get-Json "testdata/sov004/scenarios/jit-without-approval-fail.json"
if ($ja.expectedValidate -ne "FAIL" -or $ja.approved -eq $true) { $failures += "jit-without-approval-fail invalid" }
$od = Get-Json "testdata/sov004/scenarios/admin-from-ordinary-device-fail.json"
if ($od.expectedValidate -ne "FAIL" -or $od.fromOrdinaryDevice -ne $true) { $failures += "admin-from-ordinary-device-fail invalid" }
$bg = Get-Json "testdata/sov004/scenarios/break-glass-no-alert-fail.json"
if ($bg.expectedValidate -ne "FAIL" -or $bg.alerted -eq $true) { $failures += "break-glass-no-alert-fail invalid" }
$lp = Get-Json "testdata/sov004/scenarios/live-pam-claim-fail.json"
if ($lp.expectedValidate -ne "FAIL" -or $lp.livePamClaim -ne $true) { $failures += "live-pam-claim-fail invalid" }

$ledger = Get-Json "testdata/sov004/harness/zero-trust-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "zero-trust-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.livePam -eq $true) { $failures += "ledger run $($r.runId) livePam must be false" }
}

# SOV-003 sync: SOV-004 Satisfied (zero-trust binding; live PAM later)
$sov3 = Get-Json "docs/sov-keys/key-sovereignty-matrix.json"
$sov4b = @($sov3.blockers | Where-Object { $_.id -eq "SOV-004" } | Select-Object -First 1)
if ($sov4b.Count -eq 0) { $failures += "SOV-003 matrix missing SOV-004 blocker" }
elseif ([string]$sov4b[0].status -ne "Satisfied") {
    $failures += "SOV-003 matrix SOV-004 must be Satisfied (got $($sov4b[0].status))"
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
    $sz = $cfg.SovZt
    if ($null -eq $sz) { $failures += "$rel missing SovZt"; continue }
    if ($sz.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($sz.LivePamClaim -eq $true) { $failures += "$rel LivePamClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-004"
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

Write-Host "PASS: SOV-004 zero-trust runtime validate"
exit 0
