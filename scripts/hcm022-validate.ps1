# HCM-022: validate recruitment workflow UI artifacts (PLAN-22).
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
    "docs/acceptance-plans/PLAN-22-hcm-recruitment-workflows-ui.md",
    "docs/acceptance-plans/execution/PLAN-22-EXECUTION.md",
    "scripts/hcm022-validate.ps1",
    "src/Services/HRCore/HR.Core.Web/Services/WorkflowPanelBuilder.cs",
    "src/Services/HRCore/HR.Core.Web/Services/OfferLetterWorkflowUiService.cs",
    "src/Services/HRCore/HR.Core.Web/Services/SelectionCommitteeWorkflowUiService.cs",
    "src/Services/HRCore/HR.Core.Web/Services/RecruitmentAppealWorkflowUiService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeWorkflowHandler.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealWorkflowHandler.cs",
    "src/Tests/HRManagement.UnitTests/Hcm022RecruitmentWorkflowsUiTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm22RecruitmentWorkflowsUiTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

Write-Host "Running Hcm022 unit tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter Hcm022 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm022 unit tests failed" }

Write-Host "Running Hcm021 regression..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter Hcm021 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm021 regression failed" }

if ($failures.Count -gt 0) {
    Write-Error ("hcm022-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm022-validate: PASS"
