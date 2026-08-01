# SEC-003: validate file object authorization deliverables.
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

Need "docs/SEC_003_FILE_AUTHORIZATION.md" | Out-Null
Need "scripts/sec003-validate.ps1" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Sec003FileAuthorizationTests.cs" | Out-Null
Need "src/Tests/HRManagement.IntegrationTests/Sec003FileAccessIntegrationTests.cs" | Out-Null
Need "src/Services/FileStorage/File.Storage.Infrastructure/Services/FileAccessGrantService.cs" | Out-Null
Need "src/Services/FileStorage/File.Storage.Infrastructure/Migrations/20260731180000_SEC003_FileAccessGrants.cs" | Out-Null

$grantPath = Need "src/Shared/HRManagement.SharedKernel/Files/FileAccessGrant.cs"
if (Test-Path $grantPath) {
    $grant = Get-Content -Raw $grantPath
    foreach ($prop in @("TenantId", "FileId", "Module", "HolderType", "HolderId", "FileReference", "Visibility", "IsRevoked")) {
        if ($grant -notmatch $prop) { $failures.Add("FileAccessGrant missing property $prop") }
    }
    if ($grant -notmatch "FileVisibility") { $failures.Add("FileVisibility enum required") }
    if ($grant -notmatch "PresignedUrlMaxLifetimeSeconds") { $failures.Add("PresignedUrlMaxLifetimeSeconds required") }
}

$policyPath = Need "src/Shared/HRManagement.ServiceDefaults/Files/IFileAccessPolicy.cs"
if (Test-Path $policyPath) {
    $policy = Get-Content -Raw $policyPath
    if ($policy -notmatch "interface IFileAccessPolicy") { $failures.Add("IFileAccessPolicy interface required") }
    if ($policy -notmatch "class FileAccessPolicy") { $failures.Add("FileAccessPolicy class required") }
    if ($policy -notmatch "EvaluateDelete") { $failures.Add("EvaluateDelete method required") }
    if ($policy -notmatch "Cross-tenant") { $failures.Add("Cross-tenant denial required") }
}

# Runtime wiring checks (SEC-003 production gate).
$programPath = Need "src/Services/FileStorage/File.Storage.Web/Program.cs"
if (Test-Path $programPath) {
    $program = Get-Content -Raw $programPath
    if ($program -notmatch "IFileAccessPolicy") { $failures.Add("Program.cs must register IFileAccessPolicy") }
    if ($program -notmatch "FileAccessGrantService") { $failures.Add("Program.cs must register FileAccessGrantService") }
}

$controllerPath = Need "src/Services/FileStorage/File.Storage.Web/Controllers/FilesApiController.cs"
if (Test-Path $controllerPath) {
    $controller = Get-Content -Raw $controllerPath
    if ($controller -notmatch "IFileAccessPolicy") { $failures.Add("FilesApiController must inject IFileAccessPolicy") }
    if ($controller -notmatch "AuthorizeAccess") { $failures.Add("FilesApiController must call AuthorizeAccess") }
    if ($controller -notmatch "PresignedUrlMaxLifetimeSeconds") { $failures.Add("Presigned URL must be capped by grant lifetime") }
    if ($controller -notmatch "DecisionAllowed") { $failures.Add("Audit log must record DecisionAllowed") }
}

$dbPath = Need "src/Services/FileStorage/File.Storage.Infrastructure/Data/FileStorageDbContext.cs"
if (Test-Path $dbPath) {
    $db = Get-Content -Raw $dbPath
    if ($db -notmatch "FileAccessGrants") { $failures.Add("FileStorageDbContext must expose FileAccessGrants DbSet") }
}

if ($failures.Count -gt 0) {
    Write-Host "SEC-003 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "SEC-003 validation OK."
exit 0
