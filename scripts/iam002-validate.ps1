# IAM-002: validate fail-closed org scope deliverables.
# PLAN-52 (P-4): the unused OrgScopeRequirement and its handler are gone; this script checks the mechanism that replaced them (the
# registry of scope keys, the stance attributes and the one action filter) and that the handler does not come back.
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

$oldHandler = Join-Path $RepoRoot ("src/Shared/HRManagement.ServiceDefaults/OrgScopeAuthorizationHandler.cs" -replace '/', [IO.Path]::DirectorySeparatorChar)
if (Test-Path $oldHandler) { $failures.Add("OrgScopeAuthorizationHandler.cs is deleted (PLAN-52): no policy used it") }

$registryPath = Need "src/Shared/HRManagement.ServiceDefaults/OrgScope/OrgScopeKeyRegistry.cs"
if (Test-Path $registryPath) {
    $registry = Get-Content -Raw $registryPath
    if ($registry -notmatch "class OrgScopeKeyRegistry") { $failures.Add("OrgScopeKeyRegistry class required") }
    if ($registry -notmatch "Guid\.Empty") { $failures.Add("a null unit must never be in scope (coalesce to Guid.Empty)") }
}

$attributesPath = Need "src/Shared/HRManagement.ServiceDefaults/OrgScope/OrgScopeAttributes.cs"
if (Test-Path $attributesPath) {
    $attributes = Get-Content -Raw $attributesPath
    foreach ($name in @("OrgScopedAttribute", "OrgScopeCentralOnlyAttribute", "OrgScopeNotApplicableAttribute", "OrgScopeIdAttribute", "NotOrgScopeKeyAttribute")) {
        if ($attributes -notmatch "class $name") { $failures.Add("$name required") }
    }
}

$filterPath = Need "src/Shared/HRManagement.ServiceDefaults/OrgScope/OrgScopeActionFilter.cs"
if (Test-Path $filterPath) {
    $filter = Get-Content -Raw $filterPath
    if ($filter -notmatch "class OrgScopeActionFilter") { $failures.Add("OrgScopeActionFilter class required") }
    if ($filter -notmatch "IsDenied") { $failures.Add("the filter must refuse a denied scope") }
    if ($filter -notmatch "MustFilter") { $failures.Add("the filter must act for restricted users only") }
    if ($filter -notmatch "ForbidResult") { $failures.Add("the filter must answer 403 for a denied scope") }
    if ($filter -notmatch "NotFoundResult") { $failures.Add("the filter must answer 404 out of scope (D-52-5)") }
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
    if ($ext -notmatch "OrgScopeKeyRegistry") { $failures.Add("WhereInOrgScope over the registry required") }
}

$srcRoot = Join-Path $RepoRoot "src"
$dead = Get-ChildItem -Path $srcRoot -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\/](bin|obj)[\/]' } |
    Where-Object { (Get-Content -Raw $_.FullName) -match 'OrgScopeRequirement|AddOrgScopeAuthorization' }
foreach ($file in $dead) { $failures.Add("OrgScopeRequirement is deleted (PLAN-52) but $($file.Name) still names it") }

if ($failures.Count -gt 0) {
    Write-Host "IAM-002 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "IAM-002 validation OK."
exit 0
