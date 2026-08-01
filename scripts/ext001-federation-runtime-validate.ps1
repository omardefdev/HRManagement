# EXT-001: runtime/production federation binding validate.
# Usage:
#   powershell -File scripts/ext001-federation-runtime-validate.ps1

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

function Get-Json([string]$rel) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { throw "missing $rel ($full)" }
    return Get-Content -Raw -Path $full | ConvertFrom-Json
}

$failures = @()
$m = Get-Json $MatrixPath

if ($m.card -ne "EXT-001") { $failures += "card must be EXT-001" }

foreach ($need in @(
    @{ Id = "IAM-003"; Status = "Satisfied" },
    @{ Id = "IAM-004"; Status = "Satisfied" },
    @{ Id = "STR-002"; Status = "Satisfied" },
    @{ Id = "IAM-001"; Status = "Blocked" },
    @{ Id = "national-idp-live"; Status = "Blocked" },
    @{ Id = "agency-idp-live"; Status = "Blocked" },
    @{ Id = "phishing-mfa-fleet"; Status = "Blocked" },
    @{ Id = "live-federation-conformance"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

$aals = @($m.assuranceLevels | ForEach-Object { $_.id })
foreach ($need in @("AAL1", "AAL2", "AAL3")) {
    if ($aals -notcontains $need) { $failures += "missing assurance level $need" }
}

$personas = @($m.personas)
if ($personas.Count -lt 5) { $failures += "need >= 5 personas" }
foreach ($p in $personas) {
    if ([string]::IsNullOrWhiteSpace([string]$p.minAal) -or [string]::IsNullOrWhiteSpace([string]$p.risk)) {
        $failures += "persona $($p.id) incomplete"
    }
    if ($p.risk -in @("Critical", "High") -and $p.minAal -eq "AAL1") {
        $failures += "persona $($p.id) High/Critical cannot be AAL1"
    }
}
foreach ($need in @("payroll-operator", "security-admin", "break-glass-operator")) {
    $p = @($personas | Where-Object { $_.id -eq $need } | Select-Object -First 1)
    if ($p.Count -eq 0) { $failures += "missing sensitive persona $need"; continue }
    if ([string]$p[0].minAal -ne "AAL3") { $failures += "persona $need must require AAL3" }
}

$ft = $m.federationTrust
if ($null -eq $ft) { $failures += "federationTrust missing" }
else {
    if ($ft.nationalIdp.live -eq $true) { $failures += "national IdP must not be live in this binding" }
    if ($ft.nationalIdp.labStub -ne $true) { $failures += "nationalIdp.labStub must be true" }
    $agencyIdps = @($ft.agencyIdps)
    if ($agencyIdps.Count -lt 1) { $failures += "need >= 1 agency IdP stub" }
    foreach ($a in $agencyIdps) {
        if ($a.live -eq $true) { $failures += "agency IdP $($a.id) must not be live" }
        if ($a.labStub -ne $true) { $failures += "agency IdP $($a.id) labStub must be true" }
    }
    foreach ($need in @("entityId", "signingCert", "ssoEndpoint", "logoutEndpoint")) {
        if (@($ft.metadataRequirements) -notcontains $need) { $failures += "missing metadata requirement $need" }
    }
}

$mfa = $m.mfaPolicy
if ($null -eq $mfa) { $failures += "mfaPolicy missing" }
else {
    if (@($mfa.phishingResistantRoles).Count -lt 1) { $failures += "phishingResistantRoles empty" }
    foreach ($need in @("payroll-operator", "security-admin", "break-glass-operator")) {
        if (@($mfa.phishingResistantRoles) -notcontains $need) {
            $failures += "phishingResistantRoles missing $need"
        }
    }
    if ($mfa.smsOtpForbiddenForSensitive -ne $true) { $failures += "smsOtpForbiddenForSensitive must be true" }
}

$lc = $m.lifecycle
if ($null -eq $lc) { $failures += "lifecycle missing" }
else {
    foreach ($need in @("provision", "active", "disable", "revoke")) {
        if (@($lc.states) -notcontains $need) { $failures += "lifecycle missing state $need" }
    }
    if ($lc.revokeRequiresReason -ne $true) { $failures += "revokeRequiresReason must be true" }
}

$rev = $m.revocation
if ($null -eq $rev) { $failures += "revocation missing" }
else {
    if ([int]$rev.slaMinutes -le 0) { $failures += "revocation slaMinutes must be > 0" }
    if ($rev.breachForbiddenWithoutEscalation -ne $true) {
        $failures += "breachForbiddenWithoutEscalation must be true"
    }
}

$bg = $m.breakGlass
if ($null -eq $bg) { $failures += "breakGlass missing" }
else {
    if ($bg.dualControl -ne $true) { $failures += "breakGlass.dualControl must be true" }
    if ($bg.auditRequired -ne $true) { $failures += "breakGlass.auditRequired must be true" }
    if ([int]$bg.timeBoundMinutes -le 0) { $failures += "breakGlass.timeBoundMinutes must be > 0" }
}

$sep = $m.citizenEmployeeSeparation
if ($null -eq $sep) { $failures += "citizenEmployeeSeparation missing" }
else {
    if ($sep.required -ne $true) { $failures += "citizenEmployeeSeparation.required must be true" }
    if ($sep.forbidSharedPrincipal -ne $true) { $failures += "forbidSharedPrincipal must be true" }
}

$aa = $m.attributeAuthority
if ($null -eq $aa) { $failures += "attributeAuthority missing" }
else {
    if ($aa.noNameOnlyLinking -ne $true) { $failures += "noNameOnlyLinking must be true" }
    if (@($aa.claimsAllowlist).Count -lt 1) { $failures += "claimsAllowlist empty" }
    if (@($aa.forbidLinkKeys) -notcontains "FullName") { $failures += "forbidLinkKeys must include FullName" }
}

$idpol = $m.identifierPolicy
if ($null -eq $idpol) { $failures += "identifierPolicy missing" }
else {
    if ($idpol.opaquePreferred -ne $true) { $failures += "opaquePreferred must be true" }
    if ($idpol.forbidPlainNationalIdInLogs -ne $true) { $failures += "forbidPlainNationalIdInLogs must be true" }
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
    $version = $sig.assurancePolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.assurancePolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative fixtures
$nameFail = Get-Json "testdata/ext001/scenarios/name-only-link-fail.json"
$revFail = Get-Json "testdata/ext001/scenarios/revocation-sla-breach-fail.json"

if ($nameFail.expectedValidate -ne "FAIL") { $failures += "name-only-link fixture expectedValidate must be FAIL" }
if ([string]$nameFail.linkKey -ne "FullName" -and [string]$nameFail.linkKey -ne "display_name") {
    $failures += "name-only-link fixture must use forbidden link key"
}

if ($revFail.expectedValidate -ne "FAIL") { $failures += "revocation-sla-breach fixture expectedValidate must be FAIL" }
if ($revFail.withinSla -eq $true -or $revFail.escalated -eq $true) {
    $failures += "revocation-sla-breach must be outside SLA without escalation"
}

# Positive fixtures
$sign = Get-Json "testdata/ext001/signoff/assurance-matrix-signoff.json"
$nat = Get-Json "testdata/ext001/interop/national-idp-interop-pass.json"
$agy = Get-Json "testdata/ext001/interop/agency-idp-interop-pass.json"
$revPass = Get-Json "testdata/ext001/scenarios/revocation-sla-pass.json"
$loss = Get-Json "testdata/ext001/scenarios/credential-loss-pass.json"
$outage = Get-Json "testdata/ext001/scenarios/idp-outage-failover-pass.json"
$fraud = Get-Json "testdata/ext001/adversarial/fraud-cases.json"
$ledger = Get-Json "testdata/ext001/harness/federation-run-ledger.json"

if ($sign.expectedValidate -ne "PASS") { $failures += "assurance-matrix-signoff expectedValidate must be PASS" }
if ([string]$sign.contentHash -ne [string]$sig.contentHash) { $failures += "signoff hash must match matrix" }

if ($nat.expectedValidate -ne "PASS") { $failures += "national-idp-interop expectedValidate must be PASS" }
if ($nat.live -eq $true) { $failures += "national-idp-interop live must be false" }
if ($nat.metadataComplete -ne $true) { $failures += "national-idp-interop metadataComplete must be true" }

if ($agy.expectedValidate -ne "PASS") { $failures += "agency-idp-interop expectedValidate must be PASS" }
if ($agy.live -eq $true) { $failures += "agency-idp-interop live must be false" }
if ($agy.metadataComplete -ne $true) { $failures += "agency-idp-interop metadataComplete must be true" }

if ($revPass.expectedValidate -ne "PASS") { $failures += "revocation-sla-pass expectedValidate must be PASS" }
if ($revPass.withinSla -ne $true) { $failures += "revocation-sla-pass withinSla must be true" }

if ($loss.expectedValidate -ne "PASS") { $failures += "credential-loss expectedValidate must be PASS" }
if ($outage.expectedValidate -ne "PASS") { $failures += "idp-outage-failover expectedValidate must be PASS" }

if ($fraud.expectedValidate -ne "PASS") { $failures += "fraud-cases expectedValidate must be PASS" }
if ($fraud.anyAllowWithoutReview -eq $true) { $failures += "fraud-cases anyAllowWithoutReview must be false" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "federation-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.live -eq $true) { $failures += "federation-run-ledger run $($r.runId) live must be false" }
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
    $ef = $cfg.ExtFederation
    if ($null -eq $ef) { $failures += "$rel missing ExtFederation"; continue }
    if ($ef.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($ef.LiveFederationClaim -eq $true) { $failures += "$rel LiveFederationClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-001"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    personaCount = $personas.Count
    agencyIdpCount = @($ft.agencyIdps).Count
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

Write-Host "PASS: EXT-001 federation runtime validate"
exit 0
