# IAM-002: validate fail-closed org scope deliverables.
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

Need "docs/IAM_002_ORG_SCOPE.md" | Out-Null
Need "scripts/iam002-validate.ps1" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Iam002OrgScopeTests.cs" | Out-Null

$handlerPath = Need "src/Shared/HRManagement.ServiceDefaults/OrgScopeAuthorizationHandler.cs"
if (Test-Path $handlerPath) {
    $handler = Get-Content -Raw $handlerPath
    if ($handler -notmatch "OrgScopeAuthorizationHandler") { $failures.Add("OrgScopeAuthorizationHandler class required") }
    if ($handler -notmatch "OrgScopeRequirement") { $failures.Add("OrgScopeRequirement class required") }
    if ($handler -notmatch "InvalidClaim") { $failures.Add("Handler must reject InvalidClaim") }
    if ($handler -notmatch "Fail") { $failures.Add("Handler must call Fail for invalid claims") }
}

$scopePath = Need "src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs"
if (Test-Path $scopePath) {
    $scope = Get-Content -Raw $scopePath
    if ($scope -notmatch "OrgAccessScope") { $failures.Add("OrgAccessScope type required") }
    if ($scope -notmatch "Denied") { $failures.Add("OrgAccessScope.Denied required") }
    if ($scope -notmatch "IsDenied") { $failures.Add("IsDenied property required") }
}

$extPath = Need "src/Shared/HRManagement.ServiceDefaults/OrgScopeControllerExtensions.cs"
if (Test-Path $extPath) {
    $ext = Get-Content -Raw $extPath
    if ($ext -notmatch "ForbidIfOrgScopeDenied") { $failures.Add("ForbidIfOrgScopeDenied required") }
    if ($ext -notmatch "WhereInOrgScope") { $failures.Add("WhereInOrgScope required") }
}

if ($failures.Count -gt 0) {
    Write-Host "IAM-002 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "IAM-002 validation OK."
exit 0
