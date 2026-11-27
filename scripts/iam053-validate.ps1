# PLAN-53 (IAM-B): static checks of the payroll write policies and of the salary and reference change requests. ASCII only;
# Windows PowerShell 5.1.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/iam053-validate.ps1 [-SelfTest]
#
# What it checks (PayrollWriteActionsTests, PayProfileWritersTests, Iam053* and AC-IAM-53 prove the behaviour; this script guards the
# files a reviewer or an operator edits by hand):
#   1. No write-policy list is left under tests/architecture, and PLAN-53 is not one of CeilingList.Owners.
#   2. The direct write is gone: no "UpsertEmployeePayProfile" in the product code.
#   3. Payroll's settings (base, Production, Staging) hold Payroll:SalaryChange:*, Payroll:MasterData:RequireApproval=true and the
#      Payroll.MasterData roles, without Payroll.MasterData among the admin-excluded policies; the prod-like compose mirrors them.
#   4. Migration PAY053_SalaryChangeRequests is present, with no byte order mark, the filtered unique index of the one open request,
#      both Restrict relations and row security on both request tables.
#   5. The three events exist as contracts and are named in EVENT_PUBLISHER_INVENTORY.md, MESSAGE_EVENT_MATRIX.md and
#      rel-compat/event-schema-inventory.json.
#   6. Payroll.Web registers the org-scope filter with Payroll's keys and the activation job.
#   7. The acceptance seed orchestrator applies the service-regime patch, and nothing else does.
#   8. AcIam53SalaryChangeTests is a required prod-like class of the main group owned by PLAN-53, and the payroll regression
#      (AC-23, AC-24, AC-25) is required where it runs (AC-23 in the prod-like main group since PLAN-59; AC-24 and AC-25 on the lab).
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

function Read-Bytes([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path)) { return $null }
    return [System.IO.File]::ReadAllBytes($path)
}

function Get-Json([string]$text) {
    if ($null -eq $text) { return $null }
    # A settings file may start with a byte order mark.
    return ($text.TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

$paths = @{
    Owners = "src/Tests/HRManagement.ArchitectureTests/CeilingList.cs"
    WriteLists = "tests/architecture/write-policy-allowlist"
    Base = "src/Services/Payroll/Payroll.Web/appsettings.json"
    Production = "src/Services/Payroll/Payroll.Web/appsettings.Production.json"
    Staging = "src/Services/Payroll/Payroll.Web/appsettings.Staging.json"
    ProdLike = "docker-compose.acceptance-prodlike.yml"
    Migrations = "src/Services/Payroll/Payroll.Infrastructure/Migrations"
    Contracts = "src/Shared/HRManagement.MessageContracts/Events"
    Publishers = "docs/EVENT_PUBLISHER_INVENTORY.md"
    Matrix = "docs/MESSAGE_EVENT_MATRIX.md"
    Schema = "docs/rel-compat/event-schema-inventory.json"
    Program = "src/Services/Payroll/Payroll.Web/Program.cs"
    Orchestrator = "src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceSeedOrchestrator.cs"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
}
$events = @("IPayProfileChangeSubmitted", "IPayProfileChangeDecided", "IPayProfileChangeApplied")

# $lists: file names under tests/architecture/write-policy-allowlist; $owners: the text of CeilingList.cs
function Test-Lists([string[]]$lists, [string]$owners) {
    $problems = @()
    foreach ($name in $lists) { $problems += "write-policy-allowlist/$name exists: no service keeps a write-policy list" }
    if ($null -eq $owners) { $problems += "CeilingList.cs is missing" }
    else {
        $line = [regex]::Match($owners, 'Owners\s*=\s*\[([^\]]*)\]')
        if (-not $line.Success) { $problems += "CeilingList.Owners was not found" }
        elseif ($line.Groups[1].Value -match 'PLAN-53') { $problems += "PLAN-53 is still one of CeilingList.Owners" }
    }
    return $problems
}

# $sources: relative path -> text, every .cs file of the product code
function Test-DirectWrite([hashtable]$sources) {
    $problems = @()
    foreach ($name in $sources.Keys) {
        if ($sources[$name] -match 'UpsertEmployeePayProfile') { $problems += "${name}: UpsertEmployeePayProfile is back (a salary changes by a request another person approves)" }
    }
    return $problems
}

function Test-Settings([string]$label, $settings) {
    $problems = @()
    if ($null -eq $settings) { return @("${label}: the settings file is missing") }
    $change = $null
    $master = $null
    if ($null -ne $settings.Payroll) { $change = $settings.Payroll.SalaryChange; $master = $settings.Payroll.MasterData }
    if ($null -eq $change) { $problems += "${label}: Payroll:SalaryChange is missing" }
    else {
        if ($change.RequireMonthStart -ne $true) { $problems += "${label}: Payroll:SalaryChange:RequireMonthStart is not true (D-53-1)" }
        if ($change.AllowOutOfGradeBounds -ne $false) { $problems += "${label}: Payroll:SalaryChange:AllowOutOfGradeBounds is not false (D-53-2)" }
        if ($change.RequireLegalReference -ne $true) { $problems += "${label}: Payroll:SalaryChange:RequireLegalReference is not true (D-PAY-6)" }
        if ($null -eq $change.Activation -or [int]$change.Activation.IntervalMinutes -lt 1) { $problems += "${label}: Payroll:SalaryChange:Activation:IntervalMinutes is missing" }
    }
    if ($null -eq $master -or $master.RequireApproval -ne $true) { $problems += "${label}: Payroll:MasterData:RequireApproval is not true (D-53-4)" }
    $roles = @()
    $excluded = @()
    if ($null -ne $settings.Authorization) {
        if ($null -ne $settings.Authorization.Policies) {
            $policy = $settings.Authorization.Policies.PSObject.Properties | Where-Object { $_.Name -eq "Payroll.MasterData" } | Select-Object -First 1
            if ($null -ne $policy) { $roles = @($policy.Value.Roles) }
        }
        $excluded = @($settings.Authorization.AdminExcludedPolicies)
    }
    if (($roles -join ",") -ne "hr-admin,payroll-processor") { $problems += "${label}: the Payroll.MasterData roles are '$($roles -join ",")', not hr-admin,payroll-processor" }
    if ($roles -contains "payroll") { $problems += "${label}: the module role payroll is one of the Payroll.MasterData roles" }
    if ($excluded -contains "Payroll.MasterData") { $problems += "${label}: Payroll.MasterData is among Authorization:AdminExcludedPolicies" }
    return $problems
}

function Test-Mirror([string]$compose) {
    $problems = @()
    if ($null -eq $compose) { return @("docker-compose.acceptance-prodlike.yml is missing") }
    $expected = @(
        'Authorization__Policies__Payroll\.MasterData__Roles__0:\s*"hr-admin"',
        'Authorization__Policies__Payroll\.MasterData__Roles__1:\s*"payroll-processor"',
        'Payroll__SalaryChange__RequireMonthStart:\s*"true"',
        'Payroll__SalaryChange__AllowOutOfGradeBounds:\s*"false"',
        'Payroll__SalaryChange__RequireLegalReference:\s*"true"',
        'Payroll__SalaryChange__Activation__IntervalMinutes:\s*"\d+"',
        'Payroll__MasterData__RequireApproval:\s*"true"'
    )
    foreach ($pattern in $expected) {
        if ($compose -notmatch $pattern) { $problems += "the prod-like compose does not mirror $($pattern -replace '\\', '' -replace ':s\*.*$', '')" }
    }
    return $problems
}

function Test-Migration([byte[]]$bytes, [string]$text) {
    $problems = @()
    if ($null -eq $bytes -or $null -eq $text) { return @("the migration PAY053_SalaryChangeRequests is missing") }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $problems += "the migration starts with a byte order mark" }
    if ($text -notmatch 'IX_PayProfileChangeRequests_OneOpen' -or $text -notmatch 'filter:\s*"\\"State\\" IN \(0, 1\)"') { $problems += "the migration has no filtered unique index for the one open request (State IN (0, 1))" }
    foreach ($key in @("FK_EmployeePayProfiles_PayGrades_PayGradeId", "FK_PayGrades_SalaryScales_SalaryScaleId")) {
        $up = $text.Substring(0, [Math]::Max(0, $text.IndexOf("void Down(")))
        $match = [regex]::Match($up, "AddForeignKey\(\s*name:\s*`"$key`"[\s\S]*?onDelete:\s*ReferentialAction\.(\w+)")
        if (-not $match.Success -or $match.Groups[1].Value -ne "Restrict") { $problems += "the migration does not turn $key to Restrict" }
    }
    if ($text -notmatch 'EnableRls\(migrationBuilder,\s*"PayProfileChangeRequests",\s*"PayrollReferenceChangeRequests"\)') { $problems += "the migration does not enable row security on both request tables" }
    return $problems
}

# $contracts: event name -> text of its contract file (or $null)
function Test-Events([hashtable]$contracts, [string]$publishers, [string]$matrix, [string]$schema) {
    $problems = @()
    foreach ($name in $events) {
        if ($null -eq $contracts[$name] -or $contracts[$name] -notmatch "interface\s+$name\b") { $problems += "the contract $name is missing" }
        if ($null -eq $publishers -or $publishers -notmatch ('`' + $name + '`')) { $problems += "$name has no row in EVENT_PUBLISHER_INVENTORY.md" }
        if ($null -eq $matrix -or $matrix -notmatch ('`' + $name + '`')) { $problems += "$name has no row in MESSAGE_EVENT_MATRIX.md" }
        if ($null -eq $schema -or $schema -notmatch ('"id":\s*"' + $name + '"')) { $problems += "$name has no entry in event-schema-inventory.json" }
    }
    $submitted = $contracts["IPayProfileChangeSubmitted"]
    if ($null -ne $submitted -and $submitted -match '\bdecimal\b') { $problems += "IPayProfileChangeSubmitted carries an amount" }
    return $problems
}

function Test-Program([string]$program) {
    $problems = @()
    if ($null -eq $program) { return @("Payroll.Web/Program.cs is missing") }
    if ($program -notmatch 'AddOrgScopeFilter<PayrollDbContext>\(' -or $program -notmatch 'PayrollOrgScopeKeys\.Configure') { $problems += "Payroll.Web does not register the org-scope filter with Payroll's keys" }
    if ($program -notmatch 'AddSnapshotOrgScopeProvider<PayrollDbContext>\(\)') { $problems += "Payroll.Web does not register the snapshot scope provider" }
    if ($program -notmatch 'AddHostedService<[\w\.]*PayProfileChangeActivationJob>\(\)') { $problems += "Payroll.Web does not register the activation job" }
    return $problems
}

# $callers: relative path -> text of every product .cs file that names the patch
function Test-RegimePatch([string]$orchestrator, [hashtable]$callers) {
    $problems = @()
    if ($null -eq $orchestrator -or $orchestrator -notmatch 'AcceptancePayrollRegimePatch\.ApplyAsync\(') { $problems += "AcceptanceSeedOrchestrator does not apply AcceptancePayrollRegimePatch" }
    foreach ($name in $callers.Keys) {
        if ($name -like "*AcceptanceSeedOrchestrator.cs" -or $name -like "*AcceptancePayrollRegimePatch.cs") { continue }
        $problems += "${name}: the acceptance regime patch is called outside the acceptance seed"
    }
    return $problems
}

function Test-Acceptance($scope, $ownership) {
    $problems = @()
    if ($null -eq $scope) { return @("tests/acceptance/ci-scope.json is missing") }
    $row = @($scope.prodLike | Where-Object { $_.class -eq "AcIam53SalaryChangeTests" })
    if ($row.Count -ne 1 -or $row[0].group -ne "main" -or -not $row[0].required) { $problems += "AcIam53SalaryChangeTests is not a required prod-like class of the main group" }
    # AC-12 is not in this list: PLAN-59 rewrote it and owns its place. PLAN-59 (P-11) moved AC-23 to the prod-like main group: the
    # payroll regression stays required wherever its class runs (lab[] or prodLike[]), once.
    foreach ($class in @("Ac23PayrollFreezeTests", "Ac24PartialDisbursementTests", "Ac25FraudHoldTests")) {
        $rows = @(@($scope.lab) + @($scope.prodLike) | Where-Object { $_.class -eq $class })
        if ($rows.Count -ne 1 -or -not $rows[0].required) { $problems += "$class is not a required acceptance class (the payroll regression of PLAN-53)" }
    }
    $entry = $null
    if ($null -ne $ownership) {
        $ac = $ownership.PSObject.Properties | Where-Object { $_.Name -eq "acs" -or $_.Name -eq "classes" -or $_.Name -eq "entries" } | Select-Object -First 1
        $entries = if ($null -ne $ac) { $ac.Value } else { $ownership }
        $entry = $entries.PSObject.Properties | Where-Object { $_.Name -eq "AC-IAM-53" } | Select-Object -First 1
    }
    if ($null -eq $entry -or $entry.Value.ownerPlan -ne "PLAN-53" -or -not $entry.Value.provesClaim) { $problems += "AC-IAM-53 is not owned by PLAN-53 with provesClaim" }
    return $problems
}

function Get-WriteLists {
    $directory = Join-Path $root $paths.WriteLists
    if (-not (Test-Path $directory)) { return @() }
    return @(Get-ChildItem -Path $directory -File -Filter *.txt | ForEach-Object { $_.Name })
}

function Get-ProductSources([string]$needle) {
    $sources = @{}
    foreach ($base in @("src/Services", "src/Shared", "src/Gateway")) {
        $directory = Join-Path $root $base
        if (-not (Test-Path $directory)) { continue }
        foreach ($file in Get-ChildItem -Path $directory -Recurse -File -Filter *.cs) {
            $full = $file.FullName.Replace('\', '/')
            if ($full -match '/(bin|obj)/') { continue }
            $text = [System.IO.File]::ReadAllText($file.FullName)
            if ($text.Contains($needle)) { $sources[$full.Substring($root.Replace('\', '/').Length + 1)] = $text }
        }
    }
    return $sources
}

function Get-Migration {
    $directory = Join-Path $root $paths.Migrations
    $file = Get-ChildItem -Path $directory -File -Filter "*_PAY053_SalaryChangeRequests.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $file) { return @{ Bytes = $null; Text = $null } }
    return @{ Bytes = [System.IO.File]::ReadAllBytes($file.FullName); Text = [System.IO.File]::ReadAllText($file.FullName) }
}

function Get-Contracts {
    $contracts = @{}
    foreach ($name in $events) { $contracts[$name] = Read-Text (Join-Path $paths.Contracts "$name.cs") }
    return $contracts
}

function Invoke-All {
    $all = @()
    $all += Test-Lists (Get-WriteLists) (Read-Text $paths.Owners)
    $all += Test-DirectWrite (Get-ProductSources "UpsertEmployeePayProfile")
    $all += Test-Settings "appsettings.json" (Get-Json (Read-Text $paths.Base))
    $all += Test-Settings "appsettings.Production.json" (Get-Json (Read-Text $paths.Production))
    $all += Test-Settings "appsettings.Staging.json" (Get-Json (Read-Text $paths.Staging))
    $all += Test-Mirror (Read-Text $paths.ProdLike)
    $migration = Get-Migration
    $all += Test-Migration $migration.Bytes $migration.Text
    $all += Test-Events (Get-Contracts) (Read-Text $paths.Publishers) (Read-Text $paths.Matrix) (Read-Text $paths.Schema)
    $all += Test-Program (Read-Text $paths.Program)
    $all += Test-RegimePatch (Read-Text $paths.Orchestrator) (Get-ProductSources "AcceptancePayrollRegimePatch.")
    $all += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    return @($all | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $owners = Read-Text $paths.Owners
    if ((Test-Lists @("payroll.txt") $owners).Count -eq 0) { $failures += "a write-policy list that came back was not refused" }
    if ((Test-Lists @() ($owners -replace 'Owners\s*=\s*\[', 'Owners = ["PLAN-53", ')).Count -eq 0) { $failures += "PLAN-53 back among the owners was not refused" }
    if ((Test-DirectWrite @{ "planted.cs" = "public record UpsertEmployeePayProfileCommand(Guid EmployeeId, decimal BaseSalary);" }).Count -eq 0) { $failures += "the direct write command back in the code was not refused" }
    $production = Get-Json (Read-Text $paths.Production)
    $production.Payroll.MasterData.RequireApproval = $false
    if ((Test-Settings "planted" $production).Count -eq 0) { $failures += "RequireApproval=false in Production was not refused" }
    $production = Get-Json (Read-Text $paths.Production)
    $production.Payroll.SalaryChange.AllowOutOfGradeBounds = $true
    if ((Test-Settings "planted" $production).Count -eq 0) { $failures += "AllowOutOfGradeBounds=true in Production was not refused" }
    $production = Get-Json (Read-Text $paths.Production)
    ($production.Authorization.Policies.PSObject.Properties | Where-Object { $_.Name -eq "Payroll.MasterData" }).Value.Roles = @("hr-admin", "payroll")
    if ((Test-Settings "planted" $production).Count -eq 0) { $failures += "the module role among the Payroll.MasterData roles was not refused" }
    $production = Get-Json (Read-Text $paths.Production)
    $production.Authorization.AdminExcludedPolicies = @($production.Authorization.AdminExcludedPolicies) + "Payroll.MasterData"
    if ((Test-Settings "planted" $production).Count -eq 0) { $failures += "Payroll.MasterData among the admin-excluded policies was not refused" }
    $compose = (Read-Text $paths.ProdLike) -replace 'Payroll__MasterData__RequireApproval:\s*"true"', 'Payroll__MasterData__RequireApproval: "false"'
    if ((Test-Mirror $compose).Count -eq 0) { $failures += "a prod-like mirror without the approval was not refused" }
    $migration = Get-Migration
    $bom = [byte[]](@(0xEF, 0xBB, 0xBF) + $migration.Bytes)
    if ((Test-Migration $bom $migration.Text).Count -eq 0) { $failures += "a migration with a byte order mark was not refused" }
    $setNull = [regex]::Replace($migration.Text, '(AddForeignKey\(\s*name:\s*"FK_EmployeePayProfiles_PayGrades_PayGradeId"[\s\S]*?onDelete:\s*ReferentialAction\.)Restrict', '${1}SetNull', 1)
    if ((Test-Migration $migration.Bytes $setNull).Count -eq 0) { $failures += "a migration that keeps SetNull on the grade was not refused" }
    if ((Test-Migration $migration.Bytes ($migration.Text -replace 'IN \(0, 1\)', '= 0')).Count -eq 0) { $failures += "an index that leaves scheduled requests out was not refused" }
    if ((Test-Migration $migration.Bytes ($migration.Text -replace 'EnableRls\(', 'Nothing(')).Count -eq 0) { $failures += "request tables without row security were not refused" }
    $contracts = Get-Contracts
    if ((Test-Events $contracts ((Read-Text $paths.Publishers) -replace 'IPayProfileChangeApplied', 'ISomethingElse') (Read-Text $paths.Matrix) (Read-Text $paths.Schema)).Count -eq 0) { $failures += "an event without a publisher row was not refused" }
    $withAmount = $contracts.Clone(); $withAmount["IPayProfileChangeSubmitted"] = $withAmount["IPayProfileChangeSubmitted"] + "`n    decimal ProposedBaseSalary { get; }"
    if ((Test-Events $withAmount (Read-Text $paths.Publishers) (Read-Text $paths.Matrix) (Read-Text $paths.Schema)).Count -eq 0) { $failures += "an amount in the submitted event was not refused" }
    if ((Test-Program ((Read-Text $paths.Program) -replace 'AddHostedService<[\w\.]*PayProfileChangeActivationJob>\(\)', 'Nothing()')).Count -eq 0) { $failures += "Payroll.Web without the activation job was not refused" }
    if ((Test-Program ((Read-Text $paths.Program) -replace 'AddOrgScopeFilter<PayrollDbContext>', 'AddNothing<PayrollDbContext>')).Count -eq 0) { $failures += "Payroll.Web without the org-scope filter was not refused" }
    if ((Test-RegimePatch ((Read-Text $paths.Orchestrator) -replace 'AcceptancePayrollRegimePatch\.ApplyAsync', 'Nothing') @{}).Count -eq 0) { $failures += "an acceptance seed without the regime patch was not refused" }
    if ((Test-RegimePatch (Read-Text $paths.Orchestrator) @{ "src/Services/HRCore/HR.Core.Infrastructure/Seeding/NationalSeedOrchestrator.cs" = "await AcceptancePayrollRegimePatch.ApplyAsync(db);" }).Count -eq 0) { $failures += "the regime patch called by the national seed was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $scope.prodLike = @($scope.prodLike | Where-Object { $_.class -ne "AcIam53SalaryChangeTests" })
    if ((Test-Acceptance $scope (Get-Json (Read-Text $paths.Ownership))).Count -eq 0) { $failures += "a scope without AC-IAM-53 was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    (@(@($scope.lab) + @($scope.prodLike)) | Where-Object { $_.class -eq "Ac23PayrollFreezeTests" }).required = $false
    if ((Test-Acceptance $scope (Get-Json (Read-Text $paths.Ownership))).Count -eq 0) { $failures += "AC-23 not required was not refused" }
    if ((Invoke-All).Count -ne 0) { $failures += "the repository itself does not pass" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] iam053-validate self-test: 20 planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] iam053-validate: no write-policy list, no direct salary write, the settings and their mirror, PAY053, the three events, the scope filter and the activation job, the regime patch, AC-IAM-53 and the payroll regression required" -ForegroundColor Green
exit 0
