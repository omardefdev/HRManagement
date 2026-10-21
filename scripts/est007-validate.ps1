# EST-007: validate establishment MDM admin UI artifacts.
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
    "docs/acceptance-plans/PLAN-20-establishment-mdm-admin-ui.md",
    "docs/acceptance-plans/execution/PLAN-20-EXECUTION.md",
    "scripts/est007-validate.ps1",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/MasterData/ReferenceCatalogAdminService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/ReferenceCatalogController.cs",
    "src/Services/HRCore/HR.Core.Web/Views/ReferenceCatalog/Index.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/ReferenceCatalog/Create.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/ReferenceCatalog/Edit.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/ReferenceCatalog/Details.cshtml",
    "src/Tests/HRManagement.UnitTests/Est007ReferenceCatalogAdminTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst18ReferenceCatalogAdminTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

Write-Host "Running Est007 unit tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter Est007 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Est007 unit tests failed" }

if ($failures.Count -gt 0) {
    Write-Error ("est007-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "est007-validate: PASS"
