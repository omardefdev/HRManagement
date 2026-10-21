# ORG-001: validate dated org structure artifacts.
# Usage:
#   powershell -File scripts/org001-validate.ps1

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
    "src/Services/HRCore/HR.Core.Domain/Entities/OrgUnitVersion.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260731170000_ORG001_DatedOrgStructure.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260824143317_ORG001_HeadOnVersion.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/OrgStructure/OrgUnitVersionService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs",
    "docs/ORG_001_DATED_STRUCTURE.md",
    "scripts/org001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Org001DatedStructureTests.cs",
    "src/Tests/HRManagement.UnitTests/OrgUnitVersionServiceTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$entityFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Entities/OrgUnitVersion.cs"
if (Test-Path $entityFile) {
    $content = Get-Content -Raw -Path $entityFile
    if ($content -notmatch "OrgUnitId") { $failures += "OrgUnitVersion missing OrgUnitId" }
    if ($content -notmatch "ValidFrom") { $failures += "OrgUnitVersion missing ValidFrom" }
    if ($content -notmatch "ValidTo") { $failures += "OrgUnitVersion missing ValidTo" }
    if ($content -notmatch "HeadEmployeeId") { $failures += "OrgUnitVersion missing HeadEmployeeId" }
    if ($content -notmatch "OrgOperationType") { $failures += "OrgUnitVersion missing OperationType" }
}

$serviceFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/OrgStructure/OrgUnitVersionService.cs"
if (Test-Path $serviceFile) {
    $svc = Get-Content -Raw -Path $serviceFile
    if ($svc -notmatch "ResolveAsOfAsync") { $failures += "OrgUnitVersionService missing ResolveAsOfAsync" }
    if ($svc -notmatch "BackfillMissingVersionsAsync") { $failures += "OrgUnitVersionService missing BackfillMissingVersionsAsync" }
}

$apiFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs"
if (Test-Path $apiFile) {
    $api = Get-Content -Raw -Path $apiFile
    if ($api -notmatch "as-of") { $failures += "OrgStructureApiController missing as-of route" }
}

$ctxFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs"
if (Test-Path $ctxFile) {
    $ctx = Get-Content -Raw -Path $ctxFile
    if ($ctx -notmatch "OrgUnitVersions") { $failures += "HRCoreDbContext missing OrgUnitVersions DbSet" }
}

$docFile = Resolve-RepoPath "docs/ORG_001_DATED_STRUCTURE.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "ORG-001") { $failures += "doc missing ORG-001 reference" }
}

$outDir = Resolve-RepoPath "testdata/org001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "ORG-001"
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

Write-Host "PASS: ORG-001 dated structure validate"
exit 0
