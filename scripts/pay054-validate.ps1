# PLAN-54 (PAY-A): static checks of the payroll legal framework packs and of what guards them. ASCII only; Windows PowerShell 5.1.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pay054-validate.ps1 [-SelfTest]
#
# What it checks (Pay054* tests, PayrollWriteActionsTests and AC-PAY-54 prove the behaviour; this script guards the files a reviewer
# or an operator edits by hand):
#   1. The schema and the interim pack (docs/syr/payroll-framework-packs): the pack names the schema; its tax brackets, its nineteen
#      governed fields, its four statutory deductions, its holidays and its priority rules equal what the seeds hold
#      (PayrollRegimeSeed, DemoPayrollSeeder, AcceptancePayrollSeeder); every value has a legal reference and a status; every
#      interim value cites an exception of docs/str-legal/control-catalog.json; every LD- reference is a decision of the pack.
#   2. Migration PAY054_LegalFrameworkPacks: no byte order mark; the existing rows are set by column defaults (fingerprint schema 1,
#      Approved, System, Interim, "[]") and by no UPDATE; row security on the three new tables.
#   3. Payroll's settings (base, Production, Staging) hold Payroll:ManualInputs:RequireApproval=true and a
#      Payroll:Legal:AttestorPolicy; the prod-like compose mirrors what Production sets.
#   4. No [HttpPost] or [HttpPut] of PayrollFrameworksApiController is without [ValidateAntiForgeryToken].
#   5. AcPay54LegalFrameworkTests is a required prod-like class of the main group owned by PLAN-54, and the persona of the second
#      approver (the legal attestor) is present.
# -SelfTest runs each check on a planted defect and fails unless every one of them is refused.
param([switch]$SelfTest)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

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
    Schema = "docs/syr/payroll-framework-packs/schema.v1.json"
    Pack = "docs/syr/payroll-framework-packs/law50-interim.json"
    Catalog = "docs/str-legal/control-catalog.json"
    RegimeSeed = "src/Services/Payroll/Payroll.Domain/Services/PayrollRegimeSeed.cs"
    DemoSeed = "src/Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs"
    AcceptanceSeed = "src/Services/Payroll/Payroll.Infrastructure/Data/AcceptancePayrollSeeder.cs"
    Configuration = "src/Services/Payroll/Payroll.Domain/Entities/PayrollConfiguration.cs"
    FieldSource = "src/Services/Payroll/Payroll.Domain/Entities/PayrollConfigurationFieldSource.cs"
    Migrations = "src/Services/Payroll/Payroll.Infrastructure/Migrations"
    Base = "src/Services/Payroll/Payroll.Web/appsettings.json"
    Production = "src/Services/Payroll/Payroll.Web/appsettings.Production.json"
    Staging = "src/Services/Payroll/Payroll.Web/appsettings.Staging.json"
    ProdLike = "docker-compose.acceptance-prodlike.yml"
    Controller = "src/Services/Payroll/Payroll.Web/Controllers/PayrollFrameworksApiController.cs"
    Scope = "tests/acceptance/ci-scope.json"
    Ownership = "tests/acceptance/ac-ownership.json"
    Personas = "testdata/acceptance/personas.json"
}
$schemaName = "payroll-framework-pack.v1"
$statuses = @("Signed", "Interim")

# A C# literal or a JSON value as one comparable text: a number in its shortest invariant form, a boolean in lower case, an enum
# member or a string without its qualifier and quotes.
function Format-Value($value) {
    if ($null -eq $value) { return "" }
    if ($value -is [bool]) { return $value.ToString().ToLowerInvariant() }
    $text = ([string]::Format($invariant, "{0}", $value)).Trim()
    if ($text -match '^"(.*)"$') { return $Matches[1] }
    $number = $text -replace '_', '' -replace 'm$', ''
    $parsed = [double]0
    if ([double]::TryParse($number, [System.Globalization.NumberStyles]::Float, $invariant, [ref]$parsed)) {
        return $parsed.ToString("R", $invariant)
    }
    if ($text -match '^(true|false)$') { return $text }
    if ($text -match '^[A-Za-z_][\w]*\.([A-Za-z_]\w*)$') { return $Matches[1] }
    return $text
}

# What the seeds hold, read from their source: the brackets, the fields, the statutory deductions, the holidays, the priority rules.
function Get-SeedFacts([string]$regimeSeed, [string]$demoSeed, [string]$acceptanceSeed, [string]$configuration, [string]$fieldSource) {
    $facts = @{ Problems = @(); Brackets = @(); Fields = @{}; FieldNames = @(); Deductions = @(); Holidays = @(); Rules = @() }
    if ($null -eq $regimeSeed -or $null -eq $demoSeed -or $null -eq $acceptanceSeed -or $null -eq $configuration -or $null -eq $fieldSource) {
        $facts.Problems += "a seed source is missing (PayrollRegimeSeed, DemoPayrollSeeder, AcceptancePayrollSeeder, PayrollConfiguration or PayrollConfigurationFieldSource)"
        return $facts
    }

    $top = [regex]::Match($regimeSeed, 'TopTaxBracketUpperLimit\s*=\s*([\d_\.]+m)')
    $block = [regex]::Match($regimeSeed, 'Law50TaxBrackets\s*=\s*\[([\s\S]*?)\];')
    if (-not $block.Success -or -not $top.Success) { $facts.Problems += "PayrollRegimeSeed.Law50TaxBrackets was not found" }
    else {
        foreach ($row in [regex]::Matches($block.Groups[1].Value, 'new\(\s*([\w\.]+)\s*,\s*([\d_\.]+m)\s*\)')) {
            $limit = $row.Groups[1].Value
            if ($limit -eq "TopTaxBracketUpperLimit") { $limit = $top.Groups[1].Value }
            $rate = [double]::Parse(($row.Groups[2].Value -replace '_', '' -replace 'm$', ''), $invariant) * 100
            $facts.Brackets += "$(Format-Value $limit)|$(Format-Value $rate)"
        }
    }

    $names = [regex]::Match($fieldSource, 'GovernedFields\s*=\s*\[([\s\S]*?)\];')
    if (-not $names.Success) { $facts.Problems += "PayrollConfigurationFieldSource.GovernedFields was not found" }
    else { $facts.FieldNames = @([regex]::Matches($names.Groups[1].Value, 'nameof\(PayrollConfiguration\.(\w+)\)') | ForEach-Object { $_.Groups[1].Value }) }
    $create = [regex]::Match($regimeSeed, 'private static PayrollConfiguration CreateConfiguration\(([\s\S]*?)\)\s*\{\s*var config = new PayrollConfiguration\s*\{([\s\S]*?)\};')
    $law50 = [regex]::Match($regimeSeed, 'CreateLaw50Configuration\([^)]*\)\s*=>\s*CreateConfiguration\(([\s\S]*?)\);')
    if (-not $create.Success -or -not $law50.Success) { $facts.Problems += "PayrollRegimeSeed.CreateConfiguration or CreateLaw50Configuration was not found" }
    else {
        $arguments = @{}
        foreach ($parameter in [regex]::Matches($create.Groups[1].Value, '(\w+)\s+(\w+)\s*=\s*([^,\)]+)')) { $arguments[$parameter.Groups[2].Value] = $parameter.Groups[3].Value.Trim() }
        foreach ($given in [regex]::Matches($law50.Groups[1].Value, '(\w+):\s*([^,\r\n]+)')) { $arguments[$given.Groups[1].Value] = $given.Groups[2].Value.Trim() }
        $assigned = @{}
        foreach ($line in [regex]::Matches($create.Groups[2].Value, '(?m)^\s*(\w+)\s*=\s*(.+?),?\s*$')) { $assigned[$line.Groups[1].Value] = $line.Groups[2].Value.Trim() }
        foreach ($name in $facts.FieldNames) {
            if ($assigned.ContainsKey($name)) {
                $value = $assigned[$name]
                if ($arguments.ContainsKey($value)) { $value = $arguments[$value] }
                $facts.Fields[$name] = Format-Value $value
                continue
            }
            # Not set by the seed: the entity's own default.
            $property = [regex]::Match($configuration, "public\s+([\w\?]+)\s+$name\s*\{\s*get;\s*set;\s*\}(\s*=\s*([^;]+);)?")
            if (-not $property.Success) { $facts.Problems += "PayrollConfiguration.$name was not found"; continue }
            if ($property.Groups[3].Success) { $facts.Fields[$name] = Format-Value $property.Groups[3].Value.Trim() }
            elseif ($property.Groups[1].Value -eq "bool") { $facts.Fields[$name] = "false" }
            else { $facts.Fields[$name] = "0" }
        }
    }

    $definitions = [regex]::Match($demoSeed, 'DeductionDefs\s*=\s*\[([\s\S]*?)\];')
    if (-not $definitions.Success) { $facts.Problems += "DemoPayrollSeeder.DeductionDefs was not found" }
    else {
        foreach ($row in [regex]::Matches($definitions.Groups[1].Value, '\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*(true|false)\s*,\s*([\w\.]+)\s*,\s*([\w\.]+)\s*\)')) {
            if ($row.Groups[3].Value -ne "true") { continue }
            $percent = if ($row.Groups[4].Value -eq "null") { "0" } else { Format-Value $row.Groups[4].Value }
            $fixed = if ($row.Groups[5].Value -eq "null") { "0" } else { Format-Value $row.Groups[5].Value }
            $facts.Deductions += "$($row.Groups[1].Value)|$($row.Groups[2].Value)|$percent|$fixed"
        }
    }

    $holidays = [regex]::Match($regimeSeed, 'CreateDefaultHolidays\([\s\S]*?new \(int month, int day, string name\)\[\]\s*\{([\s\S]*?)\};')
    if (-not $holidays.Success) { $facts.Problems += "PayrollRegimeSeed.CreateDefaultHolidays was not found" }
    else {
        foreach ($row in [regex]::Matches($holidays.Groups[1].Value, '\(\s*(\d+)\s*,\s*(\d+)\s*,\s*"([^"]+)"\s*\)')) {
            $facts.Holidays += ("2000-{0:00}-{1:00}|{2}|true" -f [int]$row.Groups[1].Value, [int]$row.Groups[2].Value, $row.Groups[3].Value)
        }
    }

    foreach ($row in [regex]::Matches($acceptanceSeed, 'new DisbursementPriorityRule\s*\{([\s\S]*?)\}')) {
        $body = $row.Groups[1].Value
        $category = [regex]::Match($body, 'Category\s*=\s*"([^"]+)"')
        $priority = [regex]::Match($body, 'Priority\s*=\s*(\d+)')
        $minimum = [regex]::Match($body, 'MinimumAmount\s*=\s*([\d_\.]+m?)')
        $source = [regex]::Match($body, 'LegalSource\s*=\s*"([^"]+)"')
        if (-not ($category.Success -and $priority.Success -and $minimum.Success -and $source.Success)) { $facts.Problems += "a priority rule of AcceptancePayrollSeeder could not be read"; continue }
        $facts.Rules += "$($category.Groups[1].Value)|$($priority.Groups[1].Value)|$(Format-Value $minimum.Groups[1].Value)|$($source.Groups[1].Value)"
    }
    if ($facts.Rules.Count -eq 0) { $facts.Problems += "AcceptancePayrollSeeder holds no priority rule" }
    return $facts
}

function Compare-Rows([string]$what, [string[]]$seed, [string[]]$pack) {
    $problems = @()
    if ($seed.Count -ne $pack.Count) { return @("the pack holds $($pack.Count) $what, the seed $($seed.Count)") }
    for ($i = 0; $i -lt $seed.Count; $i++) {
        if ($seed[$i] -cne $pack[$i]) { $problems += "$what, row $($i + 1): the pack differs from the seed" }
    }
    return $problems
}

# One value of the pack: a reference, a known status, and for an interim value an exception the catalog holds.
function Test-Sourced([string]$what, $value, [string[]]$exceptions, [string[]]$decisions) {
    $problems = @()
    if ($null -eq $value) { return @("${what}: missing") }
    if ([string]::IsNullOrWhiteSpace([string]$value.legalReference)) { $problems += "${what}: no legal reference" }
    elseif (([string]$value.legalReference).StartsWith("LD-") -and $decisions -cnotcontains [string]$value.legalReference) { $problems += "${what}: cites the decision $($value.legalReference), which the pack does not hold" }
    if ($statuses -cnotcontains [string]$value.legalStatus) { $problems += "${what}: the legal status is '$($value.legalStatus)', not Signed or Interim" }
    if ([string]$value.legalStatus -eq "Interim") {
        if ([string]::IsNullOrWhiteSpace([string]$value.interimExceptionId)) { $problems += "${what}: an interim value that cites no exception" }
        elseif ($exceptions -cnotcontains [string]$value.interimExceptionId) { $problems += "${what}: cites the exception $($value.interimExceptionId), which the control catalog does not hold" }
    }
    return $problems
}

function Test-Pack($pack, [string]$schema, $facts, $catalog) {
    $problems = @()
    if ($null -eq $schema -or $schema -notmatch ('"const":\s*"' + [regex]::Escape($schemaName) + '"')) { $problems += "schema.v1.json is missing or does not name $schemaName" }
    if ($null -eq $pack) { return $problems + "law50-interim.json is missing" }
    if ($null -eq $catalog) { return $problems + "docs/str-legal/control-catalog.json is missing" }
    $problems += $facts.Problems
    if ([string]$pack.schema -ne $schemaName) { $problems += "the pack's schema is '$($pack.schema)', not $schemaName" }
    $exceptions = @($catalog.exceptions | ForEach-Object { [string]$_.exceptionId })
    $decisions = @($pack.decisions | ForEach-Object { [string]$_.decisionId })

    $problems += Compare-Rows "tax brackets" $facts.Brackets @($pack.taxBrackets | ForEach-Object { "$(Format-Value $_.upperLimit)|$(Format-Value $_.ratePercent)" })
    $problems += Compare-Rows "statutory deductions" $facts.Deductions @($pack.statutoryDeductions | ForEach-Object { "$($_.code)|$($_.nameAr)|$(Format-Value $_.employeePercent)|$(Format-Value $_.fixedAmount)" })
    $problems += Compare-Rows "holidays" $facts.Holidays @($pack.holidays | ForEach-Object { "$($_.date)|$($_.nameAr)|$(Format-Value $_.isRecurring)" })
    $problems += Compare-Rows "priority rules" $facts.Rules @($pack.priorityRules | ForEach-Object { "$($_.category)|$($_.priority)|$(Format-Value $_.minimumAmount)|$($_.legalReference)" })

    $fields = @{}
    if ($null -ne $pack.fields) { foreach ($property in $pack.fields.PSObject.Properties) { $fields[$property.Name] = $property.Value } }
    if ($fields.Count -ne $facts.FieldNames.Count) { $problems += "the pack holds $($fields.Count) fields, the fingerprint covers $($facts.FieldNames.Count)" }
    foreach ($name in $facts.FieldNames) {
        $key = $name.Substring(0, 1).ToLowerInvariant() + $name.Substring(1)
        if (-not $fields.ContainsKey($key)) { $problems += "the pack has no field $key"; continue }
        if ((Format-Value $fields[$key].value) -cne $facts.Fields[$name]) { $problems += "the field ${key}: the pack differs from the seed" }
        $problems += Test-Sourced "the field $key" $fields[$key] $exceptions $decisions
    }

    $i = 0; foreach ($row in @($pack.taxBrackets)) { $i++; $problems += Test-Sourced "tax bracket $i" $row $exceptions $decisions }
    foreach ($row in @($pack.statutoryDeductions)) { $problems += Test-Sourced "the statutory deduction $($row.code)" $row $exceptions $decisions }
    foreach ($row in @($pack.holidays)) { $problems += Test-Sourced "the holiday $($row.date)" $row $exceptions $decisions }
    foreach ($row in @($pack.priorityRules)) { $problems += Test-Sourced "the priority rule $($row.category)" $row $exceptions $decisions }
    return $problems
}

function Test-Migration([byte[]]$bytes, [string]$text) {
    $problems = @()
    if ($null -eq $bytes -or $null -eq $text) { return @("the migration PAY054_LegalFrameworkPacks is missing") }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $problems += "the migration starts with a byte order mark" }
    $up = $text.Substring(0, [Math]::Max(0, $text.IndexOf("void Down(")))
    $expected = @(
        @("HashSchemaVersion", "PayrollConfigurations", "1"),
        @("ApprovalStatus", "AllowanceRecords", '"Approved"'), @("ApprovalStatus", "DeductionRecords", '"Approved"'), @("ApprovalStatus", "PayrollAdjustments", '"Approved"'),
        @("Origin", "AllowanceRecords", '"System"'), @("Origin", "DeductionRecords", '"System"'), @("Origin", "PayrollAdjustments", '"System"'),
        @("LegalStatus", "TaxBrackets", '"Interim"'), @("LegalStatus", "OfficialHolidays", '"Interim"'), @("LegalStatus", "DisbursementPriorityRules", '"Interim"'),
        @("InterimExceptionIdsJson", "LegalAttestations", '"\[\]"')
    )
    foreach ($column in $expected) {
        $pattern = 'AddColumn<\w+>\(\s*name:\s*"' + $column[0] + '",\s*table:\s*"' + $column[1] + '",[^;]*?nullable:\s*false,\s*defaultValue:\s*' + $column[2] + '\)'
        if ($up -notmatch $pattern) { $problems += "the migration does not set $($column[1]).$($column[0]) of the existing rows by a column default of $($column[2] -replace '\\', '')" }
    }
    foreach ($statement in [regex]::Matches($up, '(?s)migrationBuilder\.Sql\((.*?)\);')) {
        if ($statement.Groups[1].Value -match '(?i)\b(UPDATE|INSERT\s+INTO|DELETE\s+FROM)\b') { $problems += "the migration changes rows: existing rows are set by column defaults only"; break }
    }
    if ($up -notmatch 'EnableRls\(\s*migrationBuilder,\s*"StatutoryDeductionRules",\s*"PayrollConfigurationFieldSources",\s*"PayrollConfigurationEditors"\)') { $problems += "the migration does not enable row security on the three new tables" }
    return $problems
}

function Test-Settings([string]$label, $settings) {
    if ($null -eq $settings) { return @("${label}: the settings file is missing") }
    $problems = @()
    $payroll = $settings.Payroll
    # The base file keeps Payroll's settings where the others do; an older layout nests them, so look one level down too.
    if ($null -eq $payroll -or ($null -eq $payroll.ManualInputs -and $null -eq $payroll.Legal)) {
        foreach ($property in $settings.PSObject.Properties) {
            if ($null -ne $property.Value -and $property.Value.PSObject.Properties.Name -contains "Payroll") { $payroll = $property.Value.Payroll }
        }
    }
    if ($null -eq $payroll -or $null -eq $payroll.ManualInputs -or $payroll.ManualInputs.RequireApproval -ne $true) { $problems += "${label}: Payroll:ManualInputs:RequireApproval is not true (D-PAY-6)" }
    $policy = if ($null -ne $payroll -and $null -ne $payroll.Legal) { [string]$payroll.Legal.AttestorPolicy } else { "" }
    if (@("Payroll.Approve", "Payroll.LegalAttest") -cnotcontains $policy) { $problems += "${label}: Payroll:Legal:AttestorPolicy is '$policy', not a payroll approval policy (D-PAY-12)" }
    return $problems
}

function Get-PayrollSection($settings) {
    if ($null -eq $settings) { return $null }
    return $settings.Payroll
}

function Test-Mirror([string]$compose, $production) {
    if ($null -eq $compose) { return @("docker-compose.acceptance-prodlike.yml is missing") }
    $problems = @()
    $payroll = Get-PayrollSection $production
    if ($null -eq $payroll -or $null -eq $payroll.ManualInputs -or $null -eq $payroll.Legal) { return @("appsettings.Production.json holds no Payroll:ManualInputs or Payroll:Legal to mirror") }
    $approval = ([string]$payroll.ManualInputs.RequireApproval).ToLowerInvariant()
    if ($compose -notmatch ('Payroll__ManualInputs__RequireApproval:\s*"' + $approval + '"')) { $problems += "the prod-like compose does not mirror Payroll__ManualInputs__RequireApproval=$approval" }
    $policy = [regex]::Escape([string]$payroll.Legal.AttestorPolicy)
    if ($compose -notmatch ('Payroll__Legal__AttestorPolicy:\s*"' + $policy + '"')) { $problems += "the prod-like compose does not mirror Payroll__Legal__AttestorPolicy=$($payroll.Legal.AttestorPolicy)" }
    return $problems
}

function Test-Antiforgery([string]$controller) {
    if ($null -eq $controller) { return @("PayrollFrameworksApiController.cs is missing") }
    $problems = @()
    $writes = [regex]::Matches($controller, '(?m)^\s*\[Http(Post|Put|Patch|Delete)(\("[^"]*"\))?\]\s*$')
    if ($writes.Count -eq 0) { $problems += "PayrollFrameworksApiController has no write action: the check reads the wrong file" }
    foreach ($write in $writes) {
        # The attributes of one action: from the verb to the action's signature.
        $rest = $controller.Substring($write.Index)
        $signature = [regex]::Match($rest, '(?m)^\s*public\s')
        $block = if ($signature.Success) { $rest.Substring(0, $signature.Index) } else { $rest }
        if ($block -notmatch '\[ValidateAntiForgeryToken\]') { $problems += "PayrollFrameworksApiController: $($write.Value.Trim()) has no [ValidateAntiForgeryToken]" }
    }
    return $problems
}

function Test-Acceptance($scope, $ownership, $personas) {
    $problems = @()
    if ($null -eq $scope) { return @("tests/acceptance/ci-scope.json is missing") }
    $row = @($scope.prodLike | Where-Object { $_.class -eq "AcPay54LegalFrameworkTests" })
    if ($row.Count -ne 1 -or $row[0].group -ne "main" -or -not $row[0].required) { $problems += "AcPay54LegalFrameworkTests is not a required prod-like class of the main group" }
    $entry = $null
    if ($null -ne $ownership) {
        $ac = $ownership.PSObject.Properties | Where-Object { $_.Name -eq "acs" -or $_.Name -eq "classes" -or $_.Name -eq "entries" } | Select-Object -First 1
        $entries = if ($null -ne $ac) { $ac.Value } else { $ownership }
        $entry = $entries.PSObject.Properties | Where-Object { $_.Name -eq "AC-PAY-54" } | Select-Object -First 1
    }
    if ($null -eq $entry -or $entry.Value.ownerPlan -ne "PLAN-54" -or -not $entry.Value.provesClaim) { $problems += "AC-PAY-54 is not owned by PLAN-54 with provesClaim" }
    $persona = $null
    if ($null -ne $personas) { $persona = @($personas.personas | Where-Object { $_.id -eq "a-payroll-approver-2" }) | Select-Object -First 1 }
    if ($null -eq $persona) { $problems += "the persona a-payroll-approver-2 (the legal attestor) is missing from personas.json" }
    else {
        if (-not $persona.enabled) { $problems += "the persona a-payroll-approver-2 is not enabled" }
        if (@($persona.roles) -cnotcontains "payroll-approver") { $problems += "the persona a-payroll-approver-2 does not hold the role payroll-approver" }
        if (@($persona.roles) -ccontains "payroll-processor" -or @($persona.roles) -ccontains "hr-admin") { $problems += "the persona a-payroll-approver-2 holds an editing role: the attestor must not be able to edit a version" }
        if ($persona.tenantCode -ne "AGENCY-A") { $problems += "the persona a-payroll-approver-2 is not of AGENCY-A" }
        if (@($persona.scenarios) -cnotcontains "AC-PAY-54") { $problems += "the persona a-payroll-approver-2 is not bound to AC-PAY-54" }
    }
    return $problems
}

function Get-Facts {
    return Get-SeedFacts (Read-Text $paths.RegimeSeed) (Read-Text $paths.DemoSeed) (Read-Text $paths.AcceptanceSeed) (Read-Text $paths.Configuration) (Read-Text $paths.FieldSource)
}

function Get-Migration {
    $directory = Join-Path $root $paths.Migrations
    $file = Get-ChildItem -Path $directory -File -Filter "*_PAY054_LegalFrameworkPacks.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $file) { return @{ Bytes = $null; Text = $null } }
    return @{ Bytes = [System.IO.File]::ReadAllBytes($file.FullName); Text = [System.IO.File]::ReadAllText($file.FullName) }
}

function Get-Pack { return Get-Json (Read-Text $paths.Pack) }

function Invoke-All {
    $all = @()
    $all += Test-Pack (Get-Pack) (Read-Text $paths.Schema) (Get-Facts) (Get-Json (Read-Text $paths.Catalog))
    $migration = Get-Migration
    $all += Test-Migration $migration.Bytes $migration.Text
    $all += Test-Settings "appsettings.json" (Get-Json (Read-Text $paths.Base))
    $all += Test-Settings "appsettings.Production.json" (Get-Json (Read-Text $paths.Production))
    $all += Test-Settings "appsettings.Staging.json" (Get-Json (Read-Text $paths.Staging))
    $all += Test-Mirror (Read-Text $paths.ProdLike) (Get-Json (Read-Text $paths.Production))
    $all += Test-Antiforgery (Read-Text $paths.Controller)
    $all += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership)) (Get-Json (Read-Text $paths.Personas))
    return @($all | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $planted = 0
    $schema = Read-Text $paths.Schema
    $facts = Get-Facts
    $catalog = Get-Json (Read-Text $paths.Catalog)
    # $change edits a fresh copy of the pack; the check must then refuse it.
    function Assert-PackRefused([string]$what, [scriptblock]$change) {
        $pack = Get-Pack
        & $change $pack
        $script:planted++
        if ((Test-Pack $pack $schema $facts $catalog).Count -eq 0) { $script:failures += $what }
    }
    Assert-PackRefused "a bracket whose rate differs from the seed was not refused" { param($p) $p.taxBrackets[2].ratePercent = 12 }
    Assert-PackRefused "a pack with one bracket less was not refused" { param($p) $p.taxBrackets = @($p.taxBrackets | Select-Object -First 4) }
    Assert-PackRefused "a statutory percentage that differs from the seed was not refused" { param($p) $p.statutoryDeductions[1].employeePercent = 8 }
    Assert-PackRefused "a field that differs from the seed was not refused" { param($p) $p.fields.dailyRateDivisor.value = 26 }
    Assert-PackRefused "a pack without a governed field was not refused" { param($p) $p.fields.PSObject.Properties.Remove("currencyCode") }
    Assert-PackRefused "a holiday that differs from the seed was not refused" { param($p) $p.holidays[0].date = "2000-01-02" }
    Assert-PackRefused "a priority rule that differs from the seed was not refused" { param($p) $p.priorityRules[0].minimumAmount = 1 }
    Assert-PackRefused "a value without a legal reference was not refused" { param($p) $p.statutoryDeductions[0].legalReference = "" }
    Assert-PackRefused "a value with an unknown status was not refused" { param($p) $p.holidays[1].legalStatus = "Final" }
    Assert-PackRefused "an interim value that cites no exception was not refused" { param($p) $p.taxBrackets[0].interimExceptionId = $null }
    Assert-PackRefused "an interim value that cites an exception outside the catalog was not refused" { param($p) $p.fields.roundingMode.interimExceptionId = "EX-NOT-IN-CATALOG" }
    Assert-PackRefused "a reference to a decision the pack does not hold was not refused" { param($p) $p.taxBrackets[1].legalReference = "LD-NOT-IN-THE-PACK" }
    Assert-PackRefused "a pack of another schema was not refused" { param($p) $p.schema = "payroll-framework-pack.v0" }

    $migration = Get-Migration
    $planted++; if ((Test-Migration ([byte[]](@(0xEF, 0xBB, 0xBF) + $migration.Bytes)) $migration.Text).Count -eq 0) { $failures += "a migration with a byte order mark was not refused" }
    $planted++; if ((Test-Migration $migration.Bytes ($migration.Text -replace '(name:\s*"HashSchemaVersion",[^;]*?defaultValue:\s*)1\)', '${1}2)')).Count -eq 0) { $failures += "a migration that gives existing versions fingerprint schema 2 was not refused" }
    $planted++; if ((Test-Migration $migration.Bytes ($migration.Text -replace 'defaultValue:\s*"Approved"', 'defaultValue: "PendingApproval"')).Count -eq 0) { $failures += "a migration that leaves existing inputs pending was not refused" }
    $planted++; if ((Test-Migration $migration.Bytes ($migration.Text -replace 'protected override void Up\(MigrationBuilder migrationBuilder\)\s*\{', ('$0' + "`n" + '            migrationBuilder.Sql("UPDATE \"PayrollConfigurations\" SET \"HashSchemaVersion\" = 2");'))).Count -eq 0) { $failures += "a migration that updates existing rows was not refused" }
    $rawString = '            migrationBuilder.Sql("""' + "`n" + '                UPDATE "AllowanceRecords" SET "ApprovalStatus" = ''PendingApproval'';' + "`n" + '                """);'
    $planted++; if ((Test-Migration $migration.Bytes ($migration.Text -replace 'protected override void Up\(MigrationBuilder migrationBuilder\)\s*\{', ('$0' + "`n" + $rawString))).Count -eq 0) { $failures += "a migration that updates existing rows in a raw string was not refused" }
    $planted++; if ((Test-Migration $migration.Bytes ($migration.Text -replace 'EnableRls\(', 'Nothing(')).Count -eq 0) { $failures += "new tables without row security were not refused" }

    $production = Get-Json (Read-Text $paths.Production)
    $production.Payroll.ManualInputs.RequireApproval = $false
    $planted++; if ((Test-Settings "planted" $production).Count -eq 0) { $failures += "RequireApproval=false in Production was not refused" }
    $production = Get-Json (Read-Text $paths.Production)
    $production.Payroll.Legal.AttestorPolicy = "Module.Payroll"
    $planted++; if ((Test-Settings "planted" $production).Count -eq 0) { $failures += "an attestor policy that any payroll user meets was not refused" }
    $compose = Read-Text $paths.ProdLike
    $planted++; if ((Test-Mirror ($compose -replace 'Payroll__ManualInputs__RequireApproval:\s*"true"', 'Payroll__ManualInputs__RequireApproval: "false"') (Get-Json (Read-Text $paths.Production))).Count -eq 0) { $failures += "a prod-like mirror without the manual input approval was not refused" }
    $planted++; if ((Test-Mirror ($compose -replace '(?m)^\s*Payroll__Legal__AttestorPolicy:.*$', '') (Get-Json (Read-Text $paths.Production))).Count -eq 0) { $failures += "a prod-like mirror without the attestor policy was not refused" }

    $controller = Read-Text $paths.Controller
    $withoutOne = [regex]::Replace($controller, '(\[HttpPost\("\{id:guid\}/publish"\)\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($withoutOne -eq $controller -or (Test-Antiforgery $withoutOne).Count -eq 0) { $failures += "a publish action without the antiforgery check was not refused" }
    $withoutPut = [regex]::Replace($controller, '(\[HttpPut\("[^"]*"\)\]\s*)\[ValidateAntiForgeryToken\]', '${1}', 1)
    $planted++; if ($withoutPut -eq $controller -or (Test-Antiforgery $withoutPut).Count -eq 0) { $failures += "a PUT action without the antiforgery check was not refused" }

    $ownership = Get-Json (Read-Text $paths.Ownership)
    $personas = Get-Json (Read-Text $paths.Personas)
    $scope = Get-Json (Read-Text $paths.Scope)
    $scope.prodLike = @($scope.prodLike | Where-Object { $_.class -ne "AcPay54LegalFrameworkTests" })
    $planted++; if ((Test-Acceptance $scope $ownership $personas).Count -eq 0) { $failures += "a scope without AC-PAY-54 was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    ($scope.prodLike | Where-Object { $_.class -eq "AcPay54LegalFrameworkTests" }).required = $false
    $planted++; if ((Test-Acceptance $scope $ownership $personas).Count -eq 0) { $failures += "AC-PAY-54 not required was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $personas.personas = @($personas.personas | Where-Object { $_.id -ne "a-payroll-approver-2" })
    $planted++; if ((Test-Acceptance $scope $ownership $personas).Count -eq 0) { $failures += "a persona file without the attestor was not refused" }
    $personas = Get-Json (Read-Text $paths.Personas)
    ($personas.personas | Where-Object { $_.id -eq "a-payroll-approver-2" }).roles = @("payroll", "payroll-approver", "payroll-processor")
    $planted++; if ((Test-Acceptance $scope $ownership $personas).Count -eq 0) { $failures += "an attestor who can also edit was not refused" }

    $own = Invoke-All
    if ($own.Count -ne 0) { $failures += "the repository itself does not pass: $($own -join '; ')" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] pay054-validate self-test: $planted planted defects refused, the repository passes" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] pay054-validate: the interim pack equals the seeds and every value is sourced, PAY054 sets existing rows by defaults, the manual input and attestor settings and their mirror, the antiforgery check on every framework write, AC-PAY-54 required with its attestor persona" -ForegroundColor Green
exit 0
