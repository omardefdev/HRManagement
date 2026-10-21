# SYR-003: validate service regimes artifacts.
# Usage:
#   powershell -File scripts/syr003-validate.ps1

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
    "docs/SYR_003_SERVICE_REGIMES.md",
    "docs/syr/service-regimes.json",
    "src/Shared/HRManagement.SharedKernel/Legal/LegalRegime.cs",
    "scripts/syr003-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr003ServiceRegimeTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify LegalRegime entity has required properties
$regimeFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Legal/LegalRegime.cs"
if (Test-Path $regimeFile) {
    $content = Get-Content -Raw -Path $regimeFile
    $requiredProps = @("LegalRegimeId", "RegimeName", "Type", "LegislationRef", "ResolvedLegalSourceId",
                       "AllowedEmploymentTypes", "EffectiveFrom", "IsActive",
                       "TransitionDecisionRef", "ConflictNotes")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "LegalRegime missing required property: $prop"
        }
    }
    if ($content -match ":\s*BaseEntity") {
        $failures += "LegalRegime must not extend BaseEntity (global register entity)"
    }
    if ($content -notmatch "Law50") {
        $failures += "LegalRegime must define Law50 regime type"
    }
    if ($content -notmatch "Law53") {
        $failures += "LegalRegime must define Law53 regime type"
    }
    if ($content -notmatch "Special") {
        $failures += "LegalRegime must define Special regime type"
    }
}

# Verify service-regimes.json structure
$jsonFile = Resolve-RepoPath "docs/syr/service-regimes.json"
if (Test-Path $jsonFile) {
    $content = Get-Content -Raw -Path $jsonFile
    $requiredKeys = @("regimes", "employment_types", "transition_rules", "exception_handling")
    foreach ($key in $requiredKeys) {
        if ($content -notmatch $key) {
            $failures += "service-regimes.json missing key: $key"
        }
    }
    if ($content -notmatch "law50") {
        $failures += "service-regimes.json must define law50 regime"
    }
    if ($content -notmatch "law53") {
        $failures += "service-regimes.json must define law53 regime"
    }
    if ($content -notmatch "movement_types") {
        $failures += "service-regimes.json missing movement_types"
    }
    if ($content -notmatch "promotion_rules") {
        $failures += "service-regimes.json missing promotion_rules (PLAN-SYR-09)"
    }
    if ($content -notmatch "separation_rules") {
        $failures += "service-regimes.json missing separation_rules (PLAN-SYR-09)"
    }
    if ($content -notmatch "article_159_patterns") {
        $failures += "service-regimes.json missing article_159_patterns (PLAN-SYR-09)"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_003_SERVICE_REGIMES.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-003") { $failures += "doc missing SYR-003 reference" }
    if ($doc -notmatch "Law 50") { $failures += "doc must reference Law 50" }
    if ($doc -notmatch "Law 53") { $failures += "doc must reference Law 53" }
    if ($doc -notmatch "LegalRegime") { $failures += "doc must describe LegalRegime entity" }
    if ($doc -notmatch "PLAN-SYR-07") { $failures += "doc must document PLAN-SYR-07 exception queue + transitions" }
    if ($doc -notmatch "PLAN-SYR-08") { $failures += "doc must document PLAN-SYR-08 movement regime binding" }
    if ($doc -notmatch "PLAN-SYR-09") { $failures += "doc must document PLAN-SYR-09 regime lifecycle completion" }
}

# Validate SYR-007 runtime enforcement (PLAN-SYR-07)
$syr007Required = @(
    "src/Services/HRCore/HR.Core.Domain/Entities/ServiceRegimeException.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/EmploymentRegimeTransition.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimeTransitionService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/ServiceRegimeExceptionsController.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/ServiceRegimeInternalController.cs",
    "src/Tests/HRManagement.UnitTests/Syr007RegimeGovernanceTests.cs"
)
foreach ($rel in $syr007Required) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$migrationDir = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
$syr007Migration = Get-ChildItem -Path $migrationDir -Filter "*SYR007_RegimeGovernance.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $syr007Migration) {
    $failures += "missing SYR007_RegimeGovernance migration"
}

$payrollGovernance = Resolve-RepoPath "src/Services/Payroll/Payroll.Application/Services/PayrollRegimeGovernance.cs"
if (-not (Test-Path $payrollGovernance)) {
    $failures += "missing PayrollRegimeGovernance.cs"
}

# Validate SYR-008 movement regime binding (PLAN-SYR-08)
$syr008Required = @(
    "src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeMovementRules.cs",
    "src/Shared/HRManagement.SharedKernel/Legal/IHrCoreActiveRegimeClient.cs",
    "src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreActiveRegimeClient.cs",
    "src/Services/HRMovements/HR.Movements.Infrastructure/Services/MovementRegimeValidationService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Consumers/SecondmentExecutedConsumer.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Consumers/LoanAssignmentExecutedConsumer.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimeActiveRegimeService.cs",
    "src/Tests/HRManagement.UnitTests/Syr008MovementRegimeTests.cs"
)
foreach ($rel in $syr008Required) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$movementsMigrationDir = Resolve-RepoPath "src/Services/HRMovements/HR.Movements.Infrastructure/Migrations"
$syr008Migration = Get-ChildItem -Path $movementsMigrationDir -Filter "*SYR008_MovementRegimeBinding.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $syr008Migration) {
    $failures += "missing SYR008_MovementRegimeBinding migration"
}

$transferExecuted = Resolve-RepoPath "src/Shared/HRManagement.MessageContracts/Events/ITransferExecuted.cs"
if (Test-Path $transferExecuted) {
    $evt = Get-Content -Raw -Path $transferExecuted
    if ($evt -notmatch "SchemaVersion => 3") {
        $failures += "ITransferExecuted must be schema version 3"
    }
    if ($evt -notmatch "FromLegalRegimeId") {
        $failures += "ITransferExecuted must carry FromLegalRegimeId"
    }
    if ($evt -notmatch "PayrollRegimeCode") {
        $failures += "ITransferExecuted must carry PayrollRegimeCode"
    }
}

$transferConsumer = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs"
if (Test-Path $transferConsumer) {
    $consumer = Get-Content -Raw -Path $transferConsumer
    if ($consumer -notmatch "LegalRegimeId") {
        $failures += "TransferExecutedConsumer must set EmploymentRecord.LegalRegimeId"
    }
    if ($consumer -notmatch "ApplyTransitionAsync") {
        $failures += "TransferExecutedConsumer must call ApplyTransitionAsync for cross-regime"
    }
}

$movementRules = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeMovementRules.cs"
if (Test-Path $movementRules) {
    $rules = Get-Content -Raw -Path $movementRules
    if ($rules -notmatch "PreservesRegime") {
        $failures += "ServiceRegimeMovementRules must expose PreservesRegime"
    }
    if ($rules -notmatch "movement_types") {
        $failures += "ServiceRegimeMovementRules must load movement_types"
    }
}

$serviceRegimesJson = Resolve-RepoPath "docs/syr/service-regimes.json"
if (Test-Path $serviceRegimesJson) {
    $regimes = Get-Content -Raw -Path $serviceRegimesJson | ConvertFrom-Json
    if ($regimes.exception_handling.blocks_final_payroll -ne $true) {
        $failures += "service-regimes.json must set exception_handling.blocks_final_payroll = true"
    }
}

# Validate SYR-009 regime lifecycle completion (PLAN-SYR-09)
$syr009Required = @(
    "src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimePromotionRules.cs",
    "src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeSeparationRules.cs",
    "src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeArticle159Rules.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Consumers/PromotionExecutedConsumer.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Consumers/DelegationExecutedConsumer.cs",
    "src/Services/HRMovements/HR.Movements.Domain/Entities/PromotionRequest.cs",
    "src/Services/HRMovements/HR.Movements.Domain/Entities/DelegationRequest.cs",
    "src/Services/HRContracts/HR.Contracts.Application/Services/SeparationRegimeValidationService.cs",
    "src/Tests/HRManagement.UnitTests/Syr009RegimeLifecycleTests.cs"
)
foreach ($rel in $syr009Required) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$hrcoreMigrationDir = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
$syr009Article159 = Get-ChildItem -Path $hrcoreMigrationDir -Filter "*SYR009_Article159Governance.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $syr009Article159) {
    $failures += "missing SYR009_Article159Governance migration"
}

$contractsMigrationDir = Resolve-RepoPath "src/Services/HRContracts/HR.Contracts.Infrastructure/Migrations"
$syr009Separation = Get-ChildItem -Path $contractsMigrationDir -Filter "*SYR009_SeparationRegimeBinding.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $syr009Separation) {
    $failures += "missing SYR009_SeparationRegimeBinding migration"
}

$movementsMigrationDirSyr09 = Resolve-RepoPath "src/Services/HRMovements/HR.Movements.Infrastructure/Migrations"
$syr009Movement = Get-ChildItem -Path $movementsMigrationDirSyr09 -Filter "*SYR009_*Movement*.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $syr009Movement) {
    $failures += "missing SYR009 promotion/delegation movement migration"
}

$exceptionEntity = Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Entities/ServiceRegimeException.cs"
if (Test-Path $exceptionEntity) {
    $exc = Get-Content -Raw -Path $exceptionEntity
    if ($exc -notmatch "Article159Conflict") {
        $failures += "ServiceRegimeExceptionType must include Article159Conflict"
    }
    if ($exc -notmatch "LegalDecisionId") {
        $failures += "ServiceRegimeException must persist LegalDecisionId"
    }
}

$separationCompleted = Resolve-RepoPath "src/Shared/HRManagement.MessageContracts/Events/ISeparationCompleted.cs"
if (Test-Path $separationCompleted) {
    $evt = Get-Content -Raw -Path $separationCompleted
    if ($evt -notmatch "SchemaVersion => 3") {
        $failures += "ISeparationCompleted must be schema version 3"
    }
    if ($evt -notmatch "PayrollRegimeCode") {
        $failures += "ISeparationCompleted must carry PayrollRegimeCode"
    }
}

$payrollSeed = Resolve-RepoPath "src/Services/Payroll/Payroll.Domain/Services/PayrollRegimeSeed.cs"
if (Test-Path $payrollSeed) {
    $seed = Get-Content -Raw -Path $payrollSeed
    if ($seed -notmatch "CreateLaw53Configuration") {
        $failures += "PayrollRegimeSeed must include CreateLaw53Configuration"
    }
}

$outDir = Resolve-RepoPath "testdata/syr003/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-003"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outDir "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: SYR-003 service regimes validate"
exit 0
