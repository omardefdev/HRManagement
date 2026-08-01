# SOV-003: validate key-sovereignty contract matrix and harness fixtures.
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

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("HierarchyCustody", "ControlRotation", "AgilityTime", "ClaimGate")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.keyHierarchy.required -ne $true) { $failures += "keyHierarchy.required must be true" }
if ($m.keyHierarchy.inventoryRequired -ne $true) { $failures += "inventoryRequired must be true" }
if ($m.hsmKms.required -ne $true) { $failures += "hsmKms.required must be true" }
if ($m.hsmKms.approvedCustodyRequired -ne $true) { $failures += "approvedCustodyRequired must be true" }
if ($m.hsmKms.vendorAloneCannotDecrypt -ne $true) { $failures += "vendorAloneCannotDecrypt must be true" }
if ($m.hsmKms.labMockAllowed -ne $true) { $failures += "labMockAllowed must be true" }
if ($m.byokHyok.required -ne $true) { $failures += "byokHyok.required must be true" }
if ($m.byokHyok.whereRequiredFlag -ne $true) { $failures += "whereRequiredFlag must be true" }
if ($m.separation.required -ne $true) { $failures += "separation.required must be true" }
if ($m.separation.envTenantPurposeSeparation -ne $true) { $failures += "envTenantPurposeSeparation must be true" }
if ($m.dualControl.required -ne $true) { $failures += "dualControl.required must be true" }
if ($m.rotation.required -ne $true) { $failures += "rotation.required must be true" }
if ($m.rotation.revocationRequired -ne $true) { $failures += "revocationRequired must be true" }
if ($m.rotation.backupRequired -ne $true) { $failures += "backupRequired must be true" }
if ($m.rotation.destructionRequired -ne $true) { $failures += "destructionRequired must be true" }
if ($m.rotation.custodianLossDrillRequired -ne $true) { $failures += "custodianLossDrillRequired must be true" }
if ($m.algorithmAgility.required -ne $true) { $failures += "algorithmAgility.required must be true" }
if ($m.algorithmAgility.deprecatedAlgForbiddenInProdClaim -ne $true) { $failures += "deprecatedAlgForbiddenInProdClaim must be true" }
if ($m.tlsCertificates.required -ne $true) { $failures += "tlsCertificates.required must be true" }
if ($m.tlsCertificates.inventoryExpiryAlertsRequired -ne $true) { $failures += "inventoryExpiryAlertsRequired must be true" }
if ($m.trustedTime.required -ne $true) { $failures += "trustedTime.required must be true" }
if ($m.trustedTime.clockDriftBoundRequired -ne $true) { $failures += "clockDriftBoundRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("keyId", "purpose", "custodyType", "actor", "occurredAt", "evidenceRef", "outcome", "ceremonyRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveHsmForbidden -ne $true) { $failures += "liveHsmForbidden must be true" }
if ($m.mockSandbox.liveKeyCeremonyForbidden -ne $true) { $failures += "liveKeyCeremonyForbidden must be true" }
if ($m.mockSandbox.liveTrustedTimeForbidden -ne $true) { $failures += "liveTrustedTimeForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

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

foreach ($rel in @(
    "testdata/sov003/scenarios/vendor-alone-decrypt-fail.json",
    "testdata/sov003/scenarios/missing-hierarchy-fail.json",
    "testdata/sov003/scenarios/no-dual-control-fail.json",
    "testdata/sov003/scenarios/missing-rotation-fail.json",
    "testdata/sov003/scenarios/deprecated-alg-claim-fail.json",
    "testdata/sov003/scenarios/clock-drift-unbounded-fail.json",
    "testdata/sov003/scenarios/soft-key-prod-claim-fail.json",
    "testdata/sov003/scenarios/live-key-custody-claim-fail.json",
    "testdata/sov003/scenarios/hierarchy-inventory-pass.json",
    "testdata/sov003/scenarios/hsm-mock-custody-pass.json",
    "testdata/sov003/scenarios/dual-control-pass.json",
    "testdata/sov003/scenarios/rotation-drill-pass.json",
    "testdata/sov003/scenarios/algorithm-agility-pass.json",
    "testdata/sov003/scenarios/cert-expiry-alert-pass.json",
    "testdata/sov003/scenarios/trusted-time-bound-pass.json",
    "testdata/sov003/audit/key-sovereignty-audit-pass.json",
    "testdata/sov003/harness/key-sovereignty-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$va = Get-Json "testdata/sov003/scenarios/vendor-alone-decrypt-fail.json"
if ($va.expectedValidate -ne "FAIL" -or $va.vendorAlone -ne $true) { $failures += "vendor-alone-decrypt-fail invalid" }
$mh = Get-Json "testdata/sov003/scenarios/missing-hierarchy-fail.json"
if ($mh.expectedValidate -ne "FAIL" -or $mh.inventoryPresent -eq $true) { $failures += "missing-hierarchy-fail invalid" }
$nd = Get-Json "testdata/sov003/scenarios/no-dual-control-fail.json"
if ($nd.expectedValidate -ne "FAIL" -or $nd.dualControlPresent -eq $true) { $failures += "no-dual-control-fail invalid" }
$mr = Get-Json "testdata/sov003/scenarios/missing-rotation-fail.json"
if ($mr.expectedValidate -ne "FAIL" -or $mr.rotationDrillPresent -eq $true) { $failures += "missing-rotation-fail invalid" }
$da = Get-Json "testdata/sov003/scenarios/deprecated-alg-claim-fail.json"
if ($da.expectedValidate -ne "FAIL" -or $da.deprecatedAlgClaimedInProd -ne $true) { $failures += "deprecated-alg-claim-fail invalid" }
$cd = Get-Json "testdata/sov003/scenarios/clock-drift-unbounded-fail.json"
if ($cd.expectedValidate -ne "FAIL" -or $cd.clockDriftBoundPresent -eq $true) { $failures += "clock-drift-unbounded-fail invalid" }
$sk = Get-Json "testdata/sov003/scenarios/soft-key-prod-claim-fail.json"
if ($sk.expectedValidate -ne "FAIL" -or $sk.softKeyClaimedAsProd -ne $true) { $failures += "soft-key-prod-claim-fail invalid" }
$lk = Get-Json "testdata/sov003/scenarios/live-key-custody-claim-fail.json"
if ($lk.expectedValidate -ne "FAIL" -or $lk.liveKeyCustodyClaim -ne $true) { $failures += "live-key-custody-claim-fail invalid" }

$hi = Get-Json "testdata/sov003/scenarios/hierarchy-inventory-pass.json"
if ($hi.expectedValidate -ne "PASS" -or $hi.inventoryPresent -ne $true) { $failures += "hierarchy-inventory-pass invalid" }
$hm = Get-Json "testdata/sov003/scenarios/hsm-mock-custody-pass.json"
if ($hm.expectedValidate -ne "PASS" -or $hm.vendorAlone -eq $true) { $failures += "hsm-mock-custody-pass invalid" }
$dc = Get-Json "testdata/sov003/scenarios/dual-control-pass.json"
if ($dc.expectedValidate -ne "PASS" -or $dc.dualControlPresent -ne $true) { $failures += "dual-control-pass invalid" }
$rd = Get-Json "testdata/sov003/scenarios/rotation-drill-pass.json"
if ($rd.expectedValidate -ne "PASS" -or $rd.rotationDrillPresent -ne $true) { $failures += "rotation-drill-pass invalid" }
$aa = Get-Json "testdata/sov003/scenarios/algorithm-agility-pass.json"
if ($aa.expectedValidate -ne "PASS" -or $aa.deprecatedAlgClaimedInProd -eq $true) { $failures += "algorithm-agility-pass invalid" }
$ce = Get-Json "testdata/sov003/scenarios/cert-expiry-alert-pass.json"
if ($ce.expectedValidate -ne "PASS" -or $ce.expiryAlertsPresent -ne $true) { $failures += "cert-expiry-alert-pass invalid" }
$tt = Get-Json "testdata/sov003/scenarios/trusted-time-bound-pass.json"
if ($tt.expectedValidate -ne "PASS" -or $tt.clockDriftBoundPresent -ne $true) { $failures += "trusted-time-bound-pass invalid" }

$audit = Get-Json "testdata/sov003/audit/key-sovereignty-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "key-sovereignty-audit-pass invalid" }
$ledger = Get-Json "testdata/sov003/harness/key-sovereignty-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "key-sovereignty-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveKeyCustody -eq $true) { $failures += "ledger run $($r.runId) liveKeyCustody must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-003"
    mode = "pack-validate"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "pack-validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: SOV-003 key-sovereignty pack validate"
exit 0
