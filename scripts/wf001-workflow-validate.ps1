# WF-001 Workflow Engine — Validation Script
# Verifies that all required artifacts for WF-001 are present and well-formed.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$errors = @()

Write-Host "=== WF-001 Workflow Engine Validation ===" -ForegroundColor Cyan

# Required files
$requiredFiles = @(
    "src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStatus.cs",
    "src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStepStatus.cs",
    "src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStepType.cs",
    "src/Shared/HRManagement.SharedKernel/Workflow/WorkflowDefinitionSnapshot.cs",
    "src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStepSnapshot.cs",
    "src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStepHistoryEntry.cs",
    "src/Shared/HRManagement.SharedKernel/Workflow/WorkflowCondition.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/Workflow/WorkflowDefinition.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/Workflow/WorkflowInstance.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/Workflow/WorkflowDelegation.cs",
    "src/Shared/HRManagement.ServiceDefaults/Workflow/IWorkflowEngine.cs",
    "src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowEngine.cs",
    "src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowEngineExtensions.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/WorkflowDefinitionsController.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260731161000_WF001_WorkflowEngine.cs",
    "src/Tests/HRManagement.UnitTests/Wf001WorkflowEngineTests.cs",
    "docs/ADR-WF-001-workflow-engine.md",
    "docs/WF_001_WORKFLOW_ENGINE.md"
)

Write-Host "`nChecking required files..." -ForegroundColor Yellow
foreach ($file in $requiredFiles) {
    $path = Join-Path $root $file
    if (Test-Path $path) {
        Write-Host "  [OK] $file" -ForegroundColor Green
    } else {
        Write-Host "  [MISSING] $file" -ForegroundColor Red
        $errors += "Missing: $file"
    }
}

# Check DbSets in HRCoreDbContext
Write-Host "`nChecking DbContext registrations..." -ForegroundColor Yellow
$dbContextPath = Join-Path $root "src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs"
if (Test-Path $dbContextPath) {
    $content = Get-Content $dbContextPath -Raw
    $dbSets = @("WorkflowDefinitions", "WorkflowInstances", "WorkflowDelegations")
    foreach ($ds in $dbSets) {
        if ($content -match $ds) {
            Write-Host "  [OK] DbSet<$ds> registered" -ForegroundColor Green
        } else {
            Write-Host "  [MISSING] DbSet<$ds>" -ForegroundColor Red
            $errors += "Missing DbSet: $ds"
        }
    }
}

# Check migration contains expected tables
Write-Host "`nChecking migration content..." -ForegroundColor Yellow
$migPath = Join-Path $root "src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260731161000_WF001_WorkflowEngine.cs"
if (Test-Path $migPath) {
    $migContent = Get-Content $migPath -Raw
    $tables = @("WorkflowDefinitions", "WorkflowInstances", "WorkflowDelegations")
    foreach ($tbl in $tables) {
        if ($migContent -match "name: `"$tbl`"") {
            Write-Host "  [OK] Table $tbl in migration" -ForegroundColor Green
        } else {
            Write-Host "  [MISSING] Table $tbl in migration" -ForegroundColor Red
            $errors += "Missing table in migration: $tbl"
        }
    }
}

# Check bridge method
Write-Host "`nChecking bridge method..." -ForegroundColor Yellow
$svcPath = Join-Path $root "src/Services/HRCore/HR.Core.Domain/Services/ApprovalWorkflowService.cs"
if (Test-Path $svcPath) {
    $svcContent = Get-Content $svcPath -Raw
    if ($svcContent -match "CreateViaEngineOrLegacy") {
        Write-Host "  [OK] Bridge method CreateViaEngineOrLegacy present" -ForegroundColor Green
    } else {
        Write-Host "  [MISSING] Bridge method" -ForegroundColor Red
        $errors += "Missing bridge method CreateViaEngineOrLegacy"
    }
}

# Summary
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
if ($errors.Count -eq 0) {
    Write-Host "All WF-001 validations passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "$($errors.Count) validation(s) failed:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
