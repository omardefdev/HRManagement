# IAM-001: validate JWT/Keycloak/TLS production hardening artifacts.
# Usage:
#   powershell -File scripts/iam001-validate.ps1

param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()

$requiredFiles = @(
    "docs/IAM_001_JWT_KEYCLOAK.md",
    "docs/iam/realm-rebuild-checklist.md",
    "scripts/iam001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Iam001JwtKeycloakTests.cs",
    "src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs",
    "src/Shared/HRManagement.ServiceDefaults/ModuleAuthenticationExtensions.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Validate JWT doc content
$docFile = Resolve-RepoPath "docs/IAM_001_JWT_KEYCLOAK.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "IAM-001") { $failures += "doc missing IAM-001 reference" }
    if ($doc -notmatch "ValidateAudience") { $failures += "doc must mention audience validation" }
    if ($doc -notmatch "ValidateIssuer") { $failures += "doc must mention issuer validation" }
    if ($doc -notmatch "RequireHttpsMetadata") { $failures += "doc must mention HTTPS metadata" }
    if ($doc -notmatch "BehindGateway") { $failures += "doc must mention BehindGateway guard" }
    if ($doc -notmatch "fail-closed|Fail-Closed") { $failures += "doc must mention fail-closed behavior" }
}

# Validate realm rebuild checklist
$realmFile = Resolve-RepoPath "docs/iam/realm-rebuild-checklist.md"
if (Test-Path $realmFile) {
    $realm = Get-Content -Raw -Path $realmFile
    if ($realm -notmatch "hrmanagement") { $failures += "realm checklist must mention realm name" }
    if ($realm -notmatch "RS256") { $failures += "realm checklist must mention signing algorithm" }
    if ($realm -notmatch "hr-gateway-client") { $failures += "realm checklist must mention gateway client" }
    if ($realm -notmatch "bearer-only") { $failures += "realm checklist must mention bearer-only" }
    if ($realm -notmatch "audience") { $failures += "realm checklist must mention audience mapper" }
}

# Validate KeycloakExtensions has audience validation
$kcFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs"
if (Test-Path $kcFile) {
    $kc = Get-Content -Raw -Path $kcFile
    if ($kc -notmatch "ValidateAudience\s*=\s*true") { $failures += "KeycloakExtensions must set ValidateAudience=true" }
    if ($kc -notmatch "ValidateIssuer") { $failures += "KeycloakExtensions must validate issuer" }
}

# Validate ModuleAuthenticationExtensions has fail-closed guard
$modFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/ModuleAuthenticationExtensions.cs"
if (Test-Path $modFile) {
    $mod = Get-Content -Raw -Path $modFile
    if ($mod -notmatch "InvalidOperationException") { $failures += "ModuleAuth must throw on invalid production config" }
    if ($mod -notmatch "BehindGateway") { $failures += "ModuleAuth must check BehindGateway" }
}

# Validate Gateway production config
$gwProd = Resolve-RepoPath "src/Gateway/HRManagement.Gateway/appsettings.Production.json"
if (Test-Path $gwProd) {
    $gw = Get-Content -Raw -Path $gwProd | ConvertFrom-Json
    if (-not $gw.Keycloak) { $failures += "Gateway production must have Keycloak section" }
    if ($gw.Keycloak -and $gw.Keycloak.RequireHttpsMetadata -ne $true) {
        $failures += "Gateway production must require HTTPS metadata"
    }
    if ($gw.HrAuth -and $gw.HrAuth.CookieSecureAlways -ne $true) {
        $failures += "Gateway production must set CookieSecureAlways"
    }
}

$outDir = Resolve-RepoPath "testdata/iam001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "IAM-001"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outDir "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: IAM-001 JWT/Keycloak/TLS production hardening validate"
exit 0
