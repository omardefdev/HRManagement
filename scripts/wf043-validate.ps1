#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-43: a text inventory of workflow governance. No service account acts on a step, the old step-role key is gone, a registered
  type starts only in ExternalApprovalService, and the enforce allow-list is empty. ASCII only, Windows PowerShell 5.1 and
  PowerShell 7. Exit 1 when a list is not empty.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/wf043-validate.ps1
#>
param([string]$RepoRoot = "")

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
Set-Location $RepoRoot
$problems = New-Object System.Collections.Generic.List[string]

function Get-TrackedFiles([string[]]$Paths) {
    return @(& git ls-files -- @Paths | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) })
}

function Read-Repo([string]$Path) {
    return [System.IO.File]::ReadAllText((Join-Path $RepoRoot $Path))
}

function Get-PolicyBody([string]$Text, [string]$Policy) {
    $start = $Text.IndexOf("AddPolicy(HrManagementPolicies.$Policy,")
    if ($start -lt 0) { return "" }
    $end = $Text.IndexOf("options.AddPolicy(", $start + 10)
    if ($end -lt 0) { $end = $Text.Length }
    return $Text.Substring($start, $end - $start)
}

$sources = @(Get-TrackedFiles @("src/*.cs") | Where-Object { $_ -notlike "src/Tests/*" -and $_ -notlike "*/Migrations/*" })

# 1. No service account acts on a step: both step policies refuse one, the routes use them, the evaluator refuses too.
$authorization = Read-Repo "src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs"
foreach ($policy in @("WorkflowStepAction", "HrCoreBranchOperation", "WorkflowDefinitionAdmin")) {
    $body = Get-PolicyBody $authorization $policy
    if ($body -eq "") { $problems.Add("policy $policy is missing"); continue }
    if ($body -notmatch '!ServiceCallerPrincipal\.IsServiceAccount\(c\.User\)') { $problems.Add("policy $policy does not refuse a service account") }
    if ($body -match 'HasScope\(') { $problems.Add("policy $policy admits a service scope") }
}
$instances = Read-Repo "src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs"
if (([regex]::Matches($instances, 'HrManagementPolicies\.WorkflowStepAction')).Count -ne 2) { $problems.Add("approve and reject do not both use WorkflowStepAction") }
if ($instances -match '\bStart\(') { $problems.Add("WorkflowInstancesController still has a Start action") }
$evaluator = Read-Repo "src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/WorkflowApprovalEvaluator.cs"
if ($evaluator -notmatch 'actor\.IsServiceAccount') { $problems.Add("the evaluator does not refuse a service account") }

# 2. Definitions: every write carries the admin policy and goes through the lifecycle service.
$definitions = Read-Repo "src/Services/HRCore/HR.Core.Web/Controllers/WorkflowDefinitionsController.cs"
$writes = ([regex]::Matches($definitions, '\[Http(Post|Put|Delete)')).Count
if (([regex]::Matches($definitions, '\[ValidateAntiForgeryToken\]')).Count -ne $writes) { $problems.Add("a definition write has no antiforgery attribute") }
if (([regex]::Matches($definitions, 'Policy = HrManagementPolicies\.WorkflowDefinitionAdmin')).Count -lt $writes) { $problems.Add("a definition write has no admin policy") }
if ($definitions -match 'SaveChanges') { $problems.Add("WorkflowDefinitionsController writes without the lifecycle service") }

# 3. The old key is gone, outside the line that warns about it.
foreach ($file in (Get-TrackedFiles @("src", "docker-compose.acceptance-prodlike.yml", "docker-compose.production.yml", "deploy") | Where-Object { $_ -notlike "src/Tests/*" -and $_ -notlike "*/bin/*" })) {
    if ($file -like "*WorkflowStageGuard.cs") { continue }
    if ($file -notmatch '\.(cs|json|yml|yaml)$') { continue }
    if ((Read-Repo $file) -match 'EnforceStepRoleEntityTypes') { $problems.Add("$file still names EnforceStepRoleEntityTypes") }
}

# 4. A registered type starts only in ExternalApprovalService.
foreach ($file in $sources) {
    if ($file -like "*/External/ExternalApprovalService.cs") { continue }
    $text = Read-Repo $file
    if ($text -match 'StartByWorkflowCodeAsync\(\s*"(ASSET-ASSIGN-APPROVAL|ASSET-LOSS-APPROVAL)"') { $problems.Add("$file starts a custody workflow by code") }
    if ($text -match 'EntityType\s*=\s*"(AssetAssignment|AssetLossReport)"' -and $text -match 'new WorkflowInstance\b') { $problems.Add("$file creates a custody instance") }
}
$orchestrator = Read-Repo "src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs"
if ($orchestrator -notmatch 'ExternalApprovalTypes\.IsRegistered\(request\.SubjectType\)') { $problems.Add("PolicyOrchestrator can start a registered type") }

# 5. The allow-list is empty.
$allowlist = Read-Repo "src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyWorkflowTriggerAllowlist.cs"
if ($allowlist -notmatch 'WorkflowCode\)> Allowed =\s*\[\];') { $problems.Add("PolicyWorkflowTriggerAllowlist is not empty") }

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host "FAIL: $problem" }
    exit 1
}

Write-Host "wf043-validate: PASS"
exit 0
