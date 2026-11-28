# PLAN-60 (SEP-A): static checks of the separation journey's governance and of what guards it.
# ASCII only; Windows PowerShell 5.1 syntax (run here by pwsh 7).
#
#   pwsh -NoProfile -File scripts/sep060-validate.ps1 [-SelfTest]
#
# What it checks (AC-SEP-60a..i and the Sep060* tests prove the behaviour; this script guards the files a reviewer or an operator edits):
#   1. AcSep60SeparationGovernanceTests is required in prodLike[] group main, AcSep60OutcomeSweepTests in prodLike[] group isolated;
#      ac-ownership.json owns AC-SEP-60 for PLAN-60 with provesClaim.
#   2. Both classes are marked [RequiredAcceptance].
#   3. No SQL write in AcSep60*.cs or AcceptanceLeaverFixture.cs (UPDATE / INSERT INTO / DELETE FROM / TRUNCATE, comments aside), and
#      no call of a helper that writes SQL, but one named allowance: the fixture's single call of
#      AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync (the payroll classes' stated precondition; P-12's deviation).
#   4. The persona a-leaver exists (hr-employee, AGENCY-A, HQ, a template), and AcceptancePersonaCatalog counts AGENCY-A's personas as
#      personas.json does.
#   5. SeparationRecord's entry of WorkflowDefinitionsHaveHandlersTests names SeparationApprovalOutcomeConsumer with no PendingPlan.
#   6. WorkflowPackSeeder seeds SEPARATION-APPROVAL (hr-core, hr-management-approver) and SEPARATION-APPROVAL-SENSITIVE (hr-core,
#      compliance, hr-management-approver), and both are in DistinctApproverCodes.
#   7. StartSeparationSagaAsync is called by ExecuteSeparationHandler alone.
#   8. No action of SeparationRecordsController or SeparationWizardController takes an approver's id, and every POST names a policy.
#   9. The SEP060 migration lifts FORCE on each table it changes and puts it back.
#  10. appsettings.json has the Separation keys; the prod-like overlay gives hr-contracts-web the sweep's keys.
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
    return ($text.TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

$paths = @{
    Acceptance = "src/Tests/HRManagement.AcceptanceTests"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
    Personas = "testdata/acceptance/personas.json"
    Catalog = "src/Shared/HRManagement.ServiceDefaults/Seeding/AcceptancePersonaCatalog.cs"
    Guard = "src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs"
    Seeder = "src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs"
    Contracts = "src/Services/HRContracts"
    Handlers = "src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs"
    RecordsController = "src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs"
    WizardController = "src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs"
    Settings = "src/Services/HRContracts/HR.Contracts.Web/appsettings.json"
    Overlay = "docker-compose.acceptance-prodlike.yml"
}
$classes = @{ "AcSep60SeparationGovernanceTests" = "main"; "AcSep60OutcomeSweepTests" = "isolated" }
$forcedTables = @("SeparationRecords", "SeparationChecklists", "SeparationSagaStates", "ServiceTransactions")

function Remove-Comments([string]$text) {
    if ($null -eq $text) { return "" }
    return (($text -replace '(?s)/\*.*?\*/', '') -replace '(?m)//.*$', '')
}

# 1. scope and ownership
function Test-Scope($scope, $ownership) {
    $problems = @()
    if ($null -eq $scope) { return @("ci-scope.json is missing") }
    foreach ($class in $classes.Keys) {
        $entry = @($scope.prodLike | Where-Object { $_.class -eq $class })
        if ($entry.Count -ne 1 -or -not $entry[0].required -or $entry[0].group -ne $classes[$class]) {
            $problems += "$class is not a required prodLike class of the group $($classes[$class])"
        }
    }
    $owned = if ($null -eq $ownership) { $null } else { $ownership.acs."AC-SEP-60" }
    if ($null -eq $owned -or $owned.ownerPlan -ne "PLAN-60" -or -not $owned.provesClaim) {
        $problems += "ac-ownership.json does not own AC-SEP-60 for PLAN-60 with provesClaim"
    }
    return $problems
}

# 2. [RequiredAcceptance]
function Test-Required([hashtable]$texts) {
    $problems = @()
    foreach ($class in $classes.Keys) {
        $text = $texts[$class]
        if ($null -eq $text) { $problems += "$class.cs is missing"; continue }
        if ($text -notmatch "\[RequiredAcceptance\][^\r\n]*\r?\n(?:\[[^\r\n]*\]\r?\n)*public (?:sealed )?(?:partial )?class $class\b") {
            $problems += "$class is not marked [RequiredAcceptance]"
        }
    }
    return $problems
}

# 3. no SQL write
$sqlWritingHelpers = '\b(AcceptancePayrollRegime\.\w+|InsertAssetAsync|Pol047Pg\.ExecAsync)\s*\('
$allowedHelper = 'AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync('
function Test-NoSqlWrite($sources) {
    $problems = @()
    if (@($sources).Count -lt 3) { $problems += "the AC-SEP-60 classes or the leaver fixture are missing" }
    $allowed = 0
    foreach ($source in $sources) {
        $code = Remove-Comments $source.Text
        if ($code -cmatch '\b(UPDATE\s+\\?"?\w|INSERT\s+INTO|DELETE\s+FROM|TRUNCATE\b)') { $problems += "$($source.Path) writes SQL: $($Matches[0])" }
        foreach ($call in [regex]::Matches($code, $sqlWritingHelpers)) {
            if ($source.Path -like "*AcceptanceLeaverFixture.cs" -and $call.Value -replace '\s+', '' -eq $allowedHelper) { $allowed++; continue }
            $problems += "$($source.Path) calls a helper that writes SQL: $($call.Value)"
        }
    }
    if ($allowed -gt 1) { $problems += "the fixture calls $allowedHelper more than once (one named allowance)" }
    return $problems
}

# 4. persona and catalog
function Test-Persona($personas, [string]$catalog) {
    $problems = @()
    if ($null -eq $personas) { return @("personas.json is missing") }
    $leaver = @($personas.personas | Where-Object { $_.id -eq "a-leaver" })
    if ($leaver.Count -ne 1) { return @("no persona a-leaver") }
    $p = $leaver[0]
    if (@($p.roles) -join "," -ne "hr-employee" -or $p.tenantCode -ne "AGENCY-A" -or $p.employeeSiteCode -ne "HQ" -or $p.purpose -notmatch "template") {
        $problems += "a-leaver is not an hr-employee template of AGENCY-A's HQ"
    }
    $count = @($personas.personas | Where-Object { $_.tenantCode -eq "AGENCY-A" -and $null -eq $_.fixture }).Count
    if ($null -eq $catalog -or $catalog -notmatch ('\["AGENCY-A"\]\s*=\s*' + $count + '\b')) {
        $problems += "AcceptancePersonaCatalog does not count $count AGENCY-A personas"
    }
    return $problems
}

# 5. the guard's entry
function Test-Guard([string]$text) {
    if ($null -eq $text -or $text -notmatch '\["SeparationRecord"\]\s*=\s*new\("HR\.Contracts",\s*"SeparationApprovalOutcomeConsumer"\)') {
        return @("SeparationRecord's guard entry does not name SeparationApprovalOutcomeConsumer without a PendingPlan")
    }
    return @()
}

# 6. the two chains
function Test-Seeder([string]$text) {
    $problems = @()
    if ($null -eq $text) { return @("WorkflowPackSeeder.cs is missing") }
    # One entry of the list: anything up to the next "new(Guid" (the descriptions hold ';' and line breaks).
    $within = '(?:(?!new\(Guid)[\s\S])*?'
    $standard = '"SEPARATION-APPROVAL",' + $within + '"SeparationRecord",' + $within + '\(1,\s*"[^"]+",\s*"hr-core"\),\s*\(2,\s*"[^"]+",\s*"hr-management-approver"\)\)'
    $sensitive = '"SEPARATION-APPROVAL-SENSITIVE",' + $within + '"SeparationRecord",' + $within + '\(1,\s*"[^"]+",\s*"hr-core"\),\s*\(2,\s*"[^"]+",\s*"compliance"\),\s*\(3,\s*"[^"]+",\s*"hr-management-approver"\)\)'
    if ($text -notmatch $standard) { $problems += "SEPARATION-APPROVAL is not seeded with hr-core then hr-management-approver" }
    if ($text -notmatch $sensitive) { $problems += "SEPARATION-APPROVAL-SENSITIVE is not seeded with hr-core, compliance, hr-management-approver" }
    if ($text -match '(?s)DistinctApproverCodes\s*=\s*new[^{]*\{(?<codes>[^}]*)\}') {
        $codes = $Matches.codes
        foreach ($code in @("SEPARATION-APPROVAL", "SEPARATION-APPROVAL-SENSITIVE")) {
            if ($codes -notmatch ('"' + [regex]::Escape($code) + '"')) { $problems += "$code is not in DistinctApproverCodes" }
        }
    }
    else { $problems += "DistinctApproverCodes not found" }
    return $problems
}

# 7. the saga starts in the execution only
function Test-Saga($sources, [string]$handlers) {
    $problems = @()
    $callers = @($sources | Where-Object { (Remove-Comments $_.Text) -match '\.StartSeparationSagaAsync\(' } | ForEach-Object { Split-Path $_.Path -Leaf })
    if (($callers -join ",") -ne "SeparationRecordHandlers.cs") { $problems += "StartSeparationSagaAsync is called from: $($callers -join ', ')" }
    $code = Remove-Comments $handlers
    $calls = [regex]::Matches($code, '\.StartSeparationSagaAsync\(')
    if ($calls.Count -ne 1) { $problems += "SeparationRecordHandlers.cs calls StartSeparationSagaAsync $($calls.Count) time(s)" }
    elseif (([regex]::Matches($code.Substring(0, $calls[0].Index), 'public class (\w+)') | Select-Object -Last 1).Groups[1].Value -ne "ExecuteSeparationHandler") {
        $problems += "the saga is started outside ExecuteSeparationHandler"
    }
    return $problems
}

# 8. controllers
function Test-Controllers([hashtable]$texts) {
    $problems = @()
    foreach ($name in $texts.Keys) {
        $code = Remove-Comments $texts[$name]
        if ($code -match '(?i)\b(Guid\??|string)\s+(approvedBy|approverId|secondApprover)\b') { $problems += "$name takes an approver's id ($($Matches[2]))" }
        foreach ($m in [regex]::Matches($code, '(?s)((?:\[[^\]]+\]\s*)+)public async Task<IActionResult> (\w+)\(')) {
            $attributes = $m.Groups[1].Value
            if ($attributes -match '\[HttpPost' -and $attributes -notmatch '\[Authorize\(Policy = HrManagementPolicies\.HrContracts(Write|Approve)\)\]') {
                $problems += "$name.$($m.Groups[2].Value) is a POST with no explicit HrContracts policy"
            }
        }
    }
    return $problems
}

# 9. the migration's FORCE
function Get-Migration {
    $dir = Join-Path $root "$($paths.Contracts)/HR.Contracts.Infrastructure/Migrations"
    $file = Get-ChildItem -Path $dir -File -Filter "*_SEP060_SeparationStatusGovernance.cs" | Select-Object -First 1
    if ($null -eq $file) { return $null }
    return [System.IO.File]::ReadAllText($file.FullName)
}

function Test-Migration([string]$text) {
    $problems = @()
    if ($null -eq $text) { return @("the SEP060 migration is missing") }
    foreach ($table in $forcedTables) {
        if ($text -notmatch ('ALTER TABLE "' + $table + '" NO FORCE ROW LEVEL SECURITY')) { $problems += "SEP060 does not lift FORCE on $table" }
        if ($text -notmatch ('ALTER TABLE "' + $table + '" FORCE ROW LEVEL SECURITY')) { $problems += "SEP060 does not put FORCE back on $table" }
        if ($text -notmatch ('relforcerowsecurity INTO \w+ FROM pg_class WHERE oid = ''"' + $table + '"''::regclass')) { $problems += "SEP060 does not read $table's FORCE before lifting it" }
    }
    return $problems
}

# 10. settings and overlay
function Test-Settings([string]$settings, [string]$overlay) {
    $problems = @()
    $json = Get-Json $settings
    if ($null -eq $json -or $null -eq $json.Separation) { return @("appsettings.json has no Separation section") }
    if (@($json.Separation.Approval.SensitiveTypes).Count -lt 1) { $problems += "Separation:Approval:SensitiveTypes is missing" }
    if ($null -eq $json.Separation.Decision.IssuerMustDifferFromCreator) { $problems += "Separation:Decision:IssuerMustDifferFromCreator is missing" }
    if ($null -eq $json.Separation.Checklist.RequireEmployeeNotification) { $problems += "Separation:Checklist:RequireEmployeeNotification is missing" }
    if ($null -eq $overlay -or $overlay -notmatch '(?s)\n  hr-contracts-web:\s*\n(?:(?!\n  \S).)*WorkflowApproval__Sweep__IntervalMinutes: "\d+"(?:(?!\n  \S).)*WorkflowApproval__Sweep__StaleAfterMinutes: "\d+"') {
        $problems += "the prod-like overlay does not give hr-contracts-web the sweep's keys"
    }
    return $problems
}

function Get-ClassTexts {
    $texts = @{}
    foreach ($class in $classes.Keys) { $texts[$class] = Read-Text "$($paths.Acceptance)/$class.cs" }
    return $texts
}

function Get-AcceptanceSources {
    $dir = Join-Path $root $paths.Acceptance
    return @(Get-ChildItem -Path $dir -File -Filter "*.cs" | Where-Object { $_.Name -like "AcSep60*" -or $_.Name -eq "AcceptanceLeaverFixture.cs" } | ForEach-Object {
            @{ Path = "$($paths.Acceptance)/$($_.Name)"; Text = [System.IO.File]::ReadAllText($_.FullName) }
        })
}

function Get-ContractsSources {
    $dir = Join-Path $root $paths.Contracts
    return @(Get-ChildItem -Path $dir -File -Recurse -Filter "*.cs" | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
            @{ Path = $_.FullName; Text = [System.IO.File]::ReadAllText($_.FullName) }
        })
}

function Get-ControllerTexts {
    return @{ "SeparationRecordsController" = (Read-Text $paths.RecordsController); "SeparationWizardController" = (Read-Text $paths.WizardController) }
}

function Invoke-All {
    $problems = @()
    $problems += Test-Scope (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    $problems += Test-Required (Get-ClassTexts)
    $problems += Test-NoSqlWrite (Get-AcceptanceSources)
    $problems += Test-Persona (Get-Json (Read-Text $paths.Personas)) (Read-Text $paths.Catalog)
    $problems += Test-Guard (Read-Text $paths.Guard)
    $problems += Test-Seeder (Read-Text $paths.Seeder)
    $problems += Test-Saga (Get-ContractsSources) (Read-Text $paths.Handlers)
    $problems += Test-Controllers (Get-ControllerTexts)
    $problems += Test-Migration (Get-Migration)
    $problems += Test-Settings (Read-Text $paths.Settings) (Read-Text $paths.Overlay)
    return @($problems | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $planted = 0

    # 1. scope
    $ownership = Get-Json (Read-Text $paths.Ownership)
    $scope = Get-Json (Read-Text $paths.Scope); ($scope.prodLike | Where-Object { $_.class -eq "AcSep60SeparationGovernanceTests" }).required = $false
    $planted++; if ((Test-Scope $scope $ownership).Count -eq 0) { $failures += "a..h not required was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope); ($scope.prodLike | Where-Object { $_.class -eq "AcSep60OutcomeSweepTests" }).group = "main"
    $planted++; if ((Test-Scope $scope $ownership).Count -eq 0) { $failures += "i in the main group was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope); $own2 = Get-Json (Read-Text $paths.Ownership); $own2.acs."AC-SEP-60".provesClaim = $false
    $planted++; if ((Test-Scope $scope $own2).Count -eq 0) { $failures += "AC-SEP-60 not proving its claim was not refused" }

    # 2. [RequiredAcceptance]
    $texts = Get-ClassTexts
    $texts["AcSep60OutcomeSweepTests"] = $texts["AcSep60OutcomeSweepTests"] -replace '\[RequiredAcceptance\][^\r\n]*\r?\n', ''
    $planted++; if ((Test-Required $texts).Count -eq 0) { $failures += "i without [RequiredAcceptance] was not refused" }

    # 3. SQL writes
    $sources = Get-AcceptanceSources
    foreach ($sql in @('var q = "UPDATE \"SeparationRecords\" SET \"Status\" = 1";', 'var q = "INSERT INTO x VALUES (1)";', 'var q = "DELETE FROM x";')) {
        $copy = @($sources | ForEach-Object { if ($_.Path -like "*AcceptanceLeaverFixture.cs") { @{ Path = $_.Path; Text = $_.Text + "`nclass Planted { void M() { $sql } }" } } else { $_ } })
        $planted++; if ((Test-NoSqlWrite $copy).Count -eq 0) { $failures += "a SQL write in the fixture was not refused: $sql" }
    }
    $copy = @($sources | ForEach-Object { if ($_.Path -like "*AcceptanceLeaverFixture.cs") { @{ Path = $_.Path; Text = $_.Text + "`n// UPDATE nothing: a comment" } } else { $_ } })
    $planted++; if ((Test-NoSqlWrite $copy).Count -ne 0) { $failures += "a comment was taken for a SQL write" }
    $copy = @($sources | ForEach-Object { if ($_.Path -like "*AcSep60SeparationGovernanceTests.cs") { @{ Path = $_.Path; Text = $_.Text + "`nclass Planted { async Task M() { await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(t); } }" } } else { $_ } })
    $planted++; if ((Test-NoSqlWrite $copy).Count -eq 0) { $failures += "the regime helper called from a test class was not refused" }
    $copy = @($sources | ForEach-Object { if ($_.Path -like "*AcceptanceLeaverFixture.cs") { @{ Path = $_.Path; Text = $_.Text + "`nclass Planted { async Task M() { await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(t); } }" } } else { $_ } })
    $planted++; if ((Test-NoSqlWrite $copy).Count -eq 0) { $failures += "a second call of the allowed helper was not refused" }

    # 4. persona
    $personas = Get-Json (Read-Text $paths.Personas); $catalog = Read-Text $paths.Catalog
    $copy = Get-Json (Read-Text $paths.Personas); $copy.personas = @($copy.personas | Where-Object { $_.id -ne "a-leaver" })
    $planted++; if ((Test-Persona $copy $catalog).Count -eq 0) { $failures += "a missing a-leaver was not refused" }
    $planted++; if ((Test-Persona $personas ($catalog -replace '\["AGENCY-A"\]\s*=\s*\d+', '["AGENCY-A"] = 1')).Count -eq 0) { $failures += "a wrong AGENCY-A count was not refused" }

    # 5. guard
    $guard = Read-Text $paths.Guard
    $planted++; if ((Test-Guard ($guard -replace '"SeparationApprovalOutcomeConsumer"\)', '"SeparationApprovalOutcomeConsumer", PendingPlan: "PLAN-60")')).Count -eq 0) { $failures += "a PendingPlan was not refused" }

    # 6. seeder
    $seeder = Read-Text $paths.Seeder
    $planted++; if ((Test-Seeder ($seeder -replace '"SEPARATION-APPROVAL-SENSITIVE"\r?\n', '')).Count -eq 0) { $failures += "a sensitive code out of DistinctApproverCodes was not refused" }
    $planted++; if ((Test-Seeder ($seeder -replace '"compliance"\), \(3,', '"hr-core"), (3,')).Count -eq 0) { $failures += "a sensitive chain without compliance was not refused" }

    # 7. saga
    $contracts = Get-ContractsSources; $handlers = Read-Text $paths.Handlers
    $planted++; if ((Test-Saga ($contracts + @(@{ Path = "Planted.cs"; Text = "class P { void M() { e.StartSeparationSagaAsync(x); } }" })) $handlers).Count -eq 0) { $failures += "a second caller of the saga was not refused" }
    $moved = $handlers -replace 'public class CancelSeparationHandler', 'public class ZzMoved' -replace 'public class ExecuteSeparationHandler', 'public class OtherHandler'
    $planted++; if ((Test-Saga $contracts $moved).Count -eq 0) { $failures += "the saga started outside ExecuteSeparationHandler was not refused" }

    # 8. controllers
    $ctl = Get-ControllerTexts
    $ctl["SeparationRecordsController"] = $ctl["SeparationRecordsController"] + "`nclass P { [HttpPost] public async Task<IActionResult> Planted(Guid id, Guid approvedBy) { return null; } }"
    $planted++; if ((Test-Controllers $ctl).Count -eq 0) { $failures += "an approvedBy parameter or a POST without a policy was not refused" }

    # 9. migration
    $migration = Get-Migration
    $planted++; if ((Test-Migration ($migration -replace 'ALTER TABLE "SeparationSagaStates" FORCE ROW LEVEL SECURITY', 'SELECT 1')).Count -eq 0) { $failures += "FORCE not put back on SeparationSagaStates was not refused" }

    # 10. settings
    $settings = Read-Text $paths.Settings; $overlay = Read-Text $paths.Overlay
    $planted++; if ((Test-Settings ($settings -replace '"RequireEmployeeNotification"', '"Other"') $overlay).Count -eq 0) { $failures += "a missing Separation key was not refused" }
    $planted++; if ((Test-Settings $settings ($overlay -replace 'WorkflowApproval__Sweep__StaleAfterMinutes: "5"', 'X: "5"')).Count -eq 0) { $failures += "hr-contracts-web without the sweep's keys was not refused" }

    $own = Invoke-All
    if ($own.Count -ne 0) { $failures += "the repository itself does not pass: $($own -join '; ')" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] sep060-validate self-test: $planted planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] sep060-validate: both AC-SEP-60 classes are required in their groups and owned, they and the leaver fixture write no SQL (but the fixture's one named regime precondition), the persona a-leaver is a template counted by the catalog, the separation's guard entry has its consumer, the two chains are seeded with D-SEP-4's roles and distinct approvers, the saga starts in the execution alone, no separation action takes an approver's id and every POST names its policy, SEP060 lifts and puts back FORCE on its tables, and the Separation and sweep settings are in place" -ForegroundColor Green
exit 0
