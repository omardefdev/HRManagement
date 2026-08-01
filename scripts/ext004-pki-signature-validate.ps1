# EXT-004: validate PKI/signature contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/ext004-pki-signature-validate.ps1

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

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "EXT-004") { $failures += "card must be EXT-004" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("SignApproval", "OrgSeal", "TrustedTimestamp", "RevocationFailClosed", "ExpiredCertLtv", "ClockFailure", "HsmKeyCustody", "ArchiveRestoreVerify", "AlgorithmAgility", "FormSignatureGate")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

if (-not $m.signerIntentRequired) { $failures += "signerIntentRequired must be true" }
if (-not $m.signerRoleRequired) { $failures += "signerRoleRequired must be true" }
if (-not $m.signatures.formSignatureNotLegalEvidence) { $failures += "formSignatureNotLegalEvidence" }
if (-not $m.organizationalSeals.cryptoSealRequiredForLegal) { $failures += "cryptoSealRequiredForLegal" }
if (-not $m.organizationalSeals.metadataOnlyInsufficient) { $failures += "metadataOnlyInsufficient" }
if (-not $m.trustedTimestamp.requiredForLtv) { $failures += "requiredForLtv" }
if (-not $m.certValidation.checkExpiry -or -not $m.certValidation.checkRevocation) { $failures += "certValidation checks" }
if (-not $m.certValidation.failClosedOnUnknown) { $failures += "failClosedOnUnknown" }
if (-not $m.revocation.failClosed) { $failures += "revocation.failClosed" }
if (-not $m.ltv.requiredAfterExpiry -or -not $m.ltv.archiveEvidenceRequired) { $failures += "ltv flags" }
if (-not $m.hsmCustody.privateKeyNeverLeavesHsm) { $failures += "privateKeyNeverLeavesHsm" }
if (-not $m.clockSkewPolicy.failClosed) { $failures += "clockSkew failClosed" }
if ([int]$m.clockSkewPolicy.maxSkewSeconds -le 0) { $failures += "maxSkewSeconds invalid" }

$allowed = @($m.algorithmAgility.allowedAlgs)
$deprecated = @($m.algorithmAgility.deprecatedAlgs)
if ($allowed.Count -lt 1) { $failures += "allowedAlgs empty" }
if ($deprecated.Count -lt 1) { $failures += "deprecatedAlgs empty" }

if (-not $m.audit.required) { $failures += "audit.required" }
$auditFields = @($m.audit.requiredFields)
foreach ($need in @("signatureId", "signerId", "signerRole", "outcome", "algorithm", "signedAt")) {
    if ($auditFields -notcontains $need) { $failures += "audit missing $need" }
}

if (-not $m.mockSandbox.labStub -or -not $m.mockSandbox.livePkiForbidden) {
    $failures += "mockSandbox must be labStub and livePkiForbidden"
}

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace($sig.contentHash)) { $failures += "signing contentHash required" }
if ($sig.signedByRole -eq $sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($blockerMap["STR-002"] -ne "Satisfied") { $failures += "STR-002 must be Satisfied" }
if ($blockerMap["OBS-003"] -ne "Satisfied") { $failures += "OBS-003 must be Satisfied" }
if ($blockerMap["SOV-003"] -ne "Satisfied") { $failures += "SOV-003 must be Satisfied" }
if ($blockerMap["national-pki-live"] -ne "Blocked") { $failures += "national-pki-live must be Blocked" }
if ($blockerMap["hsm-prod"] -ne "Blocked") { $failures += "hsm-prod must be Blocked" }
if ($blockerMap["tsa-live"] -ne "Blocked") { $failures += "tsa-live must be Blocked" }
if ($blockerMap["SYD-011"] -ne "Blocked") { $failures += "SYD-011 must be Blocked" }

function Assert-FixtureExpected([string]$rel, [string]$expect) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $script:failures += "missing fixture $rel"
        return $null
    }
    $obj = Get-Content -Raw -Path $full | ConvertFrom-Json
    if (-not [string]::IsNullOrEmpty($expect) -and $obj.expectedValidate -ne $expect) {
        $script:failures += "fixture $rel expectedValidate must be $expect"
    }
    return $obj
}

$miss = Assert-FixtureExpected $m.simulationPaths.missingSignerRoleFail "FAIL"
$rev = Assert-FixtureExpected $m.simulationPaths.revokedCertFail "FAIL"
$expFail = Assert-FixtureExpected $m.simulationPaths.expiredWithoutLtvFail "FAIL"
$expPass = Assert-FixtureExpected $m.simulationPaths.expiredCertLtvPass "PASS"
$clk = Assert-FixtureExpected $m.simulationPaths.clockSkewFail "FAIL"
$key = Assert-FixtureExpected $m.simulationPaths.keyExfilFail "FAIL"
$tsa = Assert-FixtureExpected $m.simulationPaths.tsaOutageDegradePass "PASS"
$arc = Assert-FixtureExpected $m.simulationPaths.archiveRestoreVerifyPass "PASS"
$alg = Assert-FixtureExpected $m.simulationPaths.deprecatedAlgFail "FAIL"
$form = Assert-FixtureExpected $m.simulationPaths.formSignatureAsLegalFail "FAIL"
$audit = Assert-FixtureExpected $m.simulationPaths.signatureAuditPass "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.pkiRunLedger $null

if ($null -ne $miss) {
    if ($null -ne $miss.signerRole -and -not [string]::IsNullOrWhiteSpace([string]$miss.signerRole)) {
        $failures += "missing-role FAIL must have null/empty signerRole"
    }
}

if ($null -ne $rev) {
    if ($rev.certStatus -ne "Revoked" -or $rev.treatedAsValid -ne $true) {
        $failures += "revoked FAIL incomplete"
    }
}

if ($null -ne $expFail) {
    if ($expFail.certExpired -ne $true -or $expFail.ltvPackagePresent -eq $true -or $expFail.treatedAsValid -ne $true) {
        $failures += "expired-without-ltv FAIL incomplete"
    }
}

if ($null -ne $expPass) {
    if ($expPass.ltvPackagePresent -ne $true -or $expPass.tsaTokenPresent -ne $true) {
        $failures += "expired LTV PASS needs ltv and tsa"
    }
}

if ($null -ne $clk) {
    if ([int]$clk.clockSkewSeconds -le [int]$m.clockSkewPolicy.maxSkewSeconds) {
        $failures += "clock FAIL skew must exceed maxSkewSeconds"
    }
    if ($clk.treatedAsValid -ne $true) { $failures += "clock FAIL must treat as valid" }
}

if ($null -ne $key) {
    if ($key.privateKeyExported -ne $true) { $failures += "key-exfil FAIL needs privateKeyExported" }
}

if ($null -ne $tsa) {
    if ($tsa.tsaUnavailable -ne $true -or $tsa.heldNotValid -ne $true -or $tsa.treatedAsLtvComplete -eq $true) {
        $failures += "tsa outage PASS incomplete"
    }
}

if ($null -ne $arc) {
    if ($arc.archiveRestored -ne $true -or $arc.ltvReverified -ne $true -or $arc.hashChainIntact -ne $true) {
        $failures += "archive restore PASS incomplete"
    }
}

if ($null -ne $alg) {
    if ($deprecated -notcontains $alg.algorithm) {
        $failures += "deprecated-alg FAIL algorithm must be in deprecatedAlgs"
    }
    if ($alg.treatedAsValid -ne $true) { $failures += "deprecated-alg FAIL must treat as valid" }
}

if ($null -ne $form) {
    if ($form.formSignature -ne $true -or $form.claimedAsLegalEvidence -ne $true) {
        $failures += "form-as-legal FAIL incomplete"
    }
}

if ($null -ne $audit) {
    if (@($audit.entries).Count -lt 1) { $failures += "audit entries empty" }
    foreach ($e in $audit.entries) {
        foreach ($f in $auditFields) {
            if ($null -eq $e.$f -or ($e.$f -is [string] -and [string]::IsNullOrWhiteSpace($e.$f))) {
                $failures += "audit entry missing $f"
            }
        }
        if ($allowed -notcontains $e.algorithm) {
            $failures += "audit algorithm $($e.algorithm) not allowlisted"
        }
    }
}

if ($null -ne $ledger) {
    if (@($ledger.runs).Count -lt 1) { $failures += "pki ledger empty" }
    foreach ($r in $ledger.runs) {
        if ($r.livePki -eq $true) { $failures += "ledger run must not be livePki" }
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-004"
    ok = ($failures.Count -eq 0)
    journeyCount = $journeyIds.Count
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

Write-Host "PASS: EXT-004 pki-signature validate"
exit 0
