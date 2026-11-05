#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-42: a text inventory of the trust boundary. No service client signs a proof, no shared secret is left, and no internal route
  carries a user policy. ASCII only, Windows PowerShell 5.1 and PowerShell 7. Exit 1 on the first list that is not empty.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/s2s042-validate.ps1
#>
param([string]$RepoRoot = "")

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
Set-Location $RepoRoot
$problems = New-Object System.Collections.Generic.List[string]

function Get-TrackedFiles([string[]]$Paths) {
    return @(& git ls-files -- @Paths | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) })
}

# 1. Nothing outside the gateway signs a proof.
$allowedSigners = @(
    "src/Shared/HRManagement.ServiceDefaults/GatewayCallerProof.cs",
    "src/Shared/HRManagement.ServiceDefaults/GatewayCallerProofMiddleware.cs",
    "src/Shared/HRManagement.ServiceDefaults/GatewayAccessTokenTransform.cs"
)
foreach ($file in (Get-TrackedFiles @("src/*.cs") | Where-Object { $_ -notlike "src/Tests/*" -and $_ -notlike "src/Gateway/*" })) {
    if ($allowedSigners -contains $file) { continue }
    $text = [System.IO.File]::ReadAllText((Join-Path $RepoRoot $file))
    if ($text -match 'GatewayCallerProof\.Sign|GatewayProofSigningHandler') { $problems.Add("$file signs a gateway proof") }
}

# 2. The shared secret is gone from the deployment files.
foreach ($file in (Get-TrackedFiles @("deploy"))) {
    $text = [System.IO.File]::ReadAllText((Join-Path $RepoRoot $file))
    if ($text.Contains("hr-app-secrets")) { $problems.Add("$file mentions hr-app-secrets") }
}

# 3. The private key reaches the gateway alone.
foreach ($file in (Get-TrackedFiles @("deploy/k8s/overlays") | Where-Object { $_ -like "*-secrets.yaml" })) {
    $text = [System.IO.File]::ReadAllText((Join-Path $RepoRoot $file))
    if ($text.Contains("GatewayCallerProof__PrivateKeyPem") -and $file -notlike "*/hr-gateway-secrets.yaml") {
        $problems.Add("$file holds the private key of the gateway proof")
    }
}
foreach ($compose in @("docker-compose.production.yml", "docker-compose.acceptance.yml")) {
    $lines = [System.IO.File]::ReadAllLines((Join-Path $RepoRoot $compose))
    $service = ""
    foreach ($line in $lines) {
        if ($line -match '^  ([a-z][a-z0-9-]*):\s*$') { $service = $Matches[1] }
        if ($line -match 'GatewayCallerProof__PrivateKeyPem' -and $service -ne "hr-gateway") {
            $problems.Add("$compose gives the private key to $service")
        }
    }
}

# 4. No internal route with a user policy: the architecture test holds it by reflection, this is the text view.
foreach ($file in (Get-TrackedFiles @("src/*.cs") | Where-Object { $_ -match '/Controllers/' -and $_ -notlike "src/Tests/*" })) {
    $text = [System.IO.File]::ReadAllText((Join-Path $RepoRoot $file))
    if ($text -notmatch '\[Route\("api/internal') { continue }
    if ($text -match 'Authorize\(Policy = HrManagementPolicies\.') { $problems.Add("$file has an internal route and a user policy") }
    if ($text -match 'AllowAnonymous') { $problems.Add("$file has an internal route and AllowAnonymous") }
}

if ($problems.Count -gt 0) {
    Write-Host "s2s042-validate: FAIL ($($problems.Count))"
    foreach ($problem in $problems) { Write-Host "  - $problem" }
    exit 1
}
Write-Host "s2s042-validate: PASS"
exit 0
