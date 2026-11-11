# PLAN-50 (IAM-A): static checks of the approver's reach and of approval delegation. ASCII only; Windows PowerShell 5.1.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/iam050-validate.ps1 [-SelfTest]
#
# What it checks (the unit tests GatewayApproverReachTests, Iam050* and the wiring tests prove the behaviour; this script guards the
# files a reviewer or an operator edits by hand):
#   1. The gateway table names every approval action of HR.Core with its policy, and the movement approval paths are matched by segment.
#   2. The five movement controllers name their entity type, and their Approve keeps HrManagement.Approve on top of the override.
#   3. The configuration lists are valid in HR.Core, HR.Movements and the gateway: DelegableRoles is exactly hr-management-approver,
#      CacheSeconds is 0..300, every EntityTypes entry of HR.Movements is one of HR.Core's, NonStaffRoles holds the three non-staff roles.
#   4. The internal route is service-only: its controller carries Service.ApprovalDelegationRead on the class and the action, and the
#      policy admits a service account with hr-core.lookup from svc-hr-movements only; the wiring matrix lists the endpoint and the call.
#   5. One delegation entity: WorkflowDelegation is gone; HCM050 lifts FORCE and restores it; neither PLAN-50 migration has a BOM.
# -SelfTest runs each check on a planted defect and fails unless every one of them is refused.
param([switch]$SelfTest)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }

function Read-Text([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path)) { return $null }
    return [System.IO.File]::ReadAllText($path)
}

function Test-GatewayTable([string]$text) {
    $problems = @()
    if ($null -eq $text) { return @("GatewayHrCoreActionPolicies.cs is missing") }
    $rows = @(
        'new("EmploymentRecords", "Approve", HrManagementPolicies.HrCoreApprove',
        'new("EmployeeCorrections", "Approve", HrManagementPolicies.HrCoreApprove',
        'new("Competitions", "Finalize", HrManagementPolicies.HrCoreApprove',
        'new("Competitions", "Cancel", HrManagementPolicies.HrCoreApprove',
        'new("Approvals", "Approve", HrManagementPolicies.HrCoreApprove',
        'new("Approvals", "Reject", HrManagementPolicies.HrCoreApprove',
        'new("Delegations", "Create", HrManagementPolicies.ManageDelegations',
        'new("Delegations", "Revoke", HrManagementPolicies.ManageDelegations',
        'new("PolicyTemplates", "PublishFromUi", HrManagementPolicies.PolicyApprover',
        'new("api/employee-corrections/{guid}/approve", HrManagementPolicies.HrCoreApprove',
        'new("api/onboarding/agencies/apply", HrManagementPolicies.HrCoreApprove',
        'new("api/onboarding/agencies/apply-file", HrManagementPolicies.HrCoreApprove',
        'new("api/policies/templates/{guid}/publish", HrManagementPolicies.PolicyApprover'
    )
    foreach ($row in $rows) { if (-not $text.Contains($row)) { $problems += "gateway table: missing row $row" } }
    return $problems
}

function Test-MovementController([string]$text, [string]$entityType) {
    $problems = @()
    if ($null -eq $text) { return @("$entityType controller source is missing") }
    if (-not $text.Contains("[ApprovalEntityType(")) { $problems += "$entityType`: no [ApprovalEntityType] on the controller" }
    if (-not $text.Contains($entityType)) { $problems += "$entityType`: entity type not named" }
    $approve = [regex]::Matches($text, '\[Authorize\(Policy = HrManagementPolicies\.HrManagementApprove\)\]\s*\[ModulePolicyOverride\(HrManagementPolicies\.HrMovementsApprovalAccess\)\]\s*public async Task<IActionResult> Approve\(')
    if ($approve.Count -lt 1) { $problems += "$entityType`: Approve does not keep HrManagement.Approve on top of the override" }
    return $problems
}

function Get-Json([string]$text) {
    if ($null -eq $text) { return $null }
    return ($text.TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

function Test-Configuration($core, $movements, $gateway) {
    $problems = @()
    foreach ($pair in @(@("hr-core", $core), @("hr-movements", $movements))) {
        $name = $pair[0]; $json = $pair[1]
        if ($null -eq $json -or $null -eq $json.Delegation) { $problems += "$name`: no Delegation section"; continue }
        $roles = @($json.Delegation.DelegableRoles)
        if ($roles.Count -ne 1 -or $roles[0] -ne "hr-management-approver") { $problems += "$name`: DelegableRoles must be exactly hr-management-approver (D-50-1)" }
        $cache = [int]$json.Delegation.CacheSeconds
        if ($cache -lt 0 -or $cache -gt 300) { $problems += "$name`: CacheSeconds must be 0..300" }
    }
    if ($null -ne $core -and $null -ne $movements -and $null -ne $core.Delegation -and $null -ne $movements.Delegation) {
        $known = @($core.Delegation.EntityTypes)
        foreach ($type in @($movements.Delegation.EntityTypes)) {
            if ($known -notcontains $type) { $problems += "hr-movements: entity type $type is not in HR.Core's Delegation:EntityTypes" }
        }
    }
    foreach ($pair in @(@("hr-core", $core), @("hr-movements", $movements), @("gateway", $gateway))) {
        $name = $pair[0]; $json = $pair[1]
        $list = @()
        if ($null -ne $json -and $null -ne $json.Authorization) { $list = @($json.Authorization.NonStaffRoles) }
        foreach ($role in @("hr-employee", "hr-exam-marker", "applicant")) {
            if ($list -notcontains $role) { $problems += "$name`: Authorization:NonStaffRoles lacks $role" }
        }
    }
    return $problems
}

function Test-InternalRoute([string]$controller, [string]$policies, $wiring) {
    $problems = @()
    if ($null -eq $controller) { return @("ApprovalDelegationsInternalController.cs is missing") }
    if (-not $controller.Contains('[Route("api/internal/approval-delegations")]')) { $problems += "internal route: wrong route" }
    if (([regex]::Matches($controller, '\[Authorize\(Policy = ServicePolicies\.ApprovalDelegationRead\)\]')).Count -lt 2) {
        $problems += "internal route: Service.ApprovalDelegationRead must sit on the class and the action"
    }
    if ($controller.Contains("AllowAnonymous")) { $problems += "internal route: AllowAnonymous" }
    $registration = [regex]::Match($policies, 'ServicePolicies\.ApprovalDelegationRead[\s\S]{0,400}?\)\)\);')
    if (-not $registration.Success) { $problems += "internal route: the policy is not registered" }
    else {
        $body = $registration.Value
        foreach ($needle in @("IsServiceAccount", "ServiceScopes.HrCoreLookup", "ServiceClientIds.HrMovements")) {
            if (-not $body.Contains($needle)) { $problems += "internal route: the policy does not require $needle" }
        }
    }
    if ($null -eq $wiring) { $problems += "service-wiring.json is missing" }
    else {
        $endpoint = $wiring.endpoints.'hr-core.approval-delegations.active'
        if ($null -eq $endpoint) { $problems += "wiring: no endpoint hr-core.approval-delegations.active" }
        elseif (@($endpoint.serverPolicies) -notcontains "ServicePolicies.ApprovalDelegationRead") { $problems += "wiring: the endpoint's policy is wrong" }
        $movements = @($wiring.services | Where-Object { $_.name -eq "hr-movements" })
        if ($movements.Count -ne 1 -or @($movements[0].calls | Where-Object { $_.endpoint -eq "hr-core.approval-delegations.active" }).Count -ne 1) {
            $problems += "wiring: hr-movements does not call hr-core.approval-delegations.active"
        }
    }
    return $problems
}

function Test-OneEntity([bool]$workflowDelegationExists, [string]$hcm050, [byte[]]$hcm050Bytes, [byte[]]$mov050Bytes) {
    $problems = @()
    if ($workflowDelegationExists) { $problems += "WorkflowDelegation.cs is back (one delegation entity)" }
    if ($null -eq $hcm050) { return $problems + "HCM050 migration is missing" }
    if (-not $hcm050.Contains('NO FORCE ROW LEVEL SECURITY') -or -not $hcm050.Contains('"ApprovalDelegations" FORCE ROW LEVEL SECURITY')) {
        $problems += "HCM050 must lift FORCE and restore it around the revocation"
    }
    if (-not $hcm050.Contains("PLAN-50 migration")) { $problems += "HCM050 does not record the revocation reason" }
    foreach ($pair in @(@("HCM050", $hcm050Bytes), @("MOV050", $mov050Bytes))) {
        $bytes = $pair[1]
        if ($null -eq $bytes) { $problems += "$($pair[0]) migration is missing"; continue }
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $problems += "$($pair[0]) migration has a BOM" }
    }
    return $problems
}

function Get-Bytes([string]$pattern) {
    $file = Get-ChildItem -Path (Join-Path $root (Split-Path $pattern -Parent)) -Filter (Split-Path $pattern -Leaf) -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "*.Designer.cs" } | Select-Object -First 1
    if ($null -eq $file) { return $null }
    return [System.IO.File]::ReadAllBytes($file.FullName)
}

$movementControllers = @{
    "TransferRequest" = "src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs"
}
$extended = Read-Text "src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs"

function Invoke-All {
    $all = @()
    $all += Test-GatewayTable (Read-Text "src/Shared/HRManagement.ServiceDefaults/GatewayHrCoreActionPolicies.cs")
    $all += Test-MovementController (Read-Text $movementControllers["TransferRequest"]) "TransferRequest"
    foreach ($type in @("SecondmentRequest", "LoanAssignmentRequest", "PromotionRequest", "DelegationRequest")) {
        $start = $extended.IndexOf("[ApprovalEntityType(`"$type`")]")
        if ($start -lt 0) { $all += "$type`: no [ApprovalEntityType] on the controller"; continue }
        $next = $extended.IndexOf("[ApprovalEntityType(", $start + 10)
        $block = if ($next -lt 0) { $extended.Substring($start) } else { $extended.Substring($start, $next - $start) }
        $all += Test-MovementController $block $type
    }
    $all += Test-Configuration (Get-Json (Read-Text "src/Services/HRCore/HR.Core.Web/appsettings.json")) `
        (Get-Json (Read-Text "src/Services/HRMovements/HR.Movements.Web/appsettings.json")) `
        (Get-Json (Read-Text "src/Gateway/HRManagement.Gateway/appsettings.json"))
    $all += Test-InternalRoute (Read-Text "src/Services/HRCore/HR.Core.Web/Controllers/ApprovalDelegationsInternalController.cs") `
        (Read-Text "src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs") `
        (Get-Json (Read-Text "deploy/wiring/service-wiring.json"))
    $hcmBytes = Get-Bytes "src/Services/HRCore/HR.Core.Infrastructure/Migrations/*_HCM050_ApprovalDelegationScope.cs"
    $hcmText = if ($null -eq $hcmBytes) { $null } else { [System.Text.Encoding]::UTF8.GetString($hcmBytes) }
    $all += Test-OneEntity (Test-Path (Join-Path $root "src/Services/HRCore/HR.Core.Domain/Entities/Workflow/WorkflowDelegation.cs")) `
        $hcmText $hcmBytes (Get-Bytes "src/Services/HRMovements/HR.Movements.Infrastructure/Migrations/*_MOV050_ApprovalOnBehalfOf.cs")
    return @($all | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $good = Read-Text "src/Shared/HRManagement.ServiceDefaults/GatewayHrCoreActionPolicies.cs"
    if ((Test-GatewayTable ($good.Replace('new("Delegations", "Revoke"', 'new("Delegations", "RevokeX"'))).Count -eq 0) { $failures += "a missing table row was not refused" }
    $transfer = Read-Text $movementControllers["TransferRequest"]
    if ((Test-MovementController ($transfer.Replace("[ApprovalEntityType(", "[XApprovalEntityType(")) "TransferRequest").Count -eq 0) { $failures += "a missing entity type was not refused" }
    $core = Get-Json (Read-Text "src/Services/HRCore/HR.Core.Web/appsettings.json")
    $movements = Get-Json (Read-Text "src/Services/HRMovements/HR.Movements.Web/appsettings.json")
    $gateway = Get-Json (Read-Text "src/Gateway/HRManagement.Gateway/appsettings.json")
    $wide = Get-Json (Read-Text "src/Services/HRCore/HR.Core.Web/appsettings.json")
    $wide.Delegation.DelegableRoles = @("hr-management-approver", "hr-core")
    if ((Test-Configuration $wide $movements $gateway).Count -eq 0) { $failures += "a widened DelegableRoles was not refused" }
    $noStaff = Get-Json (Read-Text "src/Gateway/HRManagement.Gateway/appsettings.json")
    $noStaff.Authorization.NonStaffRoles = @("hr-employee")
    if ((Test-Configuration $core $movements $noStaff).Count -eq 0) { $failures += "a short NonStaffRoles was not refused" }
    $controller = Read-Text "src/Services/HRCore/HR.Core.Web/Controllers/ApprovalDelegationsInternalController.cs"
    $policies = Read-Text "src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs"
    $wiring = Get-Json (Read-Text "deploy/wiring/service-wiring.json")
    if ((Test-InternalRoute ($controller + "`n[AllowAnonymous]") $policies $wiring).Count -eq 0) { $failures += "an anonymous internal route was not refused" }
    if ((Test-InternalRoute $controller ($policies.Replace("IsAllowedClient(ctx.User, ServiceClientIds.HrMovements)));", "true));")) $wiring).Count -eq 0) {
        $failures += "a policy without the client list was not refused"
    }
    if ((Test-OneEntity $true "x" ([byte[]](0x2F)) ([byte[]](0x2F))).Count -eq 0) { $failures += "a WorkflowDelegation entity was not refused" }
    if ((Test-OneEntity $false "PLAN-50 migration" ([byte[]](0xEF, 0xBB, 0xBF, 0x2F)) ([byte[]](0x2F))).Count -eq 0) { $failures += "a BOM and a missing FORCE lift were not refused" }
    if ((Invoke-All).Count -ne 0) { $failures += "the repository itself does not pass" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] iam050-validate self-test: 8 planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] iam050-validate: gateway table, movement controllers, configuration, internal route, one delegation entity" -ForegroundColor Green
exit 0
