# PLAN-51 (IAM-E): static checks of the explicit policies. ASCII only; Windows PowerShell 5.1.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/iam051-validate.ps1 [-SelfTest]
#
# What it checks (HRManagement.ArchitectureTests, GatewayEssRoutingTests, Iam051* and AC-IAM-51 prove the behaviour; this script guards the
# files a reviewer or an operator edits by hand):
#   1. The gateway matches self-service by exact segment: no "/Ess" substring rule, no TryGetEssPolicy, the 404 decision is recorded.
#   2. The module filter holds no name list and applies a write policy.
#   3. The ceiling lists: only Payroll's write list (rows signed PLAN-53), no sensitive row outside Payroll, no entity binding of
#      HR.Movements, entity rows signed IAM-F, and the old HR.Core list is gone.
#   4. hr-core.workflow is accepted by the three endpoints (Workflow.ExternalStart, Service.CodeGenerate, Service.PolicyEnforce) and the
#      reads that section 5 rule 1 keeps, never by HrCore.Write.
#   5. No apply-file: the controller and the gateway table name the apply route only, under Platform.AgencyOnboarding.
#   6. The gateway's audit pages carry Gateway.Audit, whose default roles leave hr-admin out.
#   7. D-IAM-4: hr-admin is excluded from Payroll.Post, Payroll.Reverse and HrHealth.Clinical in Production, Staging and the prod-like
#      mirror; the demo compose keeps Deployment__Mode=demo (the exclusion is off there in code).
#   8. AC-IAM-51 is a required prod-like class of the main group, owned by PLAN-51.
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

function Get-Json([string]$text) {
    if ($null -eq $text) { return $null }
    return ($text | ConvertFrom-Json)
}

function Test-Gateway([string]$middleware) {
    $problems = @()
    if ($null -eq $middleware) { return @("GatewayPathAuthorizationMiddleware.cs is missing") }
    if ($middleware -match 'Contains\("/Ess"' -or $middleware -match '"/Ess"') { $problems += "the gateway still matches /Ess by substring" }
    if ($middleware -match 'TryGetEssPolicy') { $problems += "TryGetEssPolicy is back" }
    if ($middleware -notmatch 'GatewayEssRouting\.Decide') { $problems += "the gateway does not decide self-service by segment" }
    if ($middleware -notmatch 'NotExposedDecision') { $problems += "the 404 of the ess segment is not recorded in the audit" }
    return $problems
}

function Test-Filter([string]$filter) {
    if ($null -eq $filter) { return @("HrModuleAuthorizationFilter.cs is missing") }
    $problems = @()
    if ($filter -match 'RouteData\.Values\["controller"\]' -or $filter -match 'StartsWith\("Ess"') { $problems += "the module filter holds a name list again" }
    if ($filter -notmatch 'WritePolicy') { $problems += "the module filter applies no write policy" }
    return $problems
}

# $files: published path (relative to tests/architecture) -> text
function Test-Lists([hashtable]$files) {
    $problems = @()
    foreach ($name in $files.Keys) {
        $rows = @($files[$name] -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" -and -not $_.StartsWith("#") })
        if ($name -eq "hrcore-post-policy-allowlist.txt") { $problems += "the old HR.Core list is back"; continue }
        if ($name -like "write-policy-allowlist/*") {
            if ($name -ne "write-policy-allowlist/payroll.txt") { $problems += "$name exists: only Payroll keeps a write-policy list (PLAN-53)"; continue }
            foreach ($row in $rows) { if ($row -notmatch '\|\s*PLAN-53:') { $problems += "${name}: row not signed by PLAN-53: $row" } }
        }
        if ($name -eq "sensitive-actions-allowlist.txt") {
            foreach ($row in $rows) { if ($row -notmatch '^payroll:' -or $row -notmatch '\|\s*PLAN-53:') { $problems += "sensitive list: a row PLAN-53 does not own: $row" } }
        }
        if ($name -like "entity-binding-allowlist/*") {
            if ($name -eq "entity-binding-allowlist/hr-movements.txt") { $problems += "HR.Movements binds an entity again (PLAN-51 replaced them with view models)"; continue }
            foreach ($row in $rows) { if ($row -notmatch '\|\s*IAM-F:') { $problems += "${name}: row not signed by IAM-F: $row" } }
        }
    }
    return $problems
}

function Get-ListFiles {
    $files = @{}
    $base = Join-Path $root "tests/architecture"
    foreach ($file in Get-ChildItem -Path $base -Recurse -File -Filter *.txt) {
        $relative = $file.FullName.Substring($base.Length + 1).Replace('\', '/')
        if ($relative -like "write-policy-allowlist/*" -or $relative -like "entity-binding-allowlist/*" -or $relative -eq "sensitive-actions-allowlist.txt" -or $relative -eq "hrcore-post-policy-allowlist.txt") {
            $files[$relative] = [System.IO.File]::ReadAllText($file.FullName)
        }
    }
    return $files
}

$allowedWorkflowPolicies = @("ModuleHrCore", "HrCoreReadOnly", "WorkflowDefinitionRead", "WorkflowExternalStart", "CodeGenerate", "PolicyEnforceS2S")

function Test-WorkflowScope([string]$authorization, [string]$services) {
    if ($null -eq $authorization -or $null -eq $services) { return @("an authorization file is missing") }
    $problems = @()
    $found = @()
    foreach ($chunk in (($authorization + "`n" + $services) -split 'options\.')) {
        if ($chunk -notmatch 'ServiceScopes\.HrCoreWorkflow') { continue }
        $name = [regex]::Match($chunk, '(?:HrManagementPolicies|ServicePolicies)\.(\w+)').Groups[1].Value
        if ($allowedWorkflowPolicies -notcontains $name) { $problems += "hr-core.workflow is accepted by $name" } else { $found += $name }
    }
    foreach ($required in @("WorkflowExternalStart", "CodeGenerate", "PolicyEnforceS2S")) {
        if ($found -notcontains $required) { $problems += "hr-core.workflow is not accepted by $required" }
    }
    if ($authorization -notmatch 'RolesPolicy\(HrManagementPolicies\.HrCoreWrite, HrManagementRoles\.Admin, HrManagementRoles\.HrCore\)') {
        $problems += "HrCore.Write is not the roles policy of hr-admin and hr-core"
    }
    return $problems
}

function Remove-Comments([string]$text) {
    if ($null -eq $text) { return $null }
    return (($text -split "`n") | Where-Object { -not $_.TrimStart().StartsWith("//") }) -join "`n"
}

function Test-Onboarding([string]$controller, [string]$table) {
    if ($null -eq $controller -or $null -eq $table) { return @("OnboardingController.cs or GatewayHrCoreActionPolicies.cs is missing") }
    $controller = Remove-Comments $controller
    $table = Remove-Comments $table
    $problems = @()
    if ($controller -match 'apply-file' -or $controller -match 'ApplyFromFile' -or $controller -match 'LoadFromFile') { $problems += "the onboarding controller reads a file again (apply-file)" }
    if ($controller -notmatch 'HrManagementPolicies\.PlatformAgencyOnboarding') { $problems += "the onboarding controller does not name Platform.AgencyOnboarding" }
    if ($table -match 'apply-file') { $problems += "the gateway table names apply-file" }
    if ($table -notmatch 'new\("api/onboarding/agencies/apply", HrManagementPolicies\.PlatformAgencyOnboarding') { $problems += "the gateway table does not give onboarding Platform.AgencyOnboarding" }
    return $problems
}

function Test-Audit([string]$homeController, [string]$authorization) {
    if ($null -eq $homeController) { return @("HomeController.cs is missing") }
    $problems = @()
    $count = ([regex]::Matches($homeController, '\[Authorize\(Policy = HrManagementPolicies\.GatewayAudit\)\]')).Count
    if ($count -lt 3) { $problems += "Gateway.Audit is on $count of the three audit pages" }
    $line = ($authorization -split "`n" | Where-Object { $_ -match 'RolesPolicyAt\(HrManagementPolicies\.GatewayAudit' }) -join ""
    $next = ($authorization -split "`n") | Select-String -Pattern 'RolesPolicyAt\(HrManagementPolicies\.GatewayAudit' -Context 0, 1 | ForEach-Object { $_.Context.PostContext } | Select-Object -First 1
    $declaration = $line + $next
    if ($declaration -eq "") { $problems += "Gateway.Audit is not read from Gateway:Audit:Roles" }
    elseif ($declaration -match 'HrManagementRoles\.Admin\b') { $problems += "Gateway.Audit admits hr-admin by default" }
    return $problems
}

function Test-AdminExclusion($payrollProd, $payrollStaging, $healthProd, $healthStaging, [string]$prodLike, [string]$demo) {
    $problems = @()
    foreach ($pair in @(@("Payroll Production", $payrollProd, @("Payroll.Post", "Payroll.Reverse")), @("Payroll Staging", $payrollStaging, @("Payroll.Post", "Payroll.Reverse")),
                        @("HR.Health Production", $healthProd, @("HrHealth.Clinical")), @("HR.Health Staging", $healthStaging, @("HrHealth.Clinical")))) {
        $listed = @()
        if ($null -ne $pair[1] -and $null -ne $pair[1].Authorization) { $listed = @($pair[1].Authorization.AdminExcludedPolicies) }
        foreach ($policy in $pair[2]) { if ($listed -notcontains $policy) { $problems += "$($pair[0]): $policy is not in Authorization:AdminExcludedPolicies" } }
    }
    foreach ($policy in @("Payroll.Post", "Payroll.Reverse", "HrHealth.Clinical")) {
        if ($null -eq $prodLike -or $prodLike -notmatch "Authorization__AdminExcludedPolicies__\d+: `"$([regex]::Escape($policy))`"") { $problems += "the prod-like mirror does not exclude hr-admin from $policy" }
    }
    if ($null -eq $demo -or $demo -notmatch 'Deployment__Mode: demo') { $problems += "the demo compose no longer sets Deployment__Mode=demo" }
    return $problems
}

function Test-Acceptance($scope, $ownership) {
    $problems = @()
    $row = @($scope.prodLike | Where-Object { $_.class -eq "AcIam51ExplicitPolicyTests" })
    if ($row.Count -ne 1 -or $row[0].group -ne "main" -or -not $row[0].required) { $problems += "AcIam51ExplicitPolicyTests is not a required prod-like class of the main group" }
    $owner = $ownership.PSObject.Properties | Where-Object { $_.Name -eq "classes" -or $_.Name -eq "acs" -or $_.Name -eq "entries" } | Select-Object -First 1
    $entries = if ($null -ne $owner) { $owner.Value } else { $ownership }
    $ac = $entries.PSObject.Properties | Where-Object { $_.Name -eq "AC-IAM-51" } | Select-Object -First 1
    if ($null -eq $ac -or $ac.Value.ownerPlan -ne "PLAN-51" -or -not $ac.Value.provesClaim) { $problems += "AC-IAM-51 is not owned by PLAN-51 with provesClaim" }
    return $problems
}

$paths = @{
    Middleware = "src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs"
    Filter = "src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs"
    Authorization = "src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs"
    Services = "src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs"
    Onboarding = "src/Services/HRCore/HR.Core.Web/Controllers/OnboardingController.cs"
    Table = "src/Shared/HRManagement.ServiceDefaults/GatewayHrCoreActionPolicies.cs"
    Home = "src/Gateway/HRManagement.Gateway/Controllers/HomeController.cs"
    PayrollProd = "src/Services/Payroll/Payroll.Web/appsettings.Production.json"
    PayrollStaging = "src/Services/Payroll/Payroll.Web/appsettings.Staging.json"
    HealthProd = "src/Services/HRHealth/HR.Health.Web/appsettings.Production.json"
    HealthStaging = "src/Services/HRHealth/HR.Health.Web/appsettings.Staging.json"
    ProdLike = "docker-compose.acceptance-prodlike.yml"
    Demo = "docker-compose.vps-demo.yml"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
}

function Invoke-All {
    $all = @()
    $all += Test-Gateway (Read-Text $paths.Middleware)
    $all += Test-Filter (Read-Text $paths.Filter)
    $all += Test-Lists (Get-ListFiles)
    $all += Test-WorkflowScope (Read-Text $paths.Authorization) (Read-Text $paths.Services)
    $all += Test-Onboarding (Read-Text $paths.Onboarding) (Read-Text $paths.Table)
    $all += Test-Audit (Read-Text $paths.Home) (Read-Text $paths.Authorization)
    $all += Test-AdminExclusion (Get-Json (Read-Text $paths.PayrollProd)) (Get-Json (Read-Text $paths.PayrollStaging)) `
        (Get-Json (Read-Text $paths.HealthProd)) (Get-Json (Read-Text $paths.HealthStaging)) (Read-Text $paths.ProdLike) (Read-Text $paths.Demo)
    $all += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    return @($all | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $middleware = Read-Text $paths.Middleware
    if ((Test-Gateway ($middleware + "`nif (p.Contains(`"/Ess`", StringComparison.OrdinalIgnoreCase)) { }")).Count -eq 0) { $failures += "a substring /Ess rule was not refused" }
    $filter = Read-Text $paths.Filter
    if ((Test-Filter ($filter + "`nif (controller.StartsWith(`"Ess`", StringComparison.OrdinalIgnoreCase)) return;")).Count -eq 0) { $failures += "a name list in the filter was not refused" }
    $lists = Get-ListFiles
    $planted = $lists.Clone(); $planted["write-policy-allowlist/hr-core.txt"] = "Shifts.Create | PLAN-53: planted"
    if ((Test-Lists $planted).Count -eq 0) { $failures += "a write list outside Payroll was not refused" }
    $unsigned = $lists.Clone(); $unsigned["write-policy-allowlist/payroll.txt"] = "PayrollRuns.Create | to save time"
    if ((Test-Lists $unsigned).Count -eq 0) { $failures += "an unsigned Payroll row was not refused" }
    $authorization = Read-Text $paths.Authorization
    $services = Read-Text $paths.Services
    $wide = $authorization.Replace("options.RolesPolicy(HrManagementPolicies.HrCoreWrite, HrManagementRoles.Admin, HrManagementRoles.HrCore);",
        "options.AddPolicy(HrManagementPolicies.HrCoreWrite, p => p.RequireAssertion(c => ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreWorkflow)));")
    if ((Test-WorkflowScope $wide $services).Count -eq 0) { $failures += "HrCore.Write with the workflow scope was not refused" }
    $onboarding = Read-Text $paths.Onboarding
    if ((Test-Onboarding ($onboarding + "`n[HttpPost(`"apply-file`")]") (Read-Text $paths.Table)).Count -eq 0) { $failures += "apply-file was not refused" }
    $homeController = Read-Text $paths.Home
    if ((Test-Audit ($homeController.Replace("[Authorize(Policy = HrManagementPolicies.GatewayAudit)]", "")) $authorization).Count -eq 0) { $failures += "audit pages without Gateway.Audit were not refused" }
    $payrollProd = Get-Json (Read-Text $paths.PayrollProd)
    $payrollProd.Authorization.AdminExcludedPolicies = @("Payroll.Post")
    if ((Test-AdminExclusion $payrollProd (Get-Json (Read-Text $paths.PayrollStaging)) (Get-Json (Read-Text $paths.HealthProd)) `
            (Get-Json (Read-Text $paths.HealthStaging)) (Read-Text $paths.ProdLike) (Read-Text $paths.Demo)).Count -eq 0) { $failures += "a Production file without Payroll.Reverse excluded was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $scope.prodLike = @($scope.prodLike | Where-Object { $_.class -ne "AcIam51ExplicitPolicyTests" })
    if ((Test-Acceptance $scope (Get-Json (Read-Text $paths.Ownership))).Count -eq 0) { $failures += "a scope without AC-IAM-51 was not refused" }
    if ((Invoke-All).Count -ne 0) { $failures += "the repository itself does not pass" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] iam051-validate self-test: 9 planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] iam051-validate: self-service by segment, no name list, ceiling lists, hr-core.workflow, onboarding, audit, D-IAM-4, AC-IAM-51" -ForegroundColor Green
exit 0
