# SEC-002: validate Lookup protection deliverables (auth, DTO, rate limit).
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

function Need([string]$rel) {
    $p = Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $p)) { $script:failures.Add("missing $rel") }
    return $p
}

Need "src/Tests/HRManagement.UnitTests/Sec002LookupProtectionTests.cs" | Out-Null
Need "scripts/sec002-validate.ps1" | Out-Null

$controllerPath = Need "src/Services/HRCore/HR.Core.Web/Controllers/LookupApiController.cs"
if (Test-Path $controllerPath) {
    $controller = Get-Content -Raw $controllerPath
    if ($controller -notmatch 'Authorize\s*\(\s*Policy\s*=\s*HrManagementPolicies\.HrCoreLookup\s*\)') {
        $failures.Add("LookupApiController must have [Authorize(Policy = HrManagementPolicies.HrCoreLookup)]")
    }
    if ($controller -match '\[AllowAnonymous[^\]]*\]\s*(?:\[[^\]]+\]\s*)*public\s+class\s+LookupApiController') {
        $failures.Add("LookupApiController class must not have [AllowAnonymous]")
    }
    if ($controller -notmatch 'LookupItemDto\s*\(\s*Guid\s+Id\s*,\s*string\s+Label\s*,\s*string\?\s+Sublabel\s*\)') {
        $failures.Add("LookupItemDto must expose only Id, Label, Sublabel")
    }
    if ($controller -match 'NationalId') {
        $failures.Add("LookupApiController must not reference NationalId")
    }
    if ($controller -notmatch 'MinSearchLength\s*=\s*2') {
        $failures.Add("MinSearchLength must be 2")
    }
}

$gatewayPath = Need "src/Gateway/HRManagement.Gateway/Program.cs"
if (Test-Path $gatewayPath) {
    $gateway = Get-Content -Raw $gatewayPath
    if ($gateway -notmatch 'AddRateLimiter') { $failures.Add("Gateway Program.cs must call AddRateLimiter") }
    if ($gateway -notmatch 'UseRateLimiter') { $failures.Add("Gateway Program.cs must call UseRateLimiter") }
}

if ($failures.Count -gt 0) {
    Write-Host "SEC-002 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "SEC-002 validation OK."
exit 0
