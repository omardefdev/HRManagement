# EXT-004: runtime/production pki-signature binding validate.
# Usage:
#   powershell -File scripts/ext004-pki-signature-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/ext-pki/pki-signature-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/ext004/out"
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

if ($m.card -ne "EXT-004") { $failures += "card must be EXT-004" }

foreach ($need in @(
    @{ Id = "STR-002"; Status = "Satisfied" },
    @{ Id = "OBS-003"; Status = "Satisfied" },
    @{ Id = "SOV-003"; Status = "Satisfied" },
    @{ Id = "national-pki-live"; Status = "Blocked" },
    @{ Id = "hsm-prod"; Status = "Blocked" },
    @{ Id = "tsa-live"; Status = "Blocked" },
    @{ Id = "SYD-011"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.signerIntentRequired -ne $true) { $failures += "signerIntentRequired must be true" }
if ($m.signerRoleRequired -ne $true) { $failures += "signerRoleRequired must be true" }

if ($m.signatures.formSignatureNotLegalEvidence -ne $true) {
    $failures += "formSignatureNotLegalEvidence must be true"
}
foreach ($need in @("document", "approval")) {
    if (@($m.signatures.kinds) -notcontains $need) { $failures += "signatures.kinds missing $need" }
}

if ($m.organizationalSeals.cryptoSealRequiredForLegal -ne $true) {
    $failures += "cryptoSealRequiredForLegal must be true"
}
if ($m.organizationalSeals.metadataOnlyInsufficient -ne $true) {
    $failures += "metadataOnlyInsufficient must be true"
}

if ($m.trustedTimestamp.requiredForLtv -ne $true) { $failures += "requiredForLtv must be true" }
if ([string]$m.trustedTimestamp.tsaOutageDegrade -ne "hold-not-valid") {
    $failures += "tsaOutageDegrade must be hold-not-valid"
}

if ($m.certValidation.checkExpiry -ne $true -or $m.certValidation.checkRevocation -ne $true -or $m.certValidation.failClosedOnUnknown -ne $true) {
    $failures += "certValidation checks must all be true"
}

if ($m.revocation.failClosed -ne $true) { $failures += "revocation.failClosed must be true" }
foreach ($need in @("CRL", "OCSP")) {
    if (@($m.revocation.sources) -notcontains $need) { $failures += "revocation.sources missing $need" }
}

if ($m.ltv.requiredAfterExpiry -ne $true) { $failures += "ltv.requiredAfterExpiry must be true" }
if ($m.ltv.archiveEvidenceRequired -ne $true) { $failures += "ltv.archiveEvidenceRequired must be true" }

if ($m.hsmCustody.privateKeyNeverLeavesHsm -ne $true) { $failures += "privateKeyNeverLeavesHsm must be true" }
if ($m.hsmCustody.labMockAllowed -ne $true) { $failures += "labMockAllowed must be true" }

if ($m.clockSkewPolicy.failClosed -ne $true) { $failures += "clockSkewPolicy.failClosed must be true" }
if ([int]$m.clockSkewPolicy.maxSkewSeconds -le 0) { $failures += "maxSkewSeconds must be > 0" }

foreach ($need in @("RSA-PSS-SHA256", "ECDSA-P256-SHA256")) {
    if (@($m.algorithmAgility.allowedAlgs) -notcontains $need) { $failures += "allowedAlgs missing $need" }
}
foreach ($need in @("MD5", "SHA1", "RSA-PKCS1-SHA1")) {
    if (@($m.algorithmAgility.deprecatedAlgs) -notcontains $need) { $failures += "deprecatedAlgs missing $need" }
}

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("signatureId", "signerId", "signerRole", "outcome", "algorithm", "signedAt")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "mockSandbox.labStub must be true" }
if ($m.mockSandbox.livePkiForbidden -ne $true) { $failures += "livePkiForbidden must be true" }
if ($m.mockSandbox.mockCa -ne $true -or $m.mockSandbox.mockTsa -ne $true -or $m.mockSandbox.mockHsm -ne $true) {
    $failures += "mockCa/mockTsa/mockHsm must be true"
}

$sig = $m.signing
if ($null -eq $sig) {
    $failures += "signing missing"
}
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.pkiPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.pkiPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative fixtures
$missingRole = Get-Json "testdata/ext004/scenarios/missing-signer-role-fail.json"
$revoked = Get-Json "testdata/ext004/scenarios/revoked-cert-fail.json"
$expiredNoLtv = Get-Json "testdata/ext004/scenarios/expired-without-ltv-fail.json"
$clockFail = Get-Json "testdata/ext004/scenarios/clock-skew-fail.json"
$keyExfil = Get-Json "testdata/ext004/scenarios/key-exfil-fail.json"
$depAlg = Get-Json "testdata/ext004/scenarios/deprecated-alg-fail.json"
$formLegal = Get-Json "testdata/ext004/scenarios/form-signature-as-legal-fail.json"

if ($missingRole.expectedValidate -ne "FAIL") { $failures += "missing-signer-role expectedValidate must be FAIL" }
if (-not [string]::IsNullOrWhiteSpace([string]$missingRole.signerRole)) {
    $failures += "missing-signer-role fixture must have null/empty signerRole"
}

if ($revoked.expectedValidate -ne "FAIL") { $failures += "revoked-cert expectedValidate must be FAIL" }
if ($revoked.treatedAsValid -ne $true) { $failures += "revoked-cert must treat revoked as valid" }

if ($expiredNoLtv.expectedValidate -ne "FAIL") { $failures += "expired-without-ltv expectedValidate must be FAIL" }
if ($expiredNoLtv.certExpired -ne $true -or $expiredNoLtv.ltvPackagePresent -eq $true) {
    $failures += "expired-without-ltv fixture must be expired without LTV"
}

if ($clockFail.expectedValidate -ne "FAIL") { $failures += "clock-skew expectedValidate must be FAIL" }
if ([int]$clockFail.clockSkewSeconds -le [int]$m.clockSkewPolicy.maxSkewSeconds) {
    $failures += "clock-skew fixture must exceed maxSkewSeconds"
}

if ($keyExfil.expectedValidate -ne "FAIL") { $failures += "key-exfil expectedValidate must be FAIL" }
if ($keyExfil.privateKeyExported -ne $true) { $failures += "key-exfil must export private key" }

if ($depAlg.expectedValidate -ne "FAIL") { $failures += "deprecated-alg expectedValidate must be FAIL" }
if (@($m.algorithmAgility.deprecatedAlgs) -notcontains [string]$depAlg.algorithm) {
    $failures += "deprecated-alg fixture algorithm must be in deprecatedAlgs"
}

if ($formLegal.expectedValidate -ne "FAIL") { $failures += "form-signature-as-legal expectedValidate must be FAIL" }
if ($formLegal.claimedAsLegalEvidence -ne $true) { $failures += "form-signature-as-legal must claim legal evidence" }

# Positive fixtures
$ltvPass = Get-Json "testdata/ext004/scenarios/expired-cert-ltv-pass.json"
$tsaPass = Get-Json "testdata/ext004/scenarios/tsa-outage-degrade-pass.json"
$archivePass = Get-Json "testdata/ext004/scenarios/archive-restore-verify-pass.json"
$auditPass = Get-Json "testdata/ext004/audit/signature-audit-pass.json"
$ledger = Get-Json "testdata/ext004/harness/pki-run-ledger.json"

if ($ltvPass.expectedValidate -ne "PASS") { $failures += "expired-cert-ltv expectedValidate must be PASS" }
if ($ltvPass.ltvPackagePresent -ne $true) { $failures += "expired-cert-ltv must have LTV package" }

if ($tsaPass.expectedValidate -ne "PASS") { $failures += "tsa-outage-degrade expectedValidate must be PASS" }
if ($tsaPass.heldNotValid -ne $true -or $tsaPass.treatedAsLtvComplete -eq $true) {
    $failures += "tsa-outage must hold-not-valid and not treat as LTV complete"
}

if ($archivePass.expectedValidate -ne "PASS") { $failures += "archive-restore-verify expectedValidate must be PASS" }
if ($archivePass.ltvReverified -ne $true) { $failures += "archive-restore must re-verify LTV" }

if ($auditPass.expectedValidate -ne "PASS") { $failures += "signature-audit expectedValidate must be PASS" }
if (@($auditPass.entries).Count -lt 1) { $failures += "signature-audit needs entries" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "pki-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.livePki -eq $true) { $failures += "ledger run $($r.runId) livePki must be false" }
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
    $ep = $cfg.ExtPki
    if ($null -eq $ep) { $failures += "$rel missing ExtPki"; continue }
    if ($ep.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($ep.LivePkiClaim -eq $true) { $failures += "$rel LivePkiClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-004"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    allowedAlgCount = @($m.algorithmAgility.allowedAlgs).Count
    auditFieldCount = @($m.audit.requiredFields).Count
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

Write-Host "PASS: EXT-004 pki-signature runtime validate"
exit 0
