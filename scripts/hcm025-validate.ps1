# HCM-025 (PLAN-25): validate that recruitment approvals work with the Production settings and are enforced on the server.
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts/hcm025-validate.ps1 [-SkipTests]
# Checks:
#   - no recruitment or establishment approval start is gated on IsLabScale (only NationalScopeOptions/Guard may name it);
#   - Production and Staging carry decision 10 (PolicyEngine Core.Recruitment=Shadow, Recruitment:Approvals:AlwaysStartWorkflow)
#     and Workflow:AdminBypass=false; the prod-like compose mirrors them;
#   - WorkflowStageGuard has the step-role check and the action service runs the evaluator on approve and reject;
#   - R-REC-HIRE is deactivated and RECRUITMENT-HIRE retired; the POST-policy allow-list exists;
#   - e2e-journeys.yml runs the prod-like journey with a zero-skip step (structural check: no YAML parser on this machine);
#   - the Hcm025 | WorkflowStageGuard | Architecture unit tests pass (the known BolaArchitectureTests baseline is reported).
# Writes artifacts/hcm025/validate-report.json (git-ignored).

param(
    [string]$RepoRoot = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

function Read-Text([string]$rel) {
    return Get-Content -Raw -Encoding UTF8 -Path (Resolve-RepoPath $rel)
}

$failures = @()
$notes = @()

# 1. IsLabScale no longer gates any approval start.
$gated = @(
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionLifecycleService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeLifecycleService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeLifecycleService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealService.cs"
)
foreach ($rel in $gated) {
    $text = Read-Text $rel
    if ($text -match 'IsLabScale') { $failures += "${rel}: still references IsLabScale" }
    if ($rel -notlike "*AppealService.cs" -and $text -notmatch 'approvalStart\.StartAndApplyAsync') {
        $failures += "${rel}: the submit does not go through ApprovalStartCoordinator"
    }
}
$hrCore = Get-ChildItem -Path (Resolve-RepoPath "src/Services/HRCore") -Recurse -Include "*.cs", "*.cshtml" -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($file in $hrCore) {
    if ((Get-Content -Raw -Encoding UTF8 -Path $file.FullName) -match 'IsLabScale') {
        $failures += "$($file.FullName.Substring($RepoRoot.Length + 1)): names IsLabScale (only NationalScopeOptions/Guard may)"
    }
}

# 2. Settings per environment.
foreach ($envName in @("Production", "Staging")) {
    $json = Read-Text "src/Services/HRCore/HR.Core.Web/appsettings.$envName.json" | ConvertFrom-Json
    if ($json.PolicyEngine.'Core.Recruitment' -ne "Shadow") { $failures += "appsettings.${envName}.json: PolicyEngine:Core.Recruitment is not Shadow" }
    if ($json.Recruitment.Approvals.AlwaysStartWorkflow -ne $true) { $failures += "appsettings.${envName}.json: Recruitment:Approvals:AlwaysStartWorkflow is not true" }
    if ($json.Workflow.AdminBypass -ne $false) { $failures += "appsettings.${envName}.json: Workflow:AdminBypass is not false" }
    if (-not ($json.Workflow.EnforceStepRoleEntityTypes -contains "RecruitmentRequisition")) {
        $failures += "appsettings.${envName}.json: Workflow:EnforceStepRoleEntityTypes lacks the recruitment types"
    }
}
$base = Read-Text "src/Services/HRCore/HR.Core.Web/appsettings.json" | ConvertFrom-Json
if ($base.Workflow.AdminBypass -ne $false) { $failures += "appsettings.json: Workflow:AdminBypass is not false" }
$prodLike = Read-Text "docker-compose.acceptance-prodlike.yml"
foreach ($line in @('NationalScope__IsLabScale: "false"', 'PolicyEngine__Core.Recruitment: "Shadow"',
        'Recruitment__Approvals__AlwaysStartWorkflow: "true"', 'Workflow__AdminBypass: "false"')) {
    if (-not $prodLike.Contains($line)) { $failures += "docker-compose.acceptance-prodlike.yml lacks $line" }
}

# 3. Server-side enforcement.
$guard = Read-Text "src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs"
if ($guard -notmatch 'public static string\? ValidateStepRole\(' -or $guard -notmatch 'public static string\? ValidateReject\(') {
    $failures += "WorkflowStageGuard lacks ValidateStepRole/ValidateReject"
}
$actions = Read-Text "src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs"
if (([regex]::Matches($actions, '_evaluator\.GetBlockReasonAsync')).Count -lt 2) { $failures += "WorkflowInstanceActionService does not run the evaluator on approve and reject" }
if ($actions -match 'ValidateSelfApprovalAsync') { $failures += "ValidateSelfApprovalAsync still exists" }
foreach ($controller in @("Competitions", "EstablishmentChanges", "OfferLetters", "RecruitmentRequisitions", "SelectionCommittees", "WorkflowInstances", "RecruitmentAppeals")) {
    $text = Read-Text "src/Services/HRCore/HR.Core.Web/Controllers/${controller}Controller.cs"
    if ($text -match 'FindFirstValue\(ClaimTypes\.Role\)') { $failures += "${controller}Controller builds the workflow actor from the first role claim" }
    if ($text -match 'new WorkflowActionActor\(') { $failures += "${controller}Controller constructs WorkflowActionActor directly (use FromPrincipal)" }
}

# 4. G-14 and G-15.
$seeds = Read-Text "src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs"
if ($seeds -match 'Rule\("R-REC-HIRE"') { $failures += "PolicyEngineDomainSeeds still seeds R-REC-HIRE" }
if ($seeds -notmatch 'DeactivateRuleIfPresentAsync\(db, "R-REC-HIRE"') { $failures += "R-REC-HIRE is not deactivated on existing databases" }
$pack = Read-Text "src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs"
if ($pack -notmatch '"RECRUITMENT-HIRE"\s*\r?\n\s*\}' -or $pack -notmatch 'RetireAsync') { $failures += "RECRUITMENT-HIRE is not in WorkflowPackSeeder.RetiredCodes / RetireAsync" }
if ((Read-Text "src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyStaticFallback.cs") -match '"RECRUITMENT-HIRE"') {
    $failures += "PolicyStaticFallback still triggers RECRUITMENT-HIRE"
}
if (-not (Test-Path (Resolve-RepoPath "tests/architecture/hrcore-post-policy-allowlist.txt"))) { $failures += "missing tests/architecture/hrcore-post-policy-allowlist.txt" }

# 5. CI: the prod-like journey runs with a zero-skip step (structural YAML check).
$ci = Read-Text ".github/workflows/e2e-journeys.yml"
if ($ci -match "`t") { $failures += "e2e-journeys.yml contains a tab" }
foreach ($needle in @('--filter "Category=JourneyProdLike"', 'Fail on skipped prod-like journey', 'docker-compose.acceptance-prodlike.yml')) {
    if (-not $ci.Contains($needle)) { $failures += "e2e-journeys.yml lacks «$needle»" }
}
$odd = ($ci -split "`n") | Where-Object { $_ -match '^( +)\S' -and ($Matches[1].Length % 2) -ne 0 -and $_ -notmatch '^\s+--' }
if ($odd) { $notes += "e2e-journeys.yml: $(@($odd).Count) line(s) with odd indentation inside folded blocks (allowed)" }

# 6. Tests.
$tests = "skipped"
if (-not $SkipTests) {
    $trxDir = Resolve-RepoPath "artifacts/hcm025/tr"
    New-Item -ItemType Directory -Force -Path $trxDir | Out-Null
    $trx = Join-Path $trxDir "hcm025-validate.trx"
    if (Test-Path $trx) { Remove-Item $trx -Force }
    dotnet test (Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") `
        --filter "FullyQualifiedName~Hcm025|FullyQualifiedName~WorkflowStageGuard|FullyQualifiedName~Architecture" `
        --logger "trx;LogFileName=hcm025-validate.trx" --results-directory $trxDir | Out-Host
    if (-not (Test-Path $trx)) {
        $failures += "unit tests produced no results"
    }
    else {
        [xml]$doc = Get-Content -Raw -Encoding UTF8 -Path $trx
        $failed = @($doc.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq "Failed" } | ForEach-Object { $_.testName })
        $known = @($failed | Where-Object { $_ -like "*BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard*" })
        $unexpected = @($failed | Where-Object { $_ -notin $known })
        $passed = @($doc.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq "Passed" }).Count
        $tests = "passed=$passed failed=$($failed.Count) (known baseline: $($known.Count))"
        if ($known.Count -gt 0) { $notes += "known baseline failure: BolaArchitectureTests (HR.Contracts Index actions, pre-PLAN-25)" }
        foreach ($name in $unexpected) { $failures += "unit test failed: $name" }
    }
}

$reportDir = Resolve-RepoPath "artifacts/hcm025"
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
[ordered]@{
    plan = "PLAN-25"
    ranAt = (Get-Date).ToUniversalTime().ToString("o")
    tests = $tests
    notes = $notes
    failures = $failures
    result = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
} | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $reportDir "validate-report.json") -Encoding UTF8

foreach ($n in $notes) { Write-Host "note: $n" }
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "FAIL: $f" -ForegroundColor Red }
    Write-Host "hcm025-validate: FAIL ($($failures.Count))"
    exit 1
}
Write-Host "hcm025-validate: PASS (tests: $tests)"
