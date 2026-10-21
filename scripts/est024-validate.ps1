# EST-024 / PLAN-24: validate establishment workflow UI artifacts.
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
    "docs/acceptance-plans/PLAN-24-establishment-workflow-ui.md",
    "docs/acceptance-plans/execution/PLAN-24-EXECUTION.md",
    "scripts/est024-validate.ps1",
    "src/Services/HRCore/HR.Core.Web/Services/EstablishmentChangeWorkflowUiService.cs",
    "src/Services/HRCore/HR.Core.Web/Views/EstablishmentChanges/_EstablishmentChangeWorkflowPanel.cshtml",
    "src/Tests/HRManagement.UnitTests/Est024EstablishmentWorkflowUiTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcEst24EstablishmentWorkflowUiTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

$inboxFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs"
if (Test-Path $inboxFile) {
    $inboxContent = Get-Content -Raw -Path $inboxFile
    if ($inboxContent -notmatch "EstablishmentChangeRequest") {
        $failures += "WorkflowInboxQueryService must include EstablishmentChangeRequest"
    }
}

Write-Host "Running Est024 unit tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter Est024 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Est024 unit tests failed" }

Write-Host "Running Wf023 + Hcm021 + Hcm022 regression..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter "Wf023|Hcm021|Hcm022" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Wf023/Hcm021/Hcm022 regression failed" }

if ($failures.Count -gt 0) {
    Write-Error ("est024-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "est024-validate: PASS"
