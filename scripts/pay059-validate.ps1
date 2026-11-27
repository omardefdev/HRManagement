# PLAN-59 (PAY-D): static checks of the full payroll cycle's proof and of what guards it.
# ASCII only; Windows PowerShell 5.1 syntax (run here by pwsh 7).
#
#   pwsh -NoProfile -File scripts/pay059-validate.ps1 [-SelfTest]
#
# What it checks (AC-PAY-59, AC-12, AC-21..23, Pay059* and the registry's unit test prove the behaviour; this script guards the files a
# reviewer or an operator edits by hand):
#   1. The payroll acceptance scripts write no SQL (the plan's pattern UPDATE|INSERT|DELETE|Invoke-PayrollSql finds nothing in
#      scripts/acceptance-payroll-*.ps1), and none fixes a month: each takes it as a mandatory -PeriodStart.
#   2. AcPay59FullCycleTests and PayrollCycleDriver*.cs hold no SQL: no QueryAcceptancePostgres, no psql, no Invoke-*Sql, no
#      ClassifyRecordsAddedSinceTheSeedAsync, no SELECT/UPDATE/INSERT/DELETE statement (comments aside).
#   3. AcPay59FullCycleTests is required in prodLike[] group cycle; the stage prodLike:cycle is in order[], in its own stage job
#      prodlike-cycle, in the acceptance-stage matrix of ci.yml, and prodlike-final waits for it.
#   4. AC-12, AC-21 and AC-23 are required in prodLike[] group main, and AC-22 is in excluded[].
#   5. [RequiredAcceptance] marks AC-12, AC-21, AC-22, AC-23 and AC-PAY-59.
#   6. The months' registry (testdata/acceptance/payroll-months.json): its absolute months overlap nowhere on one stack.
#   7. The monthly-cycle runbook exists and names its fifteen steps (headings "### 1." to "### 15.").
#   8. BlockRunRefusal takes the blocked employees, not a count.
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
    Scripts = "scripts"
    Acceptance = "src/Tests/HRManagement.AcceptanceTests"
    Scope = "tests/acceptance/ci-scope.json"
    Workflow = ".github/workflows/ci.yml"
    Registry = "testdata/acceptance/payroll-months.json"
    Runbook = "docs/runbooks/payroll-monthly-cycle.md"
    Governance = "src/Services/Payroll/Payroll.Application/Services/PayrollRegimeGovernance.cs"
}
$required = @{
    "Ac12PayrollSodTests" = "AC-12"
    "Ac21PayrollDeterminismTests" = "AC-21"
    "Ac22PayrollResumeAfterKillTests" = "AC-22"
    "Ac23PayrollFreezeTests" = "AC-23"
    "AcPay59FullCycleTests" = "AC-PAY-59"
}

function Get-PayrollScripts {
    $dir = Join-Path $root $paths.Scripts
    return @(Get-ChildItem -Path $dir -File -Filter "acceptance-payroll-*.ps1" | ForEach-Object {
            @{ Path = "scripts/$($_.Name)"; Text = [System.IO.File]::ReadAllText($_.FullName) }
        })
}

function Get-CycleSources {
    $dir = Join-Path $root $paths.Acceptance
    return @(Get-ChildItem -Path $dir -File | Where-Object { $_.Name -eq "AcPay59FullCycleTests.cs" -or $_.Name -like "PayrollCycleDriver*.cs" } | ForEach-Object {
            @{ Path = "$($paths.Acceptance)/$($_.Name)"; Text = [System.IO.File]::ReadAllText($_.FullName) }
        })
}

# 1. scripts
function Test-Scripts($scripts) {
    $problems = @()
    if (@($scripts).Count -lt 2) { $problems += "expected the two payroll acceptance scripts, found $(@($scripts).Count)" }
    foreach ($script in $scripts) {
        $lines = $script.Text -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'UPDATE|INSERT|DELETE|Invoke-PayrollSql') { $problems += "$($script.Path):$($i + 1) writes SQL: $($lines[$i].Trim())" }
        }
        if ($script.Text -match "\[datetime\]\s*'20\d\d-\d\d-\d\d'" -or $script.Text -match "TIMESTAMPTZ\s*'20\d\d") {
            $problems += "$($script.Path) fixes a month"
        }
        if ($script.Text -notmatch '\[Parameter\(Mandatory\s*=\s*\$true\)\]\[string\]\$PeriodStart') {
            $problems += "$($script.Path) does not take its month as a mandatory -PeriodStart"
        }
    }
    return $problems
}

# 2. no SQL in the cycle
function Remove-Comments([string]$text) {
    return (($text -split "`n") | Where-Object { $_ -notmatch '^\s*//' }) -join "`n"
}

function Test-CycleSql($sources) {
    $problems = @()
    if (@($sources | Where-Object { $_.Path -like "*AcPay59FullCycleTests.cs" }).Count -ne 1) { $problems += "AcPay59FullCycleTests.cs is missing" }
    if (@($sources | Where-Object { $_.Path -like "*PayrollCycleDriver*.cs" }).Count -lt 1) { $problems += "PayrollCycleDriver.cs is missing" }
    foreach ($source in $sources) {
        $code = Remove-Comments $source.Text
        foreach ($pattern in @('QueryAcceptancePostgres', '\bpsql\b', 'Invoke-\w*Sql', 'ClassifyRecordsAddedSinceTheSeedAsync', '"\s*(SELECT|UPDATE|INSERT|DELETE)\s')) {
            if ($code -match $pattern) { $problems += "$($source.Path) holds SQL ($pattern)" }
        }
    }
    return $problems
}

# 3 and 4. scope and workflow
function Test-Scope($scope, [string]$workflow) {
    $problems = @()
    if ($null -eq $scope) { return @("ci-scope.json is missing") }
    $cycle = @($scope.prodLike | Where-Object { $_.class -eq "AcPay59FullCycleTests" })
    if ($cycle.Count -ne 1 -or -not $cycle[0].required -or $cycle[0].group -ne "cycle") { $problems += "AcPay59FullCycleTests is not a required prodLike class of the group cycle" }
    if (@($scope.order | Where-Object { $_.part -eq "prodLike" -and $_.group -eq "cycle" }).Count -ne 1) { $problems += "order[] has no prodLike:cycle stage" }
    $job = @($scope.stageJobs | Where-Object { $_.name -eq "prodlike-cycle" })
    if ($job.Count -ne 1 -or @($job[0].stages) -join "," -ne "prodLike:cycle") { $problems += "no stage job prodlike-cycle with the stage prodLike:cycle alone" }
    $final = @($scope.stageJobs | Where-Object { $_.name -eq "prodlike-final" })
    if ($final.Count -ne 1 -or @($final[0].waitFor) -notcontains "prodlike-cycle") { $problems += "prodlike-final does not wait for prodlike-cycle" }
    if ($null -eq $workflow -or $workflow -notmatch '(?m)^\s*- name: prodlike-cycle\s*\r?\n\s*stages: prodLike:cycle\s*\r?\n\s*timeout: \d+') {
        $problems += "the acceptance-stage matrix of ci.yml has no prodlike-cycle entry with its stage and its limit"
    }
    foreach ($class in @("Ac12PayrollSodTests", "Ac21PayrollDeterminismTests", "Ac23PayrollFreezeTests")) {
        $entry = @($scope.prodLike | Where-Object { $_.class -eq $class })
        if ($entry.Count -ne 1 -or -not $entry[0].required -or $entry[0].group -ne "main") { $problems += "$class is not a required prodLike class of the group main" }
        if (@($scope.lab | Where-Object { $_.class -eq $class }).Count -ne 0) { $problems += "$class is still in lab[]" }
    }
    if (@($scope.excluded | Where-Object { $_.class -eq "Ac22PayrollResumeAfterKillTests" }).Count -ne 1) { $problems += "Ac22PayrollResumeAfterKillTests is not in excluded[]" }
    return $problems
}

# 5. [RequiredAcceptance]
function Test-Required([hashtable]$texts) {
    $problems = @()
    foreach ($class in $required.Keys) {
        $text = $texts[$class]
        if ($null -eq $text) { $problems += "$class.cs is missing"; continue }
        if ($text -notmatch "\[RequiredAcceptance\]\s*\r?\npublic class $class\b") { $problems += "$class ($($required[$class])) is not marked [RequiredAcceptance]" }
    }
    return $problems
}

# 6. registry
function Get-Months([string]$from, [string]$to) {
    $months = @()
    $m = [datetime]::ParseExact($from + "-01", "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
    $end = [datetime]::ParseExact($to + "-01", "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
    while ($m -le $end) { $months += $m.ToString("yyyy-MM"); $m = $m.AddMonths(1) }
    return $months
}

function Test-Registry($registry) {
    if ($null -eq $registry) { return @("the months' registry is missing") }
    $taken = @{}
    $problems = @()
    foreach ($entry in $registry.entries) {
        $months = @()
        if ($entry.PSObject.Properties["from"]) { $months = Get-Months $entry.from $entry.to }
        elseif ($entry.PSObject.Properties["window"]) { $months = Get-Months $entry.window.from $entry.window.to }
        foreach ($stack in $entry.stacks) {
            foreach ($month in $months) {
                $key = "$stack $month"
                if ($taken.ContainsKey($key) -and $taken[$key] -ne $entry.owner) { $problems += "the registry gives $key to $($taken[$key]) and $($entry.owner)" }
                else { $taken[$key] = $entry.owner }
            }
        }
    }
    return $problems
}

# 7. runbook
function Test-Runbook($text) {
    if ($null -eq $text) { return @("$($paths.Runbook) is missing") }
    $problems = @()
    foreach ($n in 1..15) {
        if ($text -notmatch "(?m)^### $n\. ") { $problems += "the runbook does not name step $n (a heading '### $n.')" }
    }
    return $problems
}

# 8. the refusal
function Test-Refusal($text) {
    if ($null -eq $text) { return @("PayrollRegimeGovernance.cs is missing") }
    $problems = @()
    if ($text -notmatch 'public static string BlockRunRefusal\(\s*IReadOnlyList<HrCorePayrollBlockerDto>') { $problems += "BlockRunRefusal does not take the blocked employees" }
    if ($text -match 'BlockRunRefusal\(\s*int\b') { $problems += "BlockRunRefusal still takes a count" }
    return $problems
}

function Get-ClassTexts {
    $texts = @{}
    foreach ($class in $required.Keys) { $texts[$class] = Read-Text "$($paths.Acceptance)/$class.cs" }
    return $texts
}

function Invoke-All {
    $problems = @()
    $problems += Test-Scripts (Get-PayrollScripts)
    $problems += Test-CycleSql (Get-CycleSources)
    $problems += Test-Scope (Get-Json (Read-Text $paths.Scope)) (Read-Text $paths.Workflow)
    $problems += Test-Required (Get-ClassTexts)
    $problems += Test-Registry (Get-Json (Read-Text $paths.Registry))
    $problems += Test-Runbook (Read-Text $paths.Runbook)
    $problems += Test-Refusal (Read-Text $paths.Governance)
    return @($problems | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $planted = 0
    function Copy-With($sources, [string]$path, [string]$text) {
        $copy = @()
        $found = $false
        foreach ($source in $sources) {
            if ($source.Path -eq $path) { $copy += @{ Path = $path; Text = $text }; $found = $true } else { $copy += $source }
        }
        if (-not $found) { $copy += @{ Path = $path; Text = $text } }
        return $copy
    }

    # 1. scripts
    $scripts = Get-PayrollScripts
    $first = $scripts[0]
    $planted++; if ((Test-Scripts (Copy-With $scripts $first.Path ($first.Text + "`nInvoke-PayrollSql 'x'"))).Count -eq 0) { $failures += "an Invoke-PayrollSql call was not refused" }
    $planted++; if ((Test-Scripts (Copy-With $scripts $first.Path ($first.Text + "`n`$q = 'UPDATE ""PayrollRuns"" SET ""Status"" = 8'"))).Count -eq 0) { $failures += "an UPDATE was not refused" }
    $planted++; if ((Test-Scripts (Copy-With $scripts $first.Path ($first.Text + "`n`$p = [datetime]'2027-01-01'"))).Count -eq 0) { $failures += "a fixed month was not refused" }
    $optional = $first.Text -replace '\[Parameter\(Mandatory = \$true\)\]\[string\]\$PeriodStart', '[string]$PeriodStart = "2027-01-01"'
    $planted++; if ($optional -eq $first.Text -or (Test-Scripts (Copy-With $scripts $first.Path $optional)).Count -eq 0) { $failures += "a script with an optional month was not refused" }

    # 2. the cycle's SQL
    $sources = Get-CycleSources
    $driver = @($sources | Where-Object { $_.Path -like "*PayrollCycleDriver.cs" })[0]
    foreach ($sql in @('var t = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db", q);', 'RunProcessAsync("docker", "exec", c, "psql");',
            'await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(t);', 'var q = "SELECT count(*) FROM x";')) {
        $planted++; if ((Test-CycleSql (Copy-With $sources $driver.Path ($driver.Text + "`nclass Planted { void M() { $sql } }"))).Count -eq 0) { $failures += "SQL in the driver was not refused: $sql" }
    }
    $planted++; if ((Test-CycleSql (Copy-With $sources $driver.Path ($driver.Text + "`n// no psql, no QueryAcceptancePostgres here"))).Count -ne 0) { $failures += "a comment was taken for SQL" }
    $planted++; if ((Test-CycleSql @($sources | Where-Object { $_.Path -notlike "*AcPay59FullCycleTests.cs" })).Count -eq 0) { $failures += "a missing cycle class was not noticed" }

    # 3 and 4. scope
    $workflow = Read-Text $paths.Workflow
    $scope = Get-Json (Read-Text $paths.Scope); ($scope.prodLike | Where-Object { $_.class -eq "AcPay59FullCycleTests" }).required = $false
    $planted++; if ((Test-Scope $scope $workflow).Count -eq 0) { $failures += "AC-PAY-59 not required was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope); ($scope.prodLike | Where-Object { $_.class -eq "AcPay59FullCycleTests" }).group = "main"
    $planted++; if ((Test-Scope $scope $workflow).Count -eq 0) { $failures += "AC-PAY-59 in the main group was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope); ($scope.stageJobs | Where-Object { $_.name -eq "prodlike-final" }).waitFor = @("lab-main", "lab-rest", "prodlike")
    $planted++; if ((Test-Scope $scope $workflow).Count -eq 0) { $failures += "prodlike-final not waiting for the cycle was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope); $scope.stageJobs = @($scope.stageJobs | Where-Object { $_.name -ne "prodlike-cycle" })
    $planted++; if ((Test-Scope $scope $workflow).Count -eq 0) { $failures += "no prodlike-cycle stage job was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $planted++; if ((Test-Scope $scope ($workflow -replace 'name: prodlike-cycle', 'name: prodlike-other')).Count -eq 0) { $failures += "a workflow without prodlike-cycle was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope); ($scope.prodLike | Where-Object { $_.class -eq "Ac21PayrollDeterminismTests" }).required = $false
    $planted++; if ((Test-Scope $scope $workflow).Count -eq 0) { $failures += "AC-21 not required was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope); $scope.excluded = @($scope.excluded | Where-Object { $_.class -ne "Ac22PayrollResumeAfterKillTests" })
    $planted++; if ((Test-Scope $scope $workflow).Count -eq 0) { $failures += "AC-22 out of excluded[] was not refused" }

    # 5. [RequiredAcceptance]
    $texts = Get-ClassTexts
    $texts["Ac12PayrollSodTests"] = $texts["Ac12PayrollSodTests"] -replace '\[RequiredAcceptance\]\s*\r?\n', ''
    $planted++; if ((Test-Required $texts).Count -eq 0) { $failures += "AC-12 without [RequiredAcceptance] was not refused" }

    # 6. registry
    $registry = Get-Json (Read-Text $paths.Registry)
    ($registry.entries | Where-Object { $_.owner -eq "AC-21" }).window.to = "2024-07"
    $planted++; if ((Test-Registry $registry).Count -eq 0) { $failures += "an overlap of AC-21 and AC-22 was not refused" }
    $registry = Get-Json (Read-Text $paths.Registry)
    ($registry.entries | Where-Object { $_.owner -eq "AC-PAY-56" }).from = "2027-12"
    ($registry.entries | Where-Object { $_.owner -eq "AC-PAY-56" }).to = "2028-01"
    $planted++; if ((Test-Registry $registry).Count -ne 0) { $failures += "a registry with no overlap was refused" }
    ($registry.entries | Where-Object { $_.owner -eq "AC-PAY-56" }).from = "2025-07"
    $planted++; if ((Test-Registry $registry).Count -eq 0) { $failures += "an overlap with AC-LVA-57 was not refused" }

    # 7. runbook
    $runbook = Read-Text $paths.Runbook
    $planted++; if ((Test-Runbook $null).Count -eq 0) { $failures += "a missing runbook was not refused" }
    if ($null -ne $runbook) {
        $planted++; if ((Test-Runbook ($runbook -replace '(?m)^### 13\. ', '### Thirteen. ')).Count -eq 0) { $failures += "a runbook without step 13 was not refused" }
    }

    # 8. refusal
    $governance = Read-Text $paths.Governance
    $planted++; if ((Test-Refusal 'public static string BlockRunRefusal(int count) => "";').Count -eq 0) { $failures += "a refusal that takes a count was not refused" }
    $planted++; if ((Test-Refusal $governance).Count -ne 0) { $failures += "the product's refusal was refused" }

    $own = Invoke-All
    if ($own.Count -ne 0) { $failures += "the repository itself does not pass: $($own -join '; ')" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] pay059-validate self-test: $planted planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] pay059-validate: the payroll acceptance scripts write no SQL and take their month from their class, AC-PAY-59 and its driver hold no SQL, AC-PAY-59 is required on its own prod-like stage job that prodlike-final waits for, AC-12, AC-21 and AC-23 are required in the prod-like main group and AC-22 is excluded, the five classes are [RequiredAcceptance], the months' registry overlaps nowhere, the runbook names its fifteen steps, and BlockRun's refusal takes the employees" -ForegroundColor Green
exit 0
