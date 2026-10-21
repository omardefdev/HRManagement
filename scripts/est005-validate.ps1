# EST-005: validate establishment MDM consumer artifacts.
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
    "docs/acceptance-plans/PLAN-18-establishment-mdm-consumer.md",
    "docs/acceptance-plans/execution/PLAN-18-EXECUTION.md",
    "scripts/est005-validate.ps1",
    "scripts/acceptance-establishment-mdm-consumer.ps1",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/MasterData/ReferenceDataProvider.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentReferenceValidator.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/JobCatalogCodeService.cs",
    "src/Services/HRCore/HR.Core.Web/Services/EstablishmentPayloadSummaryBuilder.cs",
    "src/Services/HRCore/HR.Core.Web/Views/EstablishmentChanges/_EstablishmentPayloadSummary.cshtml",
    "src/Tests/HRManagement.UnitTests/Est005EstablishmentMdmConsumerTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Est005EstablishmentMdmConsumerIntegrationTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst13EstablishmentMdmLookupTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst14JobCatalogCodPreviewTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

Write-Host "Running Est005 unit tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter Est005 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Est005 unit tests failed" }

Write-Host "Running Est005 integration tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj") --filter Est005 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Est005 integration tests failed" }

if ($failures.Count -gt 0) {
    Write-Error ("est005-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "est005-validate: PASS"
