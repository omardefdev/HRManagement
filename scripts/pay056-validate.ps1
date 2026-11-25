# PLAN-56 (PAY-C): static checks of the payroll run's state machine and of what guards it.
# ASCII only; Windows PowerShell 5.1 syntax (run here by pwsh 7).
#
#   pwsh -NoProfile -File scripts/pay056-validate.ps1 [-SelfTest]
#
# What it checks (Pay056* tests, Pay056NetComputationArchitectureTests, PayrollWriteActionsTests and AC-PAY-56 prove the behaviour;
# this script guards the files a reviewer or an operator edits by hand):
#   1. No code of Payroll, and no shared code that carries a line's figures, computes a net beside PayrollNet (a subtraction from a
#      gross, or of the total deductions), and a line's net is written only by the three named writers.
#   2. A run's status is written in PayrollRun.cs only (the init accessor and Move, which goes through PayrollRunTransitions); a run
#      is constructed only by the two creators of a draft and the two seeders; no bulk statement updates the runs; the test helper
#      that sets a status is not referenced by product code.
#   3. No batch of kind Reversal is written, and nothing names ReversePayrollRunCommand or AddSalaryLine any more (the direct
#      reversal and the manual salary line are gone).
#   4. The two migrations (PAY056_NetFormula, PAY056_RunLifecycle) and their designers have no byte order mark; the first lifts FORCE
#      on the components and the lines before its fill and puts it back after, and the fill sets TotalAdjustments only; the second
#      puts row security on the two new tables.
#   5. Every write action of the run's controllers validates an antiforgery token, and so does the older cancel-by-job action of
#      PayrollApiController, which cancels the run since this plan.
#   6. docs/EVENT_PUBLISHER_INVENTORY.md has the IPayrollReversed row with its publisher.
#   7. AcPay56RunStateMachineTests is a required prod-like class of the main group, and AC-PAY-56 is owned by PLAN-56.
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
    return ($text.TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

$paths = @{
    Services = "src/Services"
    Shared = "src/Shared"
    Payroll = "src/Services/Payroll"
    Net = "src/Services/Payroll/Payroll.Domain/Services/PayrollNet.cs"
    Run = "src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs"
    Api = "src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs"
    Migrations = "src/Services/Payroll/Payroll.Infrastructure/Migrations"
    Inventory = "docs/EVENT_PUBLISHER_INVENTORY.md"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
}
# The folders whose code carries a salary line's figures (as Pay056NetComputationArchitectureTests reads them).
$netRoots = @(
    "src/Services/Payroll/",
    "src/Shared/HRManagement.Documents.QuestPdf/",
    "src/Shared/HRManagement.Integration.Abstractions/Payroll/",
    "src/Shared/HRManagement.Integration.FileAdapters/Payroll/"
)
# A "total deductions" that is not a salary line's: the end-of-service settlement (PLAN-64).
$notALinesNet = @("src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs")
$netWriters = @(
    "src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs",
    "src/Services/Payroll/Payroll.Domain/Services/PayrollCalculation.cs",
    "src/Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs"
)
$runConstructors = @(
    "src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs",
    "src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs",
    "src/Services/Payroll/Payroll.Infrastructure/Data/AcceptancePayrollSeeder.cs",
    "src/Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs"
)
$controllers = @(
    "src/Services/Payroll/Payroll.Web/Controllers/PayrollRunLifecycleApiController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PayrollReversalRequestsController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PayrollRunsController.cs"
)
$tenantTables = @("PayrollRunRevisions", "PayrollReversalRequests")

# Source files of a folder (C# and Razor), as @{ Path = relative path; Text = content }, without build outputs and migrations.
function Get-Sources([string]$relative) {
    $directory = Join-Path $root $relative
    $files = @()
    if (-not (Test-Path $directory)) { return $files }
    foreach ($file in (Get-ChildItem -Path $directory -Recurse -File -Include *.cs, *.cshtml)) {
        $path = $file.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
        if ($path -match '/(bin|obj|Migrations)/') { continue }
        $files += @{ Path = $path; Text = [System.IO.File]::ReadAllText($file.FullName) }
    }
    return $files
}

# A line of source without its comment, so that prose about a formula is not read as one.
function Get-Code([string]$line) {
    $trimmed = $line.TrimStart()
    if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("*") -or $trimmed.StartsWith("/*")) { return "" }
    $comment = $line.IndexOf("//")
    if ($comment -ge 0) { return $line.Substring(0, $comment) }
    return $line
}

$ignoreCase = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
$fromAGross = New-Object System.Text.RegularExpressions.Regex('\b\w*gross\w*\s*\)?\s*-(?![-=>])', $ignoreCase)
$ofTheDeductions = New-Object System.Text.RegularExpressions.Regex('(?<![-=(,])\s-\s*[\w.]*totaldeductions\b', $ignoreCase)
$netAssignment = New-Object System.Text.RegularExpressions.Regex('(?<![\w])NetPay\s*=(?!=)')

# 1. $sources: the product sources (src/Services and src/Shared).
function Test-Net($sources) {
    $problems = @()
    $scoped = @($sources | Where-Object { $p = $_.Path; @($netRoots | Where-Object { $p.StartsWith($_) }).Count -gt 0 })
    if ($scoped.Count -eq 0) { return @("no source file was read under the Payroll folders: the check reads the wrong folder") }
    $one = @($scoped | Where-Object { $_.Path -eq $paths.Net }) | Select-Object -First 1
    if ($null -eq $one) { return @("PayrollNet.cs is missing: the one definition of the net") }
    if (-not $fromAGross.IsMatch($one.Text)) { $problems += "PayrollNet.cs no longer subtracts from a gross: the check no longer sees the formula" }
    $writers = @()
    foreach ($source in $scoped) {
        $lines = $source.Text -split "`n"
        $writes = $false
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $code = Get-Code $lines[$i]
            if ($code.Length -eq 0) { continue }
            if ($netAssignment.IsMatch($code)) { $writes = $true }
            if ($source.Path -eq $paths.Net) { continue }
            if ($fromAGross.IsMatch($code) -or ($ofTheDeductions.IsMatch($code) -and ($notALinesNet -notcontains $source.Path))) {
                $problems += "$($source.Path):$($i + 1) computes a net beside PayrollNet: $($lines[$i].Trim())"
            }
        }
        if ($writes) { $writers += $source.Path }
    }
    foreach ($writer in $writers) {
        if ($netWriters -notcontains $writer) { $problems += "$writer writes a line's NetPay: only the engine's rounding rule, the legacy builder and the seed do, each from PayrollNet" }
    }
    foreach ($writer in $netWriters) {
        $file = @($scoped | Where-Object { $_.Path -eq $writer }) | Select-Object -First 1
        if ($null -eq $file) { $problems += "$writer is missing" }
        elseif ($file.Text -notmatch 'PayrollNet\.Net\(') { $problems += "$writer does not take its net from PayrollNet.Net" }
    }
    return $problems
}

# 2. $sources: the product sources.
function Test-Status($sources) {
    $problems = @()
    $run = @($sources | Where-Object { $_.Path -eq $paths.Run }) | Select-Object -First 1
    if ($null -eq $run) { return @("PayrollRun.cs is missing") }
    $property = [regex]::Match($run.Text, '(?s)public\s+PayrollRunStatus\s+Status\s*\{(.*?)\n    \}')
    if (-not $property.Success) { $problems += "PayrollRun.Status is not the guarded property (get, init) any more" }
    elseif ($property.Groups[1].Value -match '\bset\b') { $problems += "PayrollRun.Status has a setter: a status would be assigned around the transitions" }
    $assignments = [regex]::Matches($run.Text, '\b_status\s*=(?!=)')
    $move = [regex]::Match($run.Text, 'private\s+void\s+Move\(PayrollRunAct\s+\w+\)\s*=>\s*_status\s*=\s*PayrollRunTransitions\.EnsureTransition\(')
    if (-not $move.Success) { $problems += "PayrollRun.Move no longer goes through PayrollRunTransitions.EnsureTransition" }
    if ($assignments.Count -ne 2) { $problems += "PayrollRun.cs assigns _status $($assignments.Count) times: only the init accessor and Move may" }
    $constructed = 0
    foreach ($source in $sources) {
        if ($source.Path -ne $paths.Run -and $source.Text -match '\b_status\b' -and $source.Path.StartsWith($paths.Payroll + "/Payroll.Domain/Entities/PayrollRun")) {
            $problems += "$($source.Path) names the run's status field"
        }
        $creations = [regex]::Matches($source.Text, 'new\s+PayrollRun\b')
        if ($creations.Count -gt 0) {
            $constructed += $creations.Count
            if ($runConstructors -notcontains $source.Path) { $problems += "$($source.Path) constructs a payroll run: only the two creators of a draft and the two seeders do" }
        }
        if ($source.Text -match '(?s)\bPayrollRuns\b[^;]{0,400}?\.ExecuteUpdate(Async)?\(' -or $source.Text -match 'UPDATE\s+\\?"PayrollRuns\\?"') {
            $problems += "$($source.Path) updates the runs in bulk: a status would move with no guard"
        }
        if ($source.Text -match '\bPayrollRunForTesting\b|\bForTesting\.SetStatus\b') { $problems += "$($source.Path) uses the test helper that sets a run's status" }
        foreach ($creator in @($runConstructors[0], $runConstructors[1])) {
            if ($source.Path -ne $creator) { continue }
            foreach ($status in [regex]::Matches($source.Text, '(?<![=!<>\w.])Status\s*=\s*PayrollRunStatus\.(\w+)')) {
                if ($status.Groups[1].Value -ne "Draft") { $problems += "$($source.Path) creates a run as $($status.Groups[1].Value): a new run is a draft" }
            }
        }
    }
    if ($constructed -eq 0) { $problems += "no 'new PayrollRun' was found at all: the check reads the wrong folder" }
    return $problems
}

# 3. $sources: the product sources; $views: the names of the Razor files of Payroll.Web.
function Test-Removed($sources, $views) {
    $problems = @()
    if ($null -eq $sources -or $sources.Count -eq 0) { return @("no source file was read: the check reads the wrong folder") }
    foreach ($source in $sources) {
        if ($source.Text -match '\bBatchKind\s*=\s*PaymentBatchKind\.Reversal\b' -or $source.Text -match 'new\s+PaymentBatch\b[^;]{0,600}PaymentBatchKind\.Reversal') {
            $problems += "$($source.Path) writes a batch of kind Reversal: a reversal voids a batch that has not left and is refused for one that has (D-PAY-16)"
        }
        if ($source.Text -match '\bReversePayrollRunCommand\b') { $problems += "$($source.Path) names ReversePayrollRunCommand: a reversal is a request and its decision" }
        if ($source.Text -match '\bAddSalaryLine\b') { $problems += "$($source.Path) names AddSalaryLine: the manual salary line is removed" }
    }
    foreach ($view in $views) {
        if ($view -match 'AddSalaryLine') { $problems += "the view $view is back: the manual salary line is removed" }
    }
    return $problems
}

function Test-NoBom([string]$name, [byte[]]$bytes) {
    if ($null -eq $bytes) { return @("$name is missing") }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return @("$name starts with a byte order mark") }
    return @()
}

# 4a. The first migration's text.
function Test-NetMigration([string]$text) {
    if ($null -eq $text) { return @("migration PAY056_NetFormula is missing") }
    $problems = @()
    $up = [regex]::Match($text, '(?s)protected override void Up\(MigrationBuilder migrationBuilder\)(.*?)protected override void Down\(')
    if (-not $up.Success) { return @("PAY056_NetFormula has no Up method") }
    $body = $up.Groups[1].Value
    if ($text -notmatch 'LinesTable\s*=\s*"SalaryLines"' -or $text -notmatch 'ComponentsTable\s*=\s*"SalaryLineComponents"') {
        $problems += "PAY056_NetFormula does not name the lines and the components as the tables of its fill"
    }
    $fill = [regex]::Match($body, 'migrationBuilder\.Sql\(FillTotalAdjustmentsSql\)')
    if (-not $fill.Success) { return $problems + "PAY056_NetFormula has no fill (FillTotalAdjustmentsSql)" }
    foreach ($table in @("LinesTable", "ComponentsTable")) {
        $lift = [regex]::Match($body, 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{' + $table + '\}" NO FORCE ROW LEVEL SECURITY;"""\)')
        $restore = [regex]::Match($body, 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{' + $table + '\}" FORCE ROW LEVEL SECURITY;"""\)')
        if (-not $lift.Success -or $lift.Index -gt $fill.Index) { $problems += "PAY056_NetFormula does not lift FORCE on $table before its fill: as the migrator it would read no row and say nothing" }
        if (-not $restore.Success -or $restore.Index -lt $fill.Index) { $problems += "PAY056_NetFormula does not put FORCE back on $table after its fill" }
    }
    $sql = [regex]::Match($text, '(?s)FillTotalAdjustmentsSql\s*=\s*"""(.*?)"""')
    if (-not $sql.Success) { $problems += "PAY056_NetFormula's fill is not a literal the check can read" }
    else {
        $set = [regex]::Match($sql.Groups[1].Value, '(?s)\bSET\b(.*?)\bFROM\b')
        $columns = @([regex]::Matches($set.Groups[1].Value, '"(\w+)"\s*=') | ForEach-Object { $_.Groups[1].Value })
        if (-not $set.Success -or $columns.Count -ne 1 -or $columns[0] -ne "TotalAdjustments") {
            $problems += "PAY056_NetFormula's fill sets something other than TotalAdjustments: a stored amount of an existing line never changes"
        }
        if ($sql.Groups[1].Value -notmatch "'Adjustment'") { $problems += "PAY056_NetFormula's fill does not read the Adjustment components" }
    }
    return $problems
}

# 4b. The second migration's text.
function Test-LifecycleMigration([string]$text) {
    if ($null -eq $text) { return @("migration PAY056_RunLifecycle is missing") }
    $problems = @()
    $up = [regex]::Match($text, '(?s)protected override void Up\(MigrationBuilder migrationBuilder\)(.*?)protected override void Down\(')
    if (-not $up.Success) { return @("PAY056_RunLifecycle has no Up method") }
    if ($up.Groups[1].Value -notmatch '(?s)foreach\s*\(var\s+table\s+in\s+NewTenantTables\)\s*TenantRlsMigrationHelper\.EnableRls\(\s*migrationBuilder\s*,\s*table\s*\)') {
        $problems += "PAY056_RunLifecycle does not put row security on its new tables"
    }
    $list = [regex]::Match($text, '(?s)NewTenantTables\s*=\s*(.*?);')
    foreach ($table in $tenantTables) {
        if (-not $list.Success -or $list.Groups[1].Value -notmatch ('"' + $table + '"')) { $problems += "PAY056_RunLifecycle does not put row security on $table" }
    }
    return $problems
}

# 5. A controller's text: every write action (alone, with a route, or with other attributes in the same brackets) validates the token.
function Test-Antiforgery([string]$name, [string]$controller) {
    if ($null -eq $controller) { return @("$name is missing") }
    $problems = @()
    $writes = [regex]::Matches($controller, '(?m)^\s*\[[^\]\r\n]*\bHttp(Post|Put|Patch|Delete)\b[^\]\r\n]*\]\s*$')
    if ($writes.Count -eq 0) { $problems += "$name has no write action: the check reads the wrong file" }
    foreach ($write in $writes) {
        # The attributes of one action: from the verb to the action's signature.
        $rest = $controller.Substring($write.Index)
        $signature = [regex]::Match($rest, '(?m)^\s*public\s')
        $block = if ($signature.Success) { $rest.Substring(0, $signature.Index) } else { $rest }
        if ($block -notmatch '\bValidateAntiForgeryToken\b') { $problems += "${name}: $($write.Value.Trim()) has no [ValidateAntiForgeryToken]" }
    }
    return $problems
}

# 5b. PayrollApiController's text: the cancel-by-job action (its older POSTs are another plan's; this one changed here).
function Test-JobCancel([string]$controller) {
    if ($null -eq $controller) { return @("PayrollApiController.cs is missing") }
    $action = [regex]::Match($controller, '(?s)\[HttpPost\("jobs/\{jobId:guid\}/cancel"\)\](.*?)public\s')
    if (-not $action.Success) { return @("PayrollApiController has no POST jobs/{jobId}/cancel: the check reads the wrong file") }
    if ($action.Groups[1].Value -notmatch '\bValidateAntiForgeryToken\b') { return @("PayrollApiController: POST jobs/{jobId}/cancel cancels a run and has no [ValidateAntiForgeryToken]") }
    return @()
}

# 6.
function Test-Inventory([string]$inventory) {
    if ($null -eq $inventory) { return @("docs/EVENT_PUBLISHER_INVENTORY.md is missing") }
    $row = [regex]::Match($inventory, '(?m)^\|\s*`IPayrollReversed`\s*\|(.*)$')
    if (-not $row.Success) { return @("EVENT_PUBLISHER_INVENTORY.md has no IPayrollReversed row") }
    if ($row.Groups[1].Value -notmatch 'PublishPayrollReversedAsync') { return @("the IPayrollReversed row does not name its publisher (PublishPayrollReversedAsync)") }
    return @()
}

# 7.
function Test-Acceptance($scope, $ownership) {
    $problems = @()
    if ($null -eq $scope) { return @("tests/acceptance/ci-scope.json is missing") }
    $row = @($scope.prodLike | Where-Object { $_.class -eq "AcPay56RunStateMachineTests" })
    if ($row.Count -ne 1 -or $row[0].group -ne "main" -or -not $row[0].required) { $problems += "AcPay56RunStateMachineTests is not a required prod-like class of the main group" }
    $entry = $null
    if ($null -ne $ownership) {
        $ac = $ownership.PSObject.Properties | Where-Object { $_.Name -eq "acs" -or $_.Name -eq "classes" -or $_.Name -eq "entries" } | Select-Object -First 1
        $entries = if ($null -ne $ac) { $ac.Value } else { $ownership }
        $entry = $entries.PSObject.Properties | Where-Object { $_.Name -eq "AC-PAY-56" } | Select-Object -First 1
    }
    if ($null -eq $entry -or $entry.Value.ownerPlan -ne "PLAN-56" -or -not $entry.Value.provesClaim) { $problems += "AC-PAY-56 is not owned by PLAN-56 with provesClaim" }
    return $problems
}

function Get-MigrationFile([string]$suffix) {
    $directory = Join-Path $root $paths.Migrations
    $file = Get-ChildItem -Path $directory -File -Filter ("*_" + $suffix) -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $file) { return @{ Name = $suffix; Bytes = $null; Text = $null } }
    return @{ Name = $file.Name; Bytes = [System.IO.File]::ReadAllBytes($file.FullName); Text = [System.IO.File]::ReadAllText($file.FullName) }
}

function Get-Views {
    $directory = Join-Path $root "src/Services/Payroll/Payroll.Web/Views"
    if (-not (Test-Path $directory)) { return @() }
    return @(Get-ChildItem -Path $directory -Recurse -File -Filter *.cshtml | ForEach-Object { $_.Name })
}

function Get-Product {
    return @((Get-Sources $paths.Services) + (Get-Sources $paths.Shared))
}

function Invoke-All {
    $all = @()
    $product = Get-Product
    $all += Test-Net $product
    $all += Test-Status $product
    $all += Test-Removed $product (Get-Views)
    foreach ($suffix in @("PAY056_NetFormula.cs", "PAY056_NetFormula.Designer.cs", "PAY056_RunLifecycle.cs", "PAY056_RunLifecycle.Designer.cs")) {
        $file = Get-MigrationFile $suffix
        $all += Test-NoBom $file.Name $file.Bytes
    }
    $all += Test-NetMigration (Get-MigrationFile "PAY056_NetFormula.cs").Text
    $all += Test-LifecycleMigration (Get-MigrationFile "PAY056_RunLifecycle.cs").Text
    foreach ($controller in $controllers) { $all += Test-Antiforgery (Split-Path -Leaf $controller) (Read-Text $controller) }
    $all += Test-JobCancel (Read-Text $paths.Api)
    $all += Test-Inventory (Read-Text $paths.Inventory)
    $all += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    return @($all | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $planted = 0
    $product = Get-Product

    # A copy of the sources with one file's text changed, or one file added.
    function Copy-Sources($sources, [string]$path, [string]$text) {
        $copy = @()
        $found = $false
        foreach ($source in $sources) {
            if ($source.Path -eq $path) { $copy += @{ Path = $path; Text = $text }; $found = $true } else { $copy += $source }
        }
        if (-not $found) { $copy += @{ Path = $path; Text = $text } }
        return $copy
    }

    # 1. the net
    $handler = "class Planted { decimal Net(SalaryLine line) => line.GrossPay - line.TotalDeductions; }"
    $planted++; if ((Test-Net (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Planted.cs" $handler)).Count -eq 0) { $failures += "a net computed in a handler was not refused" }
    $view = '<td>@((l.GrossPay - l.TotalDeductions).ToString("N2"))</td>'
    $planted++; if ((Test-Net (Copy-Sources $product "src/Services/Payroll/Payroll.Web/Views/Planted.cshtml" $view)).Count -eq 0) { $failures += "a net computed in a view was not refused" }
    $pdf = "class Planted { decimal Net(decimal allowances, decimal totalDeductions) => allowances - totalDeductions; }"
    $planted++; if ((Test-Net (Copy-Sources $product "src/Shared/HRManagement.Documents.QuestPdf/Planted.cs" $pdf)).Count -eq 0) { $failures += "a net computed in the payslip document was not refused" }
    $writer = "class Planted { void Post(SalaryLine line, decimal net) { line.NetPay = net; } }"
    $planted++; if ((Test-Net (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Planted.cs" $writer)).Count -eq 0) { $failures += "a posting that writes a line's net was not refused" }
    $comment = "class Planted { // the old formula was gross - deductions`n }"
    $planted++; if ((Test-Net (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Planted.cs" $comment)).Count -ne 0) { $failures += "a comment about the old formula was taken for a computation" }
    $rules = Read-Text $netWriters[0]
    $ownFormula = $rules -replace 'PayrollNet\.Net\(', 'OwnNet.Compute('
    $planted++; if ($ownFormula -eq $rules -or (Test-Net (Copy-Sources $product $netWriters[0] $ownFormula)).Count -eq 0) { $failures += "an engine that does not take its net from PayrollNet was not refused" }

    # 2. the status
    $run = Read-Text $paths.Run
    $withSetter = [regex]::Replace($run, 'init => _status = value;', 'set => _status = value;', 1)
    $planted++; if ($withSetter -eq $run -or (Test-Status (Copy-Sources $product $paths.Run $withSetter)).Count -eq 0) { $failures += "a setter on PayrollRun.Status was not refused" }
    $aroundTheGuard = [regex]::Replace($run, '(public void MarkCalculated\(decimal totalNetPay\)\s*\{)', '$1 _status = PayrollRunStatus.Calculated;', 1)
    $planted++; if ($aroundTheGuard -eq $run -or (Test-Status (Copy-Sources $product $paths.Run $aroundTheGuard)).Count -eq 0) { $failures += "a status assigned around Move was not refused" }
    $noGuard = $run -replace '_status = PayrollRunTransitions\.EnsureTransition\(act, _status\);', '_status = Next(act);'
    $planted++; if ($noGuard -eq $run -or (Test-Status (Copy-Sources $product $paths.Run $noGuard)).Count -eq 0) { $failures += "a Move that does not go through the transitions was not refused" }
    $constructor = "class Planted { PayrollRun Make() => new PayrollRun { Status = PayrollRunStatus.Posted }; }"
    $planted++; if ((Test-Status (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Features/Planted.cs" $constructor)).Count -eq 0) { $failures += "a run constructed as posted in a handler was not refused" }
    $job = "class Planted { object Make() => new PayrollRunJob(); }"
    $planted++; if ((Test-Status (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Features/Planted.cs" $job)).Count -ne 0) { $failures += "a run job was taken for a run" }
    $bulk = "class Planted { Task Go(PayrollDbContext db) => db.PayrollRuns.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.TotalNetPay, 0m)); }"
    $planted++; if ((Test-Status (Copy-Sources $product "src/Services/Payroll/Payroll.Infrastructure/Planted.cs" $bulk)).Count -eq 0) { $failures += "a bulk update of the runs was not refused" }
    $sql = 'class Planted { string Sql = "UPDATE \"PayrollRuns\" SET \"Status\" = 1"; }'
    $planted++; if ((Test-Status (Copy-Sources $product "src/Services/Payroll/Payroll.Infrastructure/Planted.cs" $sql)).Count -eq 0) { $failures += "a raw UPDATE of the runs was not refused" }
    $helper = "class Planted { void Go(PayrollRun run) => ForTesting.SetStatus(run, PayrollRunStatus.Approved); }"
    $planted++; if ((Test-Status (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Planted.cs" $helper)).Count -eq 0) { $failures += "product code that uses the test helper was not refused" }
    $creator = Read-Text $runConstructors[0]
    $notADraft = [regex]::Replace($creator, 'Status = PayrollRunStatus\.Draft,', 'Status = PayrollRunStatus.Calculated,', 1)
    $planted++; if ($notADraft -eq $creator -or (Test-Status (Copy-Sources $product $runConstructors[0] $notADraft)).Count -eq 0) { $failures += "a run created as calculated was not refused" }

    # 3. what was removed
    $views = Get-Views
    $reversalBatch = "class Planted { PaymentBatch Make() => new PaymentBatch { BatchKind = PaymentBatchKind.Reversal }; }"
    $planted++; if ((Test-Removed (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Planted.cs" $reversalBatch) $views).Count -eq 0) { $failures += "a reversal batch was not refused" }
    $direct = "class Planted { object Go() => new ReversePayrollRunCommand(id); }"
    $planted++; if ((Test-Removed (Copy-Sources $product "src/Services/Payroll/Payroll.Web/Controllers/Planted.cs" $direct) $views).Count -eq 0) { $failures += "a direct reversal command was not refused" }
    $manual = "class Planted { IActionResult AddSalaryLine(Guid id) => null; }"
    $planted++; if ((Test-Removed (Copy-Sources $product "src/Services/Payroll/Payroll.Web/Controllers/Planted.cs" $manual) $views).Count -eq 0) { $failures += "a manual salary line action was not refused" }
    $planted++; if ((Test-Removed $product ($views + "AddSalaryLine.cshtml")).Count -eq 0) { $failures += "the manual salary line's view was not refused" }
    $reader = "class Planted { bool Is(PaymentBatch b) => b.BatchKind == PaymentBatchKind.Reversal; }"
    $planted++; if ((Test-Removed (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Planted.cs" $reader) $views).Count -ne 0) { $failures += "a reader of an old reversal batch was taken for a writer" }

    # 4. the migrations
    $net = Get-MigrationFile "PAY056_NetFormula.cs"
    $planted++; if ((Test-NoBom "planted" ([byte[]](@(0xEF, 0xBB, 0xBF) + $net.Bytes))).Count -eq 0) { $failures += "a migration with a byte order mark was not refused" }
    $planted++; if ((Test-NoBom "planted" $null).Count -eq 0) { $failures += "a missing migration was not noticed" }
    $noLift = $net.Text -replace 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{ComponentsTable\}" NO FORCE ROW LEVEL SECURITY;"""\);', ''
    $planted++; if ($noLift -eq $net.Text -or (Test-NetMigration $noLift).Count -eq 0) { $failures += "a fill that reads the components under forced row security was not refused" }
    $noLiftLines = $net.Text -replace 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{LinesTable\}" NO FORCE ROW LEVEL SECURITY;"""\);', ''
    $planted++; if ($noLiftLines -eq $net.Text -or (Test-NetMigration $noLiftLines).Count -eq 0) { $failures += "a fill that writes the lines under forced row security was not refused" }
    $noRestore = $net.Text -replace 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{LinesTable\}" FORCE ROW LEVEL SECURITY;"""\);', ''
    $planted++; if ($noRestore -eq $net.Text -or (Test-NetMigration $noRestore).Count -eq 0) { $failures += "a migration that leaves the lines unforced was not refused" }
    $rewritesNet = $net.Text -replace 'SET "TotalAdjustments" = filled\.total', 'SET "TotalAdjustments" = filled.total, "NetPay" = l."GrossPay" + filled.total'
    $planted++; if ($rewritesNet -eq $net.Text -or (Test-NetMigration $rewritesNet).Count -eq 0) { $failures += "a fill that rewrites a stored net was not refused" }
    $noFill = $net.Text -replace 'migrationBuilder\.Sql\(FillTotalAdjustmentsSql\);', ''
    $planted++; if ($noFill -eq $net.Text -or (Test-NetMigration $noFill).Count -eq 0) { $failures += "a migration without its fill was not refused" }
    $lifecycle = Get-MigrationFile "PAY056_RunLifecycle.cs"
    $noRls = $lifecycle.Text -replace 'TenantRlsMigrationHelper\.EnableRls\(', 'TenantRlsMigrationHelper.Nothing('
    $planted++; if ($noRls -eq $lifecycle.Text -or (Test-LifecycleMigration $noRls).Count -eq 0) { $failures += "new tables without row security were not refused" }
    $oneTable = $lifecycle.Text -replace '"PayrollReversalRequests"(\s*[,\]\}])', '"SomethingElse"$1'
    $planted++; if ($oneTable -eq $lifecycle.Text -or (Test-LifecycleMigration $oneTable).Count -eq 0) { $failures += "a reversal requests table without row security was not refused" }

    # 5. the antiforgery token
    $api = Read-Text $controllers[0]
    $apiWithout = [regex]::Replace($api, '(\[HttpPost\("runs/\{id:guid\}/cancel"\)\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($apiWithout -eq $api -or (Test-Antiforgery "planted" $apiWithout).Count -eq 0) { $failures += "a cancel action of the API without the antiforgery check was not refused" }
    $decide = Read-Text $controllers[1]
    $decideWithout = [regex]::Replace($decide, '(\[HttpPost\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($decideWithout -eq $decide -or (Test-Antiforgery "planted" $decideWithout).Count -eq 0) { $failures += "a decision form without the antiforgery check was not refused" }
    $mvc = Read-Text $controllers[2]
    $mvcWithout = [regex]::Replace($mvc, '(\[HttpPost\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($mvcWithout -eq $mvc -or (Test-Antiforgery "planted" $mvcWithout).Count -eq 0) { $failures += "a run action without the antiforgery check was not refused" }
    $planted++; if ((Test-Antiforgery "planted" "class Empty { }").Count -eq 0) { $failures += "a controller with no write action was not noticed" }
    $oldApi = Read-Text $paths.Api
    $jobCancelWithout = [regex]::Replace($oldApi, '(\[HttpPost\("jobs/\{jobId:guid\}/cancel"\)\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($jobCancelWithout -eq $oldApi -or (Test-JobCancel $jobCancelWithout).Count -eq 0) { $failures += "a cancel-by-job action without the antiforgery check was not refused" }
    $planted++; if ((Test-JobCancel "class Empty { }").Count -eq 0) { $failures += "a PayrollApiController without the cancel-by-job action was not noticed" }

    # 6. the event's row
    $inventory = Read-Text $paths.Inventory
    $noRow = [regex]::Replace($inventory, '(?m)^\|\s*`IPayrollReversed`.*$', '')
    $planted++; if ($noRow -eq $inventory -or (Test-Inventory $noRow).Count -eq 0) { $failures += "an inventory without the IPayrollReversed row was not refused" }
    $noPublisher = $inventory -replace 'PublishPayrollReversedAsync', 'SomewhereElse'
    $planted++; if ($noPublisher -eq $inventory -or (Test-Inventory $noPublisher).Count -eq 0) { $failures += "an IPayrollReversed row without its publisher was not refused" }

    # 7. the acceptance class
    $ownership = Get-Json (Read-Text $paths.Ownership)
    $scope = Get-Json (Read-Text $paths.Scope)
    $scope.prodLike = @($scope.prodLike | Where-Object { $_.class -ne "AcPay56RunStateMachineTests" })
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "a scope without AC-PAY-56 was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    ($scope.prodLike | Where-Object { $_.class -eq "AcPay56RunStateMachineTests" }).required = $false
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "AC-PAY-56 not required was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    ($scope.prodLike | Where-Object { $_.class -eq "AcPay56RunStateMachineTests" }).group = "isolated"
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "AC-PAY-56 outside the main group was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $planted++; if ((Test-Acceptance $scope $null).Count -eq 0) { $failures += "AC-PAY-56 with no owner was not refused" }

    $own = Invoke-All
    if ($own.Count -ne 0) { $failures += "the repository itself does not pass: $($own -join '; ')" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] pay056-validate self-test: $planted planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] pay056-validate: a net is computed in PayrollNet only and written by its three named writers, a run's status moves through PayrollRunTransitions only and a run is constructed by the creators of a draft and the seeds, no reversal batch, direct reversal or manual salary line is left, the PAY056 migrations have no byte order mark, fill TotalAdjustments only with FORCE lifted and restored and put row security on the new tables, every write of the run's controllers validates its token, IPayrollReversed is in the publisher inventory, AC-PAY-56 is required in the prod-like scope" -ForegroundColor Green
exit 0
