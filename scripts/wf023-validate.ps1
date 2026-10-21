# WF-023 / PLAN-23: validate unified work inbox UI artifacts.
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
    "docs/acceptance-plans/PLAN-23-wf-work-inbox-ui.md",
    "docs/acceptance-plans/execution/PLAN-23-EXECUTION.md",
    "scripts/wf023-validate.ps1",
    "src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs",
    "src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxEligibilityService.cs",
    "src/Services/HRCore/HR.Core.Web/Services/WorkflowDeepLinkResolver.cs",
    "src/Services/HRCore/HR.Core.Web/Services/WorkInboxItemMapper.cs",
    "src/Services/HRCore/HR.Core.Web/Services/HrCoreWorkInboxService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/WorkInboxController.cs",
    "src/Services/HRCore/HR.Core.Web/Views/WorkInbox/Index.cshtml",
    "src/Tests/HRManagement.UnitTests/Wf023WorkInboxUiTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcWf23WorkInboxUiTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

$layout = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Views/Shared/_Layout.cshtml"
if (Test-Path $layout) {
    $layoutContent = Get-Content -Raw -Path $layout
    if ($layoutContent -notmatch "WorkInbox") { $failures += "_Layout must link to WorkInbox" }
    if ($layoutContent -notmatch 'asp-controller="WorkInbox"') { $failures += "_Layout must use WorkInbox controller" }
}

Write-Host "Running Wf023 unit tests..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter Wf023 --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Wf023 unit tests failed" }

Write-Host "Running Hcm021 + Hcm022 regression..."
dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") --filter "Hcm021|Hcm022" --no-restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures += "Hcm021/Hcm022 regression failed" }

if ($failures.Count -gt 0) {
    Write-Error ("wf023-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "wf023-validate: PASS"
