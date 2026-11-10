# PLAN-49: what the policy engine governs, checked from the files (no stack needed).
#  1. PolicyDomainCatalog describes every constant of PolicyDomains once: five governed (owner decision D-POL-1), fifteen out
#     of scope, each out-of-scope one with a reason.
#  2. docs/POLICY_DOMAIN_REGISTRY.md lists the same twenty domains with the same status, and the same reason for each
#     out-of-scope domain (the reasons are Arabic: both files are read as UTF-8 and compared, the script holds no Arabic).
#  3. The policy page reads the catalog and keeps no list of its own.
#  4. Only the owner service of a governed domain registers the enforce client, and each one that registers it injects it.
#  5. No appsettings file, compose file or deployment file sets a mode for an out-of-scope domain.
#  6. Unless -SkipTests: the unit tests that hold the same rules against the compiled catalog (Pol049PolicyDomainCatalogTests).
# Usage (PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol049-validate.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol049-validate.ps1 -SkipTests
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Text([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path)) { $script:failures.Add("missing $relative"); return "" }
    return [System.IO.File]::ReadAllText($path, $utf8)
}

function Get-Relative([string]$fullName) {
    return $fullName.Substring($root.Length + 1).Replace('\', '/')
}

# D-POL-1, as the owner accepted it on 2026-10-31.
$governedByDecision = @("Assets.Custody", "Contracts.Leave", "Core.Establishment", "Core.Recruitment", "IDCard.Lifecycle")

# 1. The catalog against the list of names.
$domainsSource = Read-Text "src/Shared/HRManagement.SharedKernel/Policy/PolicyDomains.cs"
$catalogSource = Read-Text "src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainCatalog.cs"
$constants = @{}
foreach ($m in [regex]::Matches($domainsSource, 'public const string (\w+) = "([^"]+)";')) { $constants[$m.Groups[1].Value] = $m.Groups[2].Value }
if ($constants.Count -ne 20) { $failures.Add("PolicyDomains.cs holds $($constants.Count) domain constants, expected 20") }

$catalogConstants = @{}
foreach ($m in [regex]::Matches($catalogSource, 'public const string (\w+) = "([^"]+)";')) { $catalogConstants[$m.Groups[1].Value] = $m.Groups[2].Value }
function Resolve-Owner([string]$token) {
    $t = $token.Trim()
    if ($t.StartsWith('"')) { return $t.Trim('"') }
    if ($catalogConstants.ContainsKey($t)) { return $catalogConstants[$t] }
    $script:failures.Add("PolicyDomainCatalog.cs: owner '$t' is neither a literal nor a constant of the catalog")
    return $t
}

$governed = @{}
foreach ($m in [regex]::Matches($catalogSource, 'Domain = PolicyDomains\.(\w+),\s*Status = PolicyDomainStatus\.Governed,\s*OwnerService = ([\w"\.]+),')) {
    $name = $m.Groups[1].Value
    if (-not $constants.ContainsKey($name)) { $failures.Add("the catalog governs PolicyDomains.$name, which is not a constant"); continue }
    $governed[$constants[$name]] = Resolve-Owner $m.Groups[2].Value
}
$outOfScope = @{}
$reasons = @{}
foreach ($m in [regex]::Matches($catalogSource, 'OutOfScope\(PolicyDomains\.(\w+),\s*([\w"\.]+),\s*"([^"]*)"\)')) {
    $name = $m.Groups[1].Value
    if (-not $constants.ContainsKey($name)) { $failures.Add("the catalog retires PolicyDomains.$name, which is not a constant"); continue }
    $outOfScope[$constants[$name]] = Resolve-Owner $m.Groups[2].Value
    $reasons[$constants[$name]] = $m.Groups[3].Value
}

if ($governed.Count -ne 5) { $failures.Add("the catalog governs $($governed.Count) domains, expected 5") }
if ($outOfScope.Count -ne 15) { $failures.Add("the catalog puts $($outOfScope.Count) domains out of scope, expected 15") }
foreach ($domain in $constants.Values) {
    $isGoverned = $governed.ContainsKey($domain)
    $isOut = $outOfScope.ContainsKey($domain)
    if ($isGoverned -and $isOut) { $failures.Add("$domain is both governed and out of scope in the catalog") }
    if (-not $isGoverned -and -not $isOut) { $failures.Add("$domain has no descriptor in the catalog") }
    if ($isGoverned -ne ($governedByDecision -contains $domain)) { $failures.Add("${domain}: the catalog's status differs from owner decision D-POL-1") }
}
foreach ($domain in $outOfScope.Keys) {
    if ([string]::IsNullOrWhiteSpace($reasons[$domain])) { $failures.Add("$domain is out of scope with no reason") }
}

# 2. The registry document.
$registry = Read-Text "docs/POLICY_DOMAIN_REGISTRY.md"
if ($registry -notmatch '(?m)^## Governed domains \(5\)\s*$') { $failures.Add("POLICY_DOMAIN_REGISTRY.md has no 'Governed domains (5)' heading") }
if ($registry -notmatch '(?m)^## Out of scope \(15\)\s*$') { $failures.Add("POLICY_DOMAIN_REGISTRY.md has no 'Out of scope (15)' heading") }
$outHeading = $registry.IndexOf("## Out of scope")
$governedPart = if ($outHeading -ge 0) { $registry.Substring(0, $outHeading) } else { $registry }
$outPart = if ($outHeading -ge 0) { $registry.Substring($outHeading) } else { "" }
$tick = [string][char]0x60
foreach ($domain in $governed.Keys) {
    $row = "| " + $tick + $domain + $tick + " | " + $governed[$domain] + " |"
    if (-not $governedPart.Contains($row)) { $failures.Add("POLICY_DOMAIN_REGISTRY.md: no governed row for $domain owned by $($governed[$domain])") }
    if ($outPart.Contains($tick + $domain + $tick)) { $failures.Add("POLICY_DOMAIN_REGISTRY.md lists governed $domain as out of scope") }
}
foreach ($domain in $outOfScope.Keys) {
    $row = "| " + $tick + $domain + $tick + " | " + $outOfScope[$domain] + " | " + $reasons[$domain] + " |"
    if (-not $outPart.Contains($row)) { $failures.Add("POLICY_DOMAIN_REGISTRY.md: the out-of-scope row of $domain differs from the catalog (service or reason)") }
    if ($governedPart.Contains($tick + $domain + $tick)) { $failures.Add("POLICY_DOMAIN_REGISTRY.md lists out-of-scope $domain as governed") }
}
$rows = [regex]::Matches($registry, '(?m)^\| `[A-Za-z]+\.[A-Za-z]+` \|').Count
if ($rows -ne 20) { $failures.Add("POLICY_DOMAIN_REGISTRY.md has $rows domain rows, expected 20") }

# 3. The page.
$controller = Read-Text "src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs"
$view = Read-Text "src/Services/HRCore/HR.Core.Web/Views/PolicyTemplates/Index.cshtml"
if ($controller -match 'PolicyDomainRegistry') { $failures.Add("PolicyTemplatesController still names PolicyDomainRegistry") }
if (-not $controller.Contains("PolicyDomainCatalog.All")) { $failures.Add("PolicyTemplatesController does not read PolicyDomainCatalog.All") }
if (-not $view.Contains("PolicyDomainRow")) { $failures.Add("PolicyTemplates/Index.cshtml does not render the catalog rows") }
foreach ($domain in $constants.Values) {
    if ($view.Contains($domain)) { $failures.Add("PolicyTemplates/Index.cshtml names $domain itself") }
    if ($controller.Contains('"' + $domain + '"')) { $failures.Add("PolicyTemplatesController names $domain itself") }
}

# 4. The enforce client: registered only by the owner of a governed domain outside HR.Core, and injected where registered.
$expectedFolders = @($governed.Keys | Where-Object { -not $_.StartsWith("Core.") } | ForEach-Object { $governed[$_].Replace(".", "") } | Sort-Object -Unique)
$sources = @(Get-ChildItem -Path (Join-Path $root "src") -Recurse -Include *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](Tests|bin|obj)[\\/]' })
$registering = New-Object System.Collections.Generic.List[string]
$injecting = New-Object System.Collections.Generic.List[string]
foreach ($file in $sources) {
    $relative = Get-Relative $file.FullName
    $text = [System.IO.File]::ReadAllText($file.FullName, $utf8)
    $parts = $relative.Split('/')
    $folder = if ($parts.Length -gt 2 -and $parts[1] -eq "Services") { $parts[2] } else { $parts[1] }
    if ($text.Contains(".AddHrPolicyEnforceClient(")) {
        if ($relative -like "src/Shared/*") { $failures.Add("$relative registers the enforce client from a shared library") }
        else { $registering.Add($folder) }
    }
    if ($relative -like "src/Services/*" -and $text -match 'IPolicyEnforceClient \w+') { $injecting.Add($folder) }
}
$registeringFolders = @($registering | Sort-Object -Unique)
if (($registeringFolders -join ",") -ne ($expectedFolders -join ",")) {
    $failures.Add("the enforce client is registered by [$($registeringFolders -join ', ')], and the governed owners are [$($expectedFolders -join ', ')]")
}
foreach ($folder in $registeringFolders) {
    if (-not ($injecting -contains $folder)) { $failures.Add("$folder registers the enforce client and never injects it") }
}

# 5. No mode key for an out-of-scope domain in any configuration that ships.
$configFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
Get-ChildItem -Path (Join-Path $root "src") -Recurse -Filter "appsettings*.json" -File |
    Where-Object { $_.FullName -notmatch '[\\/](Tests|bin|obj)[\\/]' } | ForEach-Object { $configFiles.Add($_) }
Get-ChildItem -Path $root -Filter "docker-compose*.yml" -File | ForEach-Object { $configFiles.Add($_) }
if (Test-Path (Join-Path $root "deploy")) {
    Get-ChildItem -Path (Join-Path $root "deploy") -Recurse -File |
        Where-Object { $_.Extension -in @(".yml", ".yaml", ".json", ".example", ".env") -or $_.Name -like ".env*" } | ForEach-Object { $configFiles.Add($_) }
}
foreach ($file in $configFiles) {
    $relative = Get-Relative $file.FullName
    $text = [System.IO.File]::ReadAllText($file.FullName, $utf8)
    foreach ($domain in $outOfScope.Keys) {
        if ($text.Contains("PolicyEngine__" + $domain) -or $text.Contains("PolicyEngine:" + $domain)) {
            $failures.Add("$relative sets a PolicyEngine key for out-of-scope $domain")
        }
    }
    if ($file.Name -like "appsettings*.json" -and $text.Contains('"PolicyEngine"')) {
        $section = $null
        try { $section = ($text | ConvertFrom-Json).PolicyEngine } catch { $failures.Add("$relative is not valid JSON") }
        if ($null -ne $section) {
            foreach ($key in @($section.PSObject.Properties | ForEach-Object { $_.Name })) {
                if ($outOfScope.ContainsKey($key)) { $failures.Add("$relative sets PolicyEngine:$key, an out-of-scope domain") }
            }
        }
    }
}

# 6. The same rules against the compiled catalog.
if (-not $SkipTests -and $failures.Count -eq 0) {
    Push-Location $root
    try {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $output = (& dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pol049PolicyDomainCatalogTests" --nologo 2>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
        $ErrorActionPreference = $previous
        if ($code -ne 0) {
            $failures.Add("Pol049PolicyDomainCatalogTests failed (dotnet test exit $code)")
            $output | Where-Object { $_ -match 'Failed |error' } | Select-Object -First 20 | ForEach-Object { Write-Output ("  " + $_) }
        }
    }
    finally { Pop-Location }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Output "FAIL: $_" }
    Write-Output "pol049-validate: FAIL ($($failures.Count))"
    exit 1
}
Write-Output ("pol049-validate: PASS (governed {0}, out of scope {1}, enforce client in [{2}])" -f $governed.Count, $outOfScope.Count, ($registeringFolders -join ", "))
exit 0
