# EXT-001: validate federation/assurance matrix and harness fixtures.
# Usage:
#   powershell -File scripts/ext001-federation-validate.ps1

param(
    [string]$MatrixPath = "docs/ext-federation/assurance-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/ext001/out"
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

if ($m.card -ne "EXT-001") { $failures += "card must be EXT-001" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("AssuranceMapping", "FederationInterop", "RevocationSla", "NoNameOnlyLink", "CredentialLoss", "IdpOutageFailover", "FraudCases")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

$aalIds = @($m.assuranceLevels | ForEach-Object { $_.id })
foreach ($need in @("AAL1", "AAL2", "AAL3")) {
    if ($aalIds -notcontains $need) { $failures += "missing assurance level $need" }
}

if (@($m.personas).Count -lt 5) { $failures += "need >= 5 personas" }
foreach ($p in $m.personas) {
    if ([string]::IsNullOrWhiteSpace($p.minAal) -or [string]::IsNullOrWhiteSpace($p.risk)) {
        $failures += "persona $($p.id) incomplete"
    }
    if ($p.risk -in @("Critical", "High") -and $p.minAal -eq "AAL1") {
        $failures += "persona $($p.id) High/Critical cannot be AAL1"
    }
}

$ft = $m.federationTrust
if ($ft.nationalIdp.live -eq $true) { $failures += "national IdP must not be live in this pack" }
if (@($ft.agencyIdps).Count -lt 1) { $failures += "need >= 1 agency IdP stub" }
foreach ($req in @($ft.metadataRequirements)) {
    if ([string]::IsNullOrWhiteSpace($req)) { $failures += "empty metadata requirement" }
}
if (@($ft.metadataRequirements).Count -lt 4) { $failures += "metadataRequirements incomplete" }

if (@($m.mfaPolicy.phishingResistantRoles).Count -lt 1) { $failures += "phishingResistantRoles empty" }
if (-not $m.mfaPolicy.smsOtpForbiddenForSensitive) { $failures += "smsOtpForbiddenForSensitive must be true" }

if ([int]$m.revocation.slaMinutes -le 0) { $failures += "revocation slaMinutes invalid" }
if (-not $m.breakGlass.dualControl -or -not $m.breakGlass.auditRequired) {
    $failures += "breakGlass dualControl/audit required"
}
if (-not $m.citizenEmployeeSeparation.required -or -not $m.citizenEmployeeSeparation.forbidSharedPrincipal) {
    $failures += "citizenEmployeeSeparation incomplete"
}
if (-not $m.attributeAuthority.noNameOnlyLinking) { $failures += "noNameOnlyLinking must be true" }
if (-not $m.identifierPolicy.opaquePreferred -or -not $m.identifierPolicy.forbidPlainNationalIdInLogs) {
    $failures += "identifierPolicy incomplete"
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
if ($blockerMap["IAM-003"] -ne "Satisfied") { $failures += "IAM-003 must be Satisfied" }
if ($blockerMap["IAM-004"] -ne "Satisfied") { $failures += "IAM-004 must be Satisfied" }
if ($blockerMap["STR-002"] -ne "Satisfied") { $failures += "STR-002 must be Satisfied" }
if ($blockerMap["IAM-001"] -ne "Blocked") { $failures += "IAM-001 must be Blocked" }
if ($blockerMap["national-idp-live"] -ne "Blocked") { $failures += "national-idp-live must be Blocked" }
if ($blockerMap["agency-idp-live"] -ne "Blocked") { $failures += "agency-idp-live must be Blocked" }
if ($blockerMap["phishing-mfa-fleet"] -ne "Blocked") { $failures += "phishing-mfa-fleet must be Blocked" }
if ($blockerMap["live-federation-conformance"] -ne "Blocked") { $failures += "live-federation-conformance must be Blocked" }

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

$sign = Assert-FixtureExpected $m.simulationPaths.assuranceMatrixSignoff "PASS"
$nat = Assert-FixtureExpected $m.simulationPaths.nationalIdpInteropPass "PASS"
$agy = Assert-FixtureExpected $m.simulationPaths.agencyIdpInteropPass "PASS"
$name = Assert-FixtureExpected $m.simulationPaths.nameOnlyLinkFail "FAIL"
$revP = Assert-FixtureExpected $m.simulationPaths.revocationSlaPass "PASS"
$revF = Assert-FixtureExpected $m.simulationPaths.revocationSlaBreachFail "FAIL"
$loss = Assert-FixtureExpected $m.simulationPaths.credentialLossPass "PASS"
$out = Assert-FixtureExpected $m.simulationPaths.idpOutageFailoverPass "PASS"
$fraud = Assert-FixtureExpected $m.simulationPaths.fraudCases "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.federationRunLedger $null

if ($null -ne $sign) {
    if ($sign.signedByRole -eq $sign.reviewerRole) { $failures += "signoff roles must differ" }
    if ($sign.contentHash -ne $sig.contentHash) { $failures += "signoff hash must match matrix" }
    if ($sign.assurancePolicyVersion -ne $sig.assurancePolicyVersion) { $failures += "signoff version mismatch" }
}

if ($null -ne $nat) {
    if ($nat.live -eq $true -or $nat.metadataComplete -ne $true) { $failures += "national interop lab incomplete" }
}
if ($null -ne $agy) {
    if ($agy.live -eq $true -or $agy.metadataComplete -ne $true) { $failures += "agency interop lab incomplete" }
}

if ($null -ne $name) {
    if ($m.attributeAuthority.forbidLinkKeys -notcontains $name.linkKey -and $name.linkKey -ne "FullName") {
        # FullName is in forbid list
    }
    if ($name.linkKey -notin @($m.attributeAuthority.forbidLinkKeys) -and $name.linkKey -ne "FullName") {
        if (@($m.attributeAuthority.forbidLinkKeys) -notcontains $name.linkKey) {
            # allow FullName which is in forbidLinkKeys
        }
    }
    if ($name.expectedValidate -ne "FAIL") { $failures += "name-only must be FAIL" }
    if ($name.linkKey -notin @("FullName", "display_name") -and @($m.attributeAuthority.forbidLinkKeys) -notcontains $name.linkKey) {
        $failures += "name-only FAIL should use forbidden link key"
    }
}

if ($null -ne $revP) {
    if ($revP.withinSla -ne $true) { $failures += "revocation pass must be within SLA" }
    if ([int]$revP.elapsedMinutes -gt [int]$m.revocation.slaMinutes) { $failures += "revocation pass elapsed exceeds SLA" }
}
if ($null -ne $revF) {
    if ($revF.withinSla -eq $true -or $revF.escalated -eq $true) {
        $failures += "revocation FAIL must be breach without escalation"
    }
}

if ($null -ne $loss) {
    if ($loss.sessionInvalidated -ne $true -or $loss.reasonPresent -ne $true) {
        $failures += "credential loss incomplete"
    }
}

if ($null -ne $out) {
    if ($out.failoverActivated -ne $true -or $out.noUnauthenticatedAccess -ne $true) {
        $failures += "outage failover incomplete"
    }
    if ($out.liveConformance -eq $true) { $failures += "liveConformance must be false" }
}

if ($null -ne $fraud) {
    if ($fraud.anyAllowWithoutReview -eq $true) { $failures += "fraud cases must not allow without review" }
    if (@($fraud.cases).Count -lt 2) { $failures += "need >= 2 fraud cases" }
    foreach ($c in $fraud.cases) {
        if ($c.forcedReview -ne $true -or $c.outcome -ne "block") {
            $failures += "fraud case $($c.id) must block with review"
        }
    }
}

if ($null -ne $ledger -and @($ledger.runs).Count -lt 1) {
    $failures += "federation ledger empty"
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-001"
    ok = ($failures.Count -eq 0)
    personaCount = @($m.personas).Count
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

Write-Host "PASS: EXT-001 federation validate"
exit 0
