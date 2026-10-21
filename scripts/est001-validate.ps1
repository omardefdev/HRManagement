# EST-001: validate establishment position artifacts.
# Usage:
#   powershell -File scripts/est001-validate.ps1

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
    "docs/EST_001_ESTABLISHMENT.md",
    "docs/acceptance-plans/PLAN-14-establishment-ui.md",
    "scripts/est001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Est001EstablishmentTests.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/JobCatalogEntry.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/FundedPosition.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/EstablishmentService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/JobCatalogController.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/FundedPositionsController.cs",
    "src/Services/HRCore/HR.Core.Web/Services/EstablishmentScopeService.cs",
    "src/Services/HRCore/HR.Core.Web/Views/JobCatalog/Index.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/FundedPositions/Index.cshtml",
    "src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260731171000_EST001_Establishment.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Validate doc content
$docFile = Resolve-RepoPath "docs/EST_001_ESTABLISHMENT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "EST-001") { $failures += "doc missing EST-001 reference" }
    if ($doc -notmatch "FundedPosition") { $failures += "doc must mention FundedPosition" }
    if ($doc -notmatch "JobCatalogEntry") { $failures += "doc must mention JobCatalogEntry" }
    if ($doc -notmatch "EstablishmentService") { $failures += "doc must mention EstablishmentService" }
}

# Validate FundedPosition entity
$fpFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Entities/FundedPosition.cs"
if (Test-Path $fpFile) {
    $fp = Get-Content -Raw -Path $fpFile
    if ($fp -notmatch "PositionCode") { $failures += "FundedPosition must have PositionCode" }
    if ($fp -notmatch "GradeCode") { $failures += "FundedPosition must have GradeCode" }
    if ($fp -notmatch "FTE") { $failures += "FundedPosition must have FTE" }
    if ($fp -notmatch "BudgetYear") { $failures += "FundedPosition must have BudgetYear" }
    if ($fp -notmatch "OrgUnitId") { $failures += "FundedPosition must have OrgUnitId" }
    if ($fp -notmatch "Occupied|Vacant|Frozen") { $failures += "FundedPosition must have status values" }
}

# Validate EstablishmentService
$svcFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Services/EstablishmentService.cs"
if (Test-Path $svcFile) {
    $svc = Get-Content -Raw -Path $svcFile
    if ($svc -notmatch "AssignEmployee") { $failures += "EstablishmentService must have AssignEmployee" }
    if ($svc -notmatch "funded position|Frozen|Vacant") { $failures += "service must validate funded position" }
}

# Validate Lookup API + picker wiring
$lookupFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Controllers/LookupApiController.cs"
if (Test-Path $lookupFile) {
    $lookup = Get-Content -Raw -Path $lookupFile
    if ($lookup -notmatch 'job-catalog') { $failures += "LookupApiController must expose job-catalog lookup" }
}

$pickerFile = Resolve-RepoPath "src/Shared/HRManagement.Web.Shared/wwwroot/js/entity-pickers.js"
if (Test-Path $pickerFile) {
    $picker = Get-Content -Raw -Path $pickerFile
    if ($picker -notmatch 'initJobCatalogPicker') { $failures += "entity-pickers.js must define initJobCatalogPicker" }
}

# Validate DbContext has DbSets
$ctxFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs"
if (Test-Path $ctxFile) {
    $ctx = Get-Content -Raw -Path $ctxFile
    if ($ctx -notmatch "JobCatalogEntries") { $failures += "DbContext must have JobCatalogEntries DbSet" }
    if ($ctx -notmatch "FundedPositions") { $failures += "DbContext must have FundedPositions DbSet" }
}

$outDir = Resolve-RepoPath "testdata/est001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "EST-001"
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

Write-Host "PASS: EST-001 establishment positions validate"
exit 0
