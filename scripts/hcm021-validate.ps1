# HCM-021: validate requisition approval UI artifacts (PLAN-21).
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
    "docs/acceptance-plans/PLAN-21-hcm-requisition-approval-ui.md",
    "docs/acceptance-plans/execution/PLAN-21-EXECUTION.md",
    "scripts/hcm021-validate.ps1",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs",
    "src/Services/HRCore/HR.Core.Web/Services/RecruitmentRequisitionWorkflowUiService.cs",
    "src/Services/HRCore/HR.Core.Web/Views/RecruitmentRequisitions/_RequisitionWorkflowPanel.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/Shared/_WorkflowProgressPanel.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/Shared/_WorkflowActionPanel.cshtml",
    "src/Tests/HRManagement.UnitTests/Hcm021RequisitionApprovalUiTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm21RequisitionApprovalUiTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

Write-Host "Running Hcm021 unit tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter Hcm021 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm021 unit tests failed" }

if ($failures.Count -gt 0) {
    Write-Error ("hcm021-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm021-validate: PASS"
