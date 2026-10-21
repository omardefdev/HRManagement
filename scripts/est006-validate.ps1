# EST-006: validate establishment job catalog bulk import artifacts.
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
    "docs/acceptance-plans/PLAN-19-establishment-job-catalog-import.md",
    "docs/acceptance-plans/execution/PLAN-19-EXECUTION.md",
    "scripts/est006-validate.ps1",
    "scripts/acceptance-establishment-job-catalog-import.ps1",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/FileJobCatalogPort.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/JobCatalogImportService.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/JobCatalogImportBatch.cs",
    "src/Services/HRCore/HR.Core.Web/Views/JobCatalog/Import.cshtml",
    "testdata/acceptance/establishment/job-catalog-sample.csv",
    "src/Tests/HRManagement.UnitTests/Est006EstablishmentJobCatalogImportTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Est006EstablishmentJobCatalogImportIntegrationTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst15JobCatalogBulkImportTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst16JobCatalogImportValidationTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst17JobCatalogMigrationLegacyTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

Write-Host "Running Est006 unit tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter Est006 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Est006 unit tests failed" }

Write-Host "Running Est006 integration tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj") --filter Est006 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Est006 integration tests failed" }

if ($failures.Count -gt 0) {
    Write-Error ("est006-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "est006-validate: PASS"
