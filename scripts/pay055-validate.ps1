# PLAN-55 (PAY-B): static checks of the employee's bank account and of the pay profile versions, and of what guards them.
# ASCII only; Windows PowerShell 5.1 syntax (run here by pwsh 7).
#
#   pwsh -NoProfile -File scripts/pay055-validate.ps1 [-SelfTest]
#
# What it checks (Pay055* tests, PayProfileWritersTests, PayrollWriteActionsTests and AC-PAY-55 prove the behaviour; this script
# guards the files a reviewer or an operator edits by hand):
#   1. Nothing reads or writes the three obsolete bank fields of the pay profile (BankAccountIban, BankName, BankAccountVerifiedAt)
#      outside the entity: the employee's account is EmployeeBankAccount. The versioning helper only names them as text.
#   2. No "new EmployeePayProfile" in the Payroll service outside EmployeePayProfileVersioning (CreateNextVersion) and the seed.
#   3. Migration PAY055_EmployeeBankAccounts: no byte order mark; it lifts FORCE on the pay profiles before its data move and puts
#      it back after; a moved account is pending (Status 0) and carries no verification; the new table gets row security; the
#      three old columns are not dropped.
#   4. Payroll:Bank:Banks is empty in Production and Staging, and the prod-like compose mirrors it as empty.
#   5. Every [HttpPost] of the new controllers validates an antiforgery token.
#   6. AcPay55BankAccountTests is a required prod-like class of the main group, and AC-PAY-55 is owned by PLAN-55.
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
    # A settings file may start with a byte order mark.
    return ($text.TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

$paths = @{
    Services = "src/Services"
    Payroll = "src/Services/Payroll"
    Entity = "src/Services/Payroll/Payroll.Domain/Entities/EmployeePayProfile.cs"
    Versioning = "src/Services/Payroll/Payroll.Domain/Services/EmployeePayProfileVersioning.cs"
    Seed = "src/Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs"
    Migrations = "src/Services/Payroll/Payroll.Infrastructure/Migrations"
    Production = "src/Services/Payroll/Payroll.Web/appsettings.Production.json"
    Staging = "src/Services/Payroll/Payroll.Web/appsettings.Staging.json"
    ProdLike = "docker-compose.acceptance-prodlike.yml"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
}
$controllers = @(
    "src/Services/Payroll/Payroll.Web/Controllers/EmployeeBankAccountsApiController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/EmployeeBankAccountsController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PayrollAdminController.cs"
)
$obsolete = @("BankAccountIban", "BankName", "BankAccountVerifiedAt")

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

# 1. $sources: every service's sources. The entity declares the three fields; the versioning helper names them as text only.
function Test-ObsoleteFields($sources) {
    $problems = @()
    if ($null -eq $sources -or $sources.Count -eq 0) { return @("no source file was read under src/Services: the check reads the wrong folder") }
    $entity = @($sources | Where-Object { $_.Path -eq $paths.Entity }) | Select-Object -First 1
    if ($null -eq $entity) { return @("EmployeePayProfile.cs is missing") }
    foreach ($name in $obsolete) {
        # The property with its [Obsolete] attribute among the attributes above it.
        $declared = [regex]::Match($entity.Text, '(?s)((?:\s*\[[^\]]*\]\s*)+)public\s+[\w\?<>]+\s+' + $name + '\s*\{')
        if (-not $declared.Success) { $problems += "EmployeePayProfile.$name is no longer declared: the column must stay for a rollback" }
        elseif ($declared.Groups[1].Value -notmatch '\[Obsolete\(') { $problems += "EmployeePayProfile.$name is not marked [Obsolete]" }
    }
    foreach ($source in $sources) {
        if ($source.Path -eq $paths.Entity) { continue }
        foreach ($name in $obsolete) {
            $pattern = if ($source.Path -eq $paths.Versioning) { '(?<!")\b' + $name + '\b(?!")' } else { '\b' + $name + '\b' }
            if ($source.Text -cmatch $pattern) { $problems += "$($source.Path) uses the obsolete pay profile field $name" }
        }
    }
    return $problems
}

# 2. $sources: the Payroll service's sources.
function Test-ProfileCreation($sources) {
    $problems = @()
    $allowed = @($paths.Versioning, $paths.Seed)
    $seen = 0
    foreach ($source in $sources) {
        $creations = [regex]::Matches($source.Text, 'new\s+EmployeePayProfile\b(?!History)')
        if ($creations.Count -eq 0) { continue }
        $seen += $creations.Count
        if ($allowed -notcontains $source.Path) { $problems += "$($source.Path) creates a pay profile outside EmployeePayProfileVersioning.CreateNextVersion and the seed" }
    }
    if ($seen -eq 0) { $problems += "no 'new EmployeePayProfile' was found at all: the check reads the wrong folder" }
    $versioning = @($sources | Where-Object { $_.Path -eq $paths.Versioning }) | Select-Object -First 1
    if ($null -eq $versioning -or $versioning.Text -notmatch 'public\s+static\s+PayProfileVersionStep\s+CreateNextVersion\(') { $problems += "EmployeePayProfileVersioning.CreateNextVersion is missing" }
    return $problems
}

# 3.
function Test-Migration([byte[]]$bytes, [string]$text) {
    if ($null -eq $bytes -or $null -eq $text) { return @("migration PAY055_EmployeeBankAccounts is missing") }
    $problems = @()
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $problems += "the migration starts with a byte order mark" }

    $up = [regex]::Match($text, '(?s)protected override void Up\(MigrationBuilder migrationBuilder\)(.*?)protected override void Down\(')
    if (-not $up.Success) { return $problems + "the migration has no Up method" }
    $body = $up.Groups[1].Value
    $lift = [regex]::Match($body, 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{ProfilesTable\}" NO FORCE ROW LEVEL SECURITY;"""\)')
    $move = [regex]::Match($body, 'migrationBuilder\.Sql\(MoveProfileAccountsSql\)')
    $restore = [regex]::Match($body, 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{ProfilesTable\}" FORCE ROW LEVEL SECURITY;"""\)')
    if ($text -notmatch 'ProfilesTable\s*=\s*"EmployeePayProfiles"') { $problems += "the migration does not name the pay profiles as the table its data move reads" }
    if (-not $move.Success) { $problems += "the migration has no data move (MoveProfileAccountsSql)" }
    elseif (-not $lift.Success -or $lift.Index -gt $move.Index) { $problems += "the migration does not lift FORCE on the pay profiles before its data move: as the migrator it would read no row and say nothing" }
    elseif (-not $restore.Success -or $restore.Index -lt $move.Index) { $problems += "the migration does not put FORCE back on the pay profiles after its data move" }

    $insert = [regex]::Match($text, '(?s)INSERT INTO "EmployeeBankAccounts"\s*\(([^)]*)\)\s*SELECT\s+(.*?)\s+FROM\s*\(')
    if (-not $insert.Success) { $problems += "the data move is not an INSERT ... SELECT into EmployeeBankAccounts" }
    else {
        $columns = @($insert.Groups[1].Value -split ',' | ForEach-Object { $_.Trim().Trim('"') })
        $values = @($insert.Groups[2].Value -split ',' | ForEach-Object { $_.Trim() })
        $status = [array]::IndexOf($columns, "Status")
        if ($columns.Count -ne $values.Count) { $problems += "the data move's column list and values do not match" }
        elseif ($status -lt 0 -or $values[$status] -ne "0") { $problems += "the data move does not write a moved account as pending (Status 0): an implicit verification" }
        foreach ($forbidden in @("VerifiedBy", "VerifiedAt", "VerificationReference")) {
            if ($columns -contains $forbidden) { $problems += "the data move writes ${forbidden}: an account is verified by a person, never by a migration" }
        }
    }
    if ($body -notmatch 'TenantRlsMigrationHelper\.EnableRls\(\s*migrationBuilder\s*,\s*"EmployeeBankAccounts"') { $problems += "the migration does not put row security on EmployeeBankAccounts" }
    foreach ($name in $obsolete) {
        if ($body -match ('DropColumn\(\s*name:\s*"' + $name + '"')) { $problems += "the migration drops the column ${name}: the previous tag reads it after a rollback" }
    }
    return $problems
}

# 4.
function Test-BankList([string]$name, $settings) {
    if ($null -eq $settings) { return @("$name is missing") }
    if ($null -eq $settings.Payroll -or $null -eq $settings.Payroll.Bank -or $null -eq $settings.Payroll.Bank.PSObject.Properties["Banks"]) {
        return @("$name does not set Payroll:Bank:Banks (it must be there, and empty)")
    }
    if (@($settings.Payroll.Bank.Banks).Count -ne 0) { return @("$name defines a bank in Payroll:Bank:Banks: the list is empty until the bank file is adopted (D-PAY-11)") }
    return @()
}

function Test-Mirror([string]$compose) {
    if ($null -eq $compose) { return @("docker-compose.acceptance-prodlike.yml is missing") }
    $problems = @()
    if ($compose -notmatch '(?m)^\s*Payroll__Bank__Banks:\s*""\s*$') { $problems += "the prod-like compose does not mirror Payroll__Bank__Banks as empty" }
    if ($compose -match 'Payroll__Bank__Banks__') { $problems += "the prod-like compose defines a bank (Payroll__Bank__Banks__...)" }
    return $problems
}

# 5. A controller's text: every [HttpPost] (alone, with a route, or with other attributes in the same brackets) validates the token.
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

# 6.
function Test-Acceptance($scope, $ownership) {
    $problems = @()
    if ($null -eq $scope) { return @("tests/acceptance/ci-scope.json is missing") }
    $row = @($scope.prodLike | Where-Object { $_.class -eq "AcPay55BankAccountTests" })
    if ($row.Count -ne 1 -or $row[0].group -ne "main" -or -not $row[0].required) { $problems += "AcPay55BankAccountTests is not a required prod-like class of the main group" }
    $entry = $null
    if ($null -ne $ownership) {
        $ac = $ownership.PSObject.Properties | Where-Object { $_.Name -eq "acs" -or $_.Name -eq "classes" -or $_.Name -eq "entries" } | Select-Object -First 1
        $entries = if ($null -ne $ac) { $ac.Value } else { $ownership }
        $entry = $entries.PSObject.Properties | Where-Object { $_.Name -eq "AC-PAY-55" } | Select-Object -First 1
    }
    if ($null -eq $entry -or $entry.Value.ownerPlan -ne "PLAN-55" -or -not $entry.Value.provesClaim) { $problems += "AC-PAY-55 is not owned by PLAN-55 with provesClaim" }
    return $problems
}

function Get-Migration {
    $directory = Join-Path $root $paths.Migrations
    $file = Get-ChildItem -Path $directory -File -Filter "*_PAY055_EmployeeBankAccounts.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $file) { return @{ Bytes = $null; Text = $null } }
    return @{ Bytes = [System.IO.File]::ReadAllBytes($file.FullName); Text = [System.IO.File]::ReadAllText($file.FullName) }
}

function Invoke-All {
    $all = @()
    $services = Get-Sources $paths.Services
    $all += Test-ObsoleteFields $services
    $all += Test-ProfileCreation @($services | Where-Object { $_.Path.StartsWith($paths.Payroll + "/") })
    $migration = Get-Migration
    $all += Test-Migration $migration.Bytes $migration.Text
    $all += Test-BankList "appsettings.Production.json" (Get-Json (Read-Text $paths.Production))
    $all += Test-BankList "appsettings.Staging.json" (Get-Json (Read-Text $paths.Staging))
    $all += Test-Mirror (Read-Text $paths.ProdLike)
    foreach ($controller in $controllers) { $all += Test-Antiforgery (Split-Path -Leaf $controller) (Read-Text $controller) }
    $all += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    return @($all | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $planted = 0
    $services = Get-Sources $paths.Services
    $payroll = @($services | Where-Object { $_.Path.StartsWith($paths.Payroll + "/") })

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

    # 1. the obsolete fields
    $reader = "class Planted { string Read(EmployeePayProfile p) => p.BankAccountIban; }"
    $planted++; if ((Test-ObsoleteFields (Copy-Sources $services "src/Services/Payroll/Payroll.Application/Planted.cs" $reader)).Count -eq 0) { $failures += "a reader of the profile's BankAccountIban was not refused" }
    $view = '<td>@Model.BankName</td>'
    $planted++; if ((Test-ObsoleteFields (Copy-Sources $services "src/Services/Payroll/Payroll.Web/Views/Planted.cshtml" $view)).Count -eq 0) { $failures += "a view that shows the profile's BankName was not refused" }
    $versioning = Read-Text $paths.Versioning
    $copied = $versioning -replace 'RegimeCode = previous\.RegimeCode,', 'RegimeCode = previous.RegimeCode, BankAccountVerifiedAt = previous.BankAccountVerifiedAt,'
    $planted++; if ($copied -eq $versioning -or (Test-ObsoleteFields (Copy-Sources $services $paths.Versioning $copied)).Count -eq 0) { $failures += "a copy-forward of the obsolete verification date was not refused" }
    $entity = Read-Text $paths.Entity
    $unmarked = [regex]::Replace($entity, '\[Obsolete\(BankFieldsObsolete\)\]\s*(public string\? BankName)', '${1}', 1)
    $planted++; if ($unmarked -eq $entity -or (Test-ObsoleteFields (Copy-Sources $services $paths.Entity $unmarked)).Count -eq 0) { $failures += "a bank field that is no longer [Obsolete] was not refused" }
    $dropped = [regex]::Replace($entity, 'public DateTime\? BankAccountVerifiedAt \{ get; set; \}', '', 1)
    $planted++; if ($dropped -eq $entity -or (Test-ObsoleteFields (Copy-Sources $services $paths.Entity $dropped)).Count -eq 0) { $failures += "a removed bank column was not refused" }

    # 2. the creation of a pay profile
    $creator = "class Planted { EmployeePayProfile Make() => new EmployeePayProfile { IsActive = true }; }"
    $planted++; if ((Test-ProfileCreation (Copy-Sources $payroll "src/Services/Payroll/Payroll.Application/Features/Planted.cs" $creator)).Count -eq 0) { $failures += "a pay profile created in a handler was not refused" }
    $history = "class Planted { object Make() => new EmployeePayProfileHistory(); }"
    $planted++; if ((Test-ProfileCreation (Copy-Sources $payroll "src/Services/Payroll/Payroll.Application/Features/Planted.cs" $history)).Count -ne 0) { $failures += "a history snapshot was taken for a pay profile" }
    $renamed = $versioning -replace 'PayProfileVersionStep CreateNextVersion\(', 'PayProfileVersionStep MakeVersion('
    $planted++; if ($renamed -eq $versioning -or (Test-ProfileCreation (Copy-Sources $payroll $paths.Versioning $renamed)).Count -eq 0) { $failures += "a versioning helper without CreateNextVersion was not refused" }

    # 3. the migration
    $migration = Get-Migration
    $planted++; if ((Test-Migration ([byte[]](@(0xEF, 0xBB, 0xBF) + $migration.Bytes)) $migration.Text).Count -eq 0) { $failures += "a migration with a byte order mark was not refused" }
    $noLift = $migration.Text -replace 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{ProfilesTable\}" NO FORCE ROW LEVEL SECURITY;"""\);', ''
    $planted++; if ($noLift -eq $migration.Text -or (Test-Migration $migration.Bytes $noLift).Count -eq 0) { $failures += "a data move under forced row security was not refused" }
    $noRestore = $migration.Text -replace 'migrationBuilder\.Sql\(\$"""ALTER TABLE "\{ProfilesTable\}" FORCE ROW LEVEL SECURITY;"""\);', ''
    $planted++; if ($noRestore -eq $migration.Text -or (Test-Migration $migration.Bytes $noRestore).Count -eq 0) { $failures += "a migration that leaves the pay profiles unforced was not refused" }
    $active = $migration.Text -replace 'moved\.number, moved\.bank, 0,', 'moved.number, moved.bank, 1,'
    $planted++; if ($active -eq $migration.Text -or (Test-Migration $migration.Bytes $active).Count -eq 0) { $failures += "a data move that makes moved accounts Active was not refused" }
    $verified = ($migration.Text -replace '"Status", "TenantId"', '"Status", "VerifiedAt", "TenantId"') -replace 'moved\.bank, 0, moved\."TenantId"', 'moved.bank, 0, now(), moved."TenantId"'
    $planted++; if ($verified -eq $migration.Text -or (Test-Migration $migration.Bytes $verified).Count -eq 0) { $failures += "a data move that writes a verification date was not refused" }
    $noRls = $migration.Text -replace 'TenantRlsMigrationHelper\.EnableRls\(', 'TenantRlsMigrationHelper.Nothing('
    $planted++; if ($noRls -eq $migration.Text -or (Test-Migration $migration.Bytes $noRls).Count -eq 0) { $failures += "a new table without row security was not refused" }
    $drop = $migration.Text -replace '(protected override void Up\(MigrationBuilder migrationBuilder\)\s*\{)', ('$1' + "`n" + '            migrationBuilder.DropColumn(name: "BankAccountIban", table: "EmployeePayProfiles");')
    $planted++; if ($drop -eq $migration.Text -or (Test-Migration $migration.Bytes $drop).Count -eq 0) { $failures += "a migration that drops an old bank column was not refused" }

    # 4. the bank list
    $production = Get-Json (Read-Text $paths.Production)
    $production.Payroll.Bank.Banks = @(@{ code = "CBS"; nameAr = "x"; accountRegex = "^[0-9]{12}$" })
    $planted++; if ((Test-BankList "planted" $production).Count -eq 0) { $failures += "a bank defined in Production was not refused" }
    $production = Get-Json (Read-Text $paths.Production)
    $production.Payroll.PSObject.Properties.Remove("Bank")
    $planted++; if ((Test-BankList "planted" $production).Count -eq 0) { $failures += "a Production file without Payroll:Bank:Banks was not refused" }
    $compose = Read-Text $paths.ProdLike
    $noMirror = $compose -replace '(?m)^\s*Payroll__Bank__Banks:.*$', ''
    $planted++; if ($noMirror -eq $compose -or (Test-Mirror $noMirror).Count -eq 0) { $failures += "a prod-like mirror without the bank list was not refused" }
    $withBank = $compose -replace '(?m)^(\s*)Payroll__Bank__Banks:\s*""\s*$', ('$1Payroll__Bank__Banks: ""' + "`n" + '$1Payroll__Bank__Banks__0__Code: "CBS"')
    $planted++; if ($withBank -eq $compose -or (Test-Mirror $withBank).Count -eq 0) { $failures += "a prod-like mirror that defines a bank was not refused" }

    # 5. the antiforgery token
    $api = Read-Text $controllers[0]
    $withoutOne = [regex]::Replace($api, '(\[HttpPost\("\{id:guid\}/verify"\)\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($withoutOne -eq $api -or (Test-Antiforgery "planted" $withoutOne).Count -eq 0) { $failures += "a verify action of the API without the antiforgery check was not refused" }
    $mvc = Read-Text $controllers[1]
    $mvcWithout = [regex]::Replace($mvc, '\[HttpPost, ValidateAntiForgeryToken\]', '[HttpPost]', 1)
    $planted++; if ($mvcWithout -eq $mvc -or (Test-Antiforgery "planted" $mvcWithout).Count -eq 0) { $failures += "a form action without the antiforgery check was not refused" }
    $admin = Read-Text $controllers[2]
    $adminWithout = [regex]::Replace($admin, '(\[HttpPost\("backfill-profile-orgunit"\)\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($adminWithout -eq $admin -or (Test-Antiforgery "planted" $adminWithout).Count -eq 0) { $failures += "a repair without the antiforgery check was not refused" }
    $planted++; if ((Test-Antiforgery "planted" "class Empty { }").Count -eq 0) { $failures += "a controller with no write action was not noticed" }

    # 6. the acceptance class
    $ownership = Get-Json (Read-Text $paths.Ownership)
    $scope = Get-Json (Read-Text $paths.Scope)
    $scope.prodLike = @($scope.prodLike | Where-Object { $_.class -ne "AcPay55BankAccountTests" })
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "a scope without AC-PAY-55 was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    ($scope.prodLike | Where-Object { $_.class -eq "AcPay55BankAccountTests" }).required = $false
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "AC-PAY-55 not required was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    ($scope.prodLike | Where-Object { $_.class -eq "AcPay55BankAccountTests" }).group = "isolated"
    $planted++; if ((Test-Acceptance $scope $ownership).Count -eq 0) { $failures += "AC-PAY-55 outside the main group was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $planted++; if ((Test-Acceptance $scope $null).Count -eq 0) { $failures += "AC-PAY-55 with no owner was not refused" }

    $own = Invoke-All
    if ($own.Count -ne 0) { $failures += "the repository itself does not pass: $($own -join '; ')" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] pay055-validate self-test: $planted planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] pay055-validate: the profile's bank fields are obsolete and unread, a pay profile is made in CreateNextVersion and the seed only, PAY055 moves old numbers as pending with FORCE lifted and restored, no bank is defined in Production, Staging and the prod-like mirror, every POST of the new controllers validates its token, AC-PAY-55 is required in the prod-like scope" -ForegroundColor Green
exit 0
