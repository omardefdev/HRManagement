# PLAN-58 (PAY-F): static checks of disbursement governance and of what guards it.
# ASCII only; Windows PowerShell 5.1 syntax (run here by pwsh 7).
#
#   pwsh -NoProfile -File scripts/pay058-validate.ps1 [-SelfTest]
#
# What it checks (Pay058* tests, Pay058SinglePaymentPathArchitectureTests, PayrollWriteActionsTests and AC-PAY-58 prove the behaviour;
# this script guards the files a reviewer or an operator edits by hand):
#   1. One payment path: a PaymentBatch is constructed only by the issuer (and the entity's named seed construction), and
#      EnsureAndSubmitOrdersAsync is called only by its two senders (process and submit-held).
#   2. What was removed stays removed: no partial-disburse route, no ExportTreasuryFile, no treasury bundle in Payroll.
#   3. No actor of Payroll is read from the client: no request record or action parameter carries an approver, a decider, a requester
#      or an actor id.
#   4. The conflicting roles: the conflict requirement covers Payroll's five financial policies and Payroll.Web registers it; no
#      acceptance persona holds a conflicting pair but the one marked fixture.
#   5. The SQL audit fixture is gone, and nothing names it.
#   6. The PAY058 migration and its designer have no byte order mark.
#   7. The settings: Payroll's appsettings.json carries Payroll:Approval, :Audit, :Blockers, :Sod and :DualControl, and the prod-like
#      overlay mirrors each of them.
#   8. Every POST of the controllers PLAN-58 wrote or changed validates an antiforgery token.
#   9. AcPay58DisbursementGovernanceTests is a required prod-like class of the main group, and AC-PAY-58 is owned by PLAN-58.
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
    Payroll = "src/Services/Payroll"
    Issuer = "src/Services/Payroll/Payroll.Application/Services/PaymentBatchIssuer.cs"
    Batch = "src/Services/Payroll/Payroll.Domain/Entities/PaymentBatch.cs"
    Senders = "src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PaymentBatchHandlers.cs"
    Coordinator = "src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderCoordinator.cs"
    CoordinatorContract = "src/Services/Payroll/Payroll.Application/Abstractions/IPaymentOrderCoordinator.cs"
    Conflict = "src/Services/Payroll/Payroll.Web/Authorization/PayrollRoleConflictRequirement.cs"
    Program = "src/Services/Payroll/Payroll.Web/Program.cs"
    Personas = "testdata/acceptance/personas.json"
    PersonaGuard = "scripts/keycloak/persona-role-conflicts.ps1"
    Fixture = "scripts/acceptance-payroll-audit-fixture.ps1"
    Migrations = "src/Services/Payroll/Payroll.Infrastructure/Migrations"
    Settings = "src/Services/Payroll/Payroll.Web/appsettings.json"
    Overlay = "docker-compose.acceptance-prodlike.yml"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
}
$controllers = @(
    "src/Services/Payroll/Payroll.Web/Controllers/PaymentBatchesApiController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PaymentBatchesController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/DualControlApiController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/DualControlController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsApiController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PayrollAuditRecordsApiController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/FraudAlertsController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/ArrearsController.cs"
)
$financialPolicies = @("PayrollProcess", "PayrollApprove", "PayrollPost", "PayrollReverse", "PayrollAuditReview")
$settings = @(
    @{ Key = "Approval"; Field = "DualApprovalThreshold"; Overlay = "Payroll__Approval__DualApprovalThreshold" },
    @{ Key = "Audit"; Field = "Required"; Overlay = "Payroll__Audit__Required" },
    @{ Key = "Audit"; Field = "ReviewerRoles"; Overlay = "Payroll__Audit__ReviewerRoles" },
    @{ Key = "Blockers"; Field = "Mode"; Overlay = "Payroll__Blockers__Mode" },
    @{ Key = "Sod"; Field = "RejectConflictingRoleHolders"; Overlay = "Payroll__Sod__RejectConflictingRoleHolders" },
    @{ Key = "DualControl"; Field = "ExpiryHours"; Overlay = "Payroll__DualControl__ExpiryHours" }
)

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

# Code without its comments (line and block), so that prose about a removed path is not read as one.
function Get-Code([string]$text) {
    $noBlocks = [regex]::Replace($text, '/\*.*?\*/', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $lines = foreach ($line in ($noBlocks -split "`n")) {
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("@*")) { "" }
        else {
            $comment = $line.IndexOf("//")
            if ($comment -ge 0 -and $line.Substring(0, $comment).Split('"').Count % 2 -eq 1) { $line.Substring(0, $comment) } else { $line }
        }
    }
    return ($lines -join "`n")
}

$newBatch = New-Object System.Text.RegularExpressions.Regex('new\s+PaymentBatch\s*(\{|\()')
$submitCall = New-Object System.Text.RegularExpressions.Regex('\.EnsureAndSubmitOrdersAsync\s*\(')

# 1. $sources: Payroll's product sources.
function Test-OnePath($sources) {
    $problems = @()
    if (@($sources).Count -eq 0) { return @("no source file was read under Payroll: the check reads the wrong folder") }
    $issuerSeen = $false
    $sendersSeen = $false
    foreach ($source in $sources) {
        $code = Get-Code $source.Text
        if ($newBatch.IsMatch($code)) {
            if ($source.Path -eq $paths.Issuer) { $issuerSeen = $true }
            elseif ($source.Path -ne $paths.Batch) { $problems += "$($source.Path) constructs a PaymentBatch: only the issuer (the posting) and the entity's seed construction do" }
        }
        if ($submitCall.IsMatch($code)) {
            if ($source.Path -eq $paths.Senders) { $sendersSeen = $true }
            elseif ($source.Path -ne $paths.Coordinator -and $source.Path -ne $paths.CoordinatorContract) {
                $problems += "$($source.Path) submits payment orders: only process and submit-held do"
            }
        }
    }
    if (-not $issuerSeen) { $problems += "the issuer constructs no PaymentBatch: the check no longer sees the one path" }
    if (-not $sendersSeen) { $problems += "PaymentBatchHandlers.cs submits no order: the check no longer sees the senders" }
    return $problems
}

# 2. the removed paths.
function Test-Removed($sources) {
    $problems = @()
    foreach ($source in $sources) {
        $code = Get-Code $source.Text
        # The route, not a seed's key that begins with the same words (partial-disbursement-rule-set).
        foreach ($removed in @('partial-disburse(?!ment)', 'ExportTreasuryFile', 'TreasuryBundle', 'ExecutePartialDisbursement')) {
            if ([regex]::IsMatch($code, $removed)) { $problems += "$($source.Path) names ${removed}: PLAN-58 removed it (one payment path)" }
        }
    }
    return $problems
}

$actorNames = '(ApproverUserId|ApproverId|ActorId|ApprovedBy|DecidedBy|RequestedBy|ReleasedBy|PostedBy|ClosedBy|VerifiedBy|DecidedByUserId)'
$requestRecord = New-Object System.Text.RegularExpressions.Regex('record\s+\w*Request\s*\(([^)]*)\)', [System.Text.RegularExpressions.RegexOptions]::Singleline)
# (The pattern is built first: in an argument list the comma binds tighter than +.)
$actorPattern = 'Guid\??\s+' + $actorNames + '\b'
$actorParameter = New-Object System.Text.RegularExpressions.Regex($actorPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$actionSignature = New-Object System.Text.RegularExpressions.Regex('public\s+(async\s+)?(Task<)?IActionResult>?\s+\w+\s*\(([^)]*)\)', [System.Text.RegularExpressions.RegexOptions]::Singleline)

# 3. $sources: Payroll.Web's sources.
function Test-Actors($sources) {
    $problems = @()
    foreach ($source in $sources) {
        if (-not $source.Path.StartsWith("src/Services/Payroll/Payroll.Web/")) { continue }
        $code = Get-Code $source.Text
        foreach ($match in $requestRecord.Matches($code)) {
            if ($actorParameter.IsMatch($match.Groups[1].Value)) { $problems += "$($source.Path): a request record carries an actor id: $($match.Value -replace '\s+', ' ')" }
        }
        foreach ($match in $actionSignature.Matches($code)) {
            if ($actorParameter.IsMatch($match.Groups[3].Value)) { $problems += "$($source.Path): an action takes an actor id from the client: $($match.Value -replace '\s+', ' ')" }
        }
    }
    return $problems
}

# 4. the conflict requirement and the personas.
function Test-Conflicts([string]$conflict, [string]$program, $personas) {
    $problems = @()
    if ($null -eq $conflict) { return @("PayrollRoleConflictRequirement.cs is missing") }
    $block = [regex]::Match($conflict, 'FinancialPolicies[^;]*;', [System.Text.RegularExpressions.RegexOptions]::Singleline).Value
    foreach ($policy in $financialPolicies) {
        if (-not $block.Contains("HrManagementPolicies.$policy")) { $problems += "the conflict requirement does not cover $policy" }
    }
    if ($null -eq $program -or -not (Get-Code $program).Contains("AddPayrollRoleConflictCheck()")) { $problems += "Payroll.Web does not register the conflict check" }
    if ($null -eq $personas) { $problems += "personas.json is missing" }
    else {
        foreach ($conflictLine in (Get-PersonaRoleConflicts -Personas $personas.personas)) { $problems += "persona with a conflicting pair: $conflictLine" }
        $fixtures = @($personas.personas | Where-Object { $null -ne $_.sodConflictFixture -and [bool]$_.sodConflictFixture })
        if ($fixtures.Count -ne 1) { $problems += "personas.json marks $($fixtures.Count) conflict fixtures, expected one" }
    }
    return $problems
}

# 5. the SQL audit fixture.
function Test-Fixture([bool]$exists, $sources) {
    $problems = @()
    if ($exists) { $problems += "$($paths.Fixture) is back: a posting's review is two auditors' act by API" }
    foreach ($source in $sources) {
        if ($source.Text.Contains("acceptance-payroll-audit-fixture")) { $problems += "$($source.Path) names the deleted audit fixture" }
    }
    return $problems
}

# 6. the migration.
function Test-NoBom([string]$name, $bytes) {
    if ($null -eq $bytes) { return @("$name is missing") }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return @("$name has a byte order mark") }
    return @()
}

function Get-MigrationFiles {
    $directory = Join-Path $root $paths.Migrations
    return @(Get-ChildItem -Path $directory -File -Filter "*_PAY058_DisbursementGovernance*.cs")
}

# 7. the settings.
function Test-Settings($appsettings, [string]$overlay) {
    $problems = @()
    if ($null -eq $appsettings -or $null -eq $appsettings.Payroll) { return @("Payroll's appsettings.json has no Payroll section") }
    foreach ($setting in $settings) {
        $section = $appsettings.Payroll.($setting.Key)
        if ($null -eq $section -or $null -eq $section.($setting.Field)) { $problems += "appsettings.json has no Payroll:$($setting.Key):$($setting.Field)" }
        if ($null -eq $overlay -or -not $overlay.Contains($setting.Overlay)) { $problems += "the prod-like overlay does not mirror $($setting.Overlay)" }
    }
    return $problems
}

# 8. the antiforgery token: every [HttpPost] attribute block of the file holds [ValidateAntiForgeryToken].
function Test-Antiforgery([string]$name, [string]$text) {
    if ($null -eq $text) { return @("$name is missing") }
    $problems = @()
    $code = Get-Code $text
    $posts = [regex]::Matches($code, '\[HttpPost[^\]]*\](?<rest>(\s*\[[^\]]*\])*)')
    if ($posts.Count -eq 0) { return @("$name has no POST action: the check reads the wrong file") }
    foreach ($post in $posts) {
        $block = $post.Value
        $before = $code.Substring([Math]::Max(0, $post.Index - 200), [Math]::Min(200, $post.Index))
        $lastBlock = [regex]::Match($before, '(\s*\[[^\]]*\])*\s*$').Value
        if (-not ($block.Contains("ValidateAntiForgeryToken") -or $lastBlock.Contains("ValidateAntiForgeryToken"))) {
            $problems += "$name has a POST without [ValidateAntiForgeryToken]: $($post.Value.Trim() -replace '\s+', ' ')"
        }
    }
    return $problems
}

# 9. the acceptance class.
function Test-Acceptance($scope, $ownership) {
    $problems = @()
    $entry = @($scope.prodLike | Where-Object { $_.class -eq "AcPay58DisbursementGovernanceTests" }) | Select-Object -First 1
    if ($null -eq $entry) { $problems += "AcPay58DisbursementGovernanceTests is not in prodLike[]" }
    else {
        if (-not [bool]$entry.required) { $problems += "AcPay58DisbursementGovernanceTests is not required" }
        if ($entry.group -ne "main") { $problems += "AcPay58DisbursementGovernanceTests is not in the main group" }
    }
    $owner = if ($null -ne $ownership) { $ownership.acs."AC-PAY-58" } else { $null }
    if ($null -eq $owner -or $owner.ownerPlan -ne "PLAN-58") { $problems += "AC-PAY-58 is not owned by PLAN-58 in ac-ownership.json" }
    return $problems
}

# The persona guard has a -SelfTest switch of its own: dot-sourcing it rebinds $SelfTest, so this script's is kept first.
$runSelfTest = $SelfTest.IsPresent
. (Join-Path $root $paths.PersonaGuard)

$product = Get-Sources $paths.Payroll
$everything = @(Get-Sources "src") + @(foreach ($file in (Get-ChildItem -Path (Join-Path $root "scripts") -Recurse -File -Include *.ps1, *.sh, *.json)) {
        $path = $file.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
        if ($path -eq "scripts/pay058-validate.ps1") { continue }
        @{ Path = $path; Text = [System.IO.File]::ReadAllText($file.FullName) }
    })

function Invoke-All {
    $problems = @()
    $problems += Test-OnePath $product
    $problems += Test-Removed $product
    $problems += Test-Actors $product
    $problems += Test-Conflicts (Read-Text $paths.Conflict) (Read-Text $paths.Program) (Get-Json (Read-Text $paths.Personas))
    $problems += Test-Fixture (Test-Path (Join-Path $root $paths.Fixture)) $everything
    $migrations = Get-MigrationFiles
    if ($migrations.Count -ne 2) { $problems += "expected the PAY058 migration and its designer, found $($migrations.Count) file(s)" }
    foreach ($file in $migrations) { $problems += Test-NoBom $file.Name ([System.IO.File]::ReadAllBytes($file.FullName)) }
    $problems += Test-Settings (Get-Json (Read-Text $paths.Settings)) (Read-Text $paths.Overlay)
    foreach ($controller in $controllers) { $problems += Test-Antiforgery $controller (Read-Text $controller) }
    $problems += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    return @($problems | Where-Object { $_ })
}

if ($runSelfTest) {
    $failures = @()
    $planted = 0
    function Copy-Sources($sources, [string]$path, [string]$text) {
        $copy = @()
        $found = $false
        foreach ($source in $sources) {
            if ($source.Path -eq $path) { $copy += @{ Path = $path; Text = $text }; $found = $true } else { $copy += $source }
        }
        if (-not $found) { $copy += @{ Path = $path; Text = $text } }
        return $copy
    }

    # 1. one path
    $batch = "class Planted { PaymentBatch Make() => new PaymentBatch { PayrollRunId = id }; }"
    $planted++; if ((Test-OnePath (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Planted.cs" $batch)).Count -eq 0) { $failures += "a batch constructed outside the issuer was not refused" }
    $send = "class Planted { Task Go() => coordinator.EnsureAndSubmitOrdersAsync(batch, gate, PaymentSending.Initial, ct); }"
    $planted++; if ((Test-OnePath (Copy-Sources $product "src/Services/Payroll/Payroll.Web/Controllers/Planted.cs" $send)).Count -eq 0) { $failures += "orders submitted outside the two senders were not refused" }
    $commented = "class Planted { // new PaymentBatch { } is the issuer's`n }"
    $planted++; if ((Test-OnePath (Copy-Sources $product "src/Services/Payroll/Payroll.Application/Planted.cs" $commented)).Count -ne 0) { $failures += "a comment was taken for a construction" }
    $planted++; if ((Test-OnePath @()).Count -eq 0) { $failures += "an empty source set was not noticed" }

    # 2. removed paths
    foreach ($removed in @('[HttpPost("runs/{id:guid}/partial-disburse")] public IActionResult P() => Ok();', 'IActionResult ExportTreasuryFile() => Ok();', 'class TreasuryBundle { }')) {
        $planted++; if ((Test-Removed (Copy-Sources $product "src/Services/Payroll/Payroll.Web/Controllers/Planted.cs" "class Planted { $removed }")).Count -eq 0) { $failures += "a removed path was not refused: $removed" }
    }

    # 3. actors from the client
    $record = "public sealed record ReleaseApiRequest(string? Reason, Guid? ApproverUserId);"
    $planted++; if ((Test-Actors (Copy-Sources $product "src/Services/Payroll/Payroll.Web/Controllers/Planted.cs" $record)).Count -eq 0) { $failures += "a request record with an approver id was not refused" }
    $action = "class Planted { public async Task<IActionResult> Approve(Guid id, [FromForm] Guid approvedBy) => Ok(); }"
    $planted++; if ((Test-Actors (Copy-Sources $product "src/Services/Payroll/Payroll.Web/Controllers/Planted.cs" $action)).Count -eq 0) { $failures += "an action taking an actor id was not refused" }

    # 4. conflicts
    $conflict = Read-Text $paths.Conflict
    $program = Read-Text $paths.Program
    $personas = Get-Json (Read-Text $paths.Personas)
    $fewer = $conflict -replace 'HrManagementPolicies\.PayrollReverse,?', ''
    $planted++; if ($fewer -eq $conflict -or (Test-Conflicts $fewer $program $personas).Count -eq 0) { $failures += "a conflict requirement without Payroll.Reverse was not refused" }
    $unregistered = $program -replace 'builder\.Services\.AddPayrollRoleConflictCheck\(\);', ''
    $planted++; if ($unregistered -eq $program -or (Test-Conflicts $conflict $unregistered $personas).Count -eq 0) { $failures += "a Payroll.Web without the conflict check was not refused" }
    $badPersonas = Get-Json (Read-Text $paths.Personas)
    $victim = @($badPersonas.personas | Where-Object { $_.id -eq "payroll-processor" }) | Select-Object -First 1
    $victim.roles = @($victim.roles) + "payroll-approver"
    $planted++; if ((Test-Conflicts $conflict $program $badPersonas).Count -eq 0) { $failures += "a persona with processor and approver was not refused" }

    # 5. fixture
    $planted++; if ((Test-Fixture $true @()).Count -eq 0) { $failures += "the audit fixture's return was not refused" }
    $planted++; if ((Test-Fixture $false @(@{ Path = "src/Planted.cs"; Text = 'RunScriptAsync("acceptance-payroll-audit-fixture.ps1")' })).Count -eq 0) { $failures += "a use of the audit fixture was not refused" }

    # 6. migration
    $migration = (Get-MigrationFiles) | Select-Object -First 1
    $bytes = [System.IO.File]::ReadAllBytes($migration.FullName)
    $planted++; if ((Test-NoBom "planted" ([byte[]](@(0xEF, 0xBB, 0xBF) + $bytes))).Count -eq 0) { $failures += "a migration with a byte order mark was not refused" }

    # 7. settings
    $appsettings = Get-Json (Read-Text $paths.Settings)
    $overlay = Read-Text $paths.Overlay
    $planted++; if ((Test-Settings $appsettings ($overlay -replace 'Payroll__Blockers__Mode', 'Payroll__Blockers__Other')).Count -eq 0) { $failures += "an overlay without the blockers' mode was not refused" }
    $appsettings.Payroll.Sod.PSObject.Properties.Remove("RejectConflictingRoleHolders")
    $planted++; if ((Test-Settings $appsettings $overlay).Count -eq 0) { $failures += "appsettings without Payroll:Sod:RejectConflictingRoleHolders was not refused" }

    # 8. antiforgery
    $api = Read-Text $controllers[0]
    $apiWithout = [regex]::Replace($api, '(\[HttpPost\("\{id:guid\}/process"\)\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($apiWithout -eq $api -or (Test-Antiforgery "planted" $apiWithout).Count -eq 0) { $failures += "a process action without the antiforgery check was not refused" }
    $planted++; if ((Test-Antiforgery "planted" "class Empty { }").Count -eq 0) { $failures += "a controller with no POST was not noticed" }

    # 9. acceptance
    $ownership = Get-Json (Read-Text $paths.Ownership)
    $scope = Get-Json (Read-Text $paths.Scope)
    $scope.prodLike = @($scope.prodLike | Where-Object { $_.class -ne "AcPay58DisbursementGovernanceTests" })
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "a scope without AC-PAY-58 was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    ($scope.prodLike | Where-Object { $_.class -eq "AcPay58DisbursementGovernanceTests" }).required = $false
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "AC-PAY-58 not required was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $planted++; if ((Test-Acceptance $scope $null).Count -eq 0) { $failures += "AC-PAY-58 with no owner was not refused" }

    $own = Invoke-All
    if ($own.Count -ne 0) { $failures += "the repository itself does not pass: $($own -join '; ')" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] pay058-validate self-test: $planted planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] pay058-validate: a batch is made by the posting's issuer only and sent by process and submit-held only, no partial disbursement or treasury export is left, no Payroll actor comes from the client, the conflict check covers the five financial policies and no persona but the fixture holds a conflicting pair, the audit fixture is gone, the PAY058 migration has no byte order mark, the settings are in appsettings and mirrored in the prod-like overlay, every POST of PLAN-58's controllers validates its token, AC-PAY-58 is required in the prod-like scope and owned by PLAN-58" -ForegroundColor Green
exit 0
