#Requires -Version 5.1
<#
.SYNOPSIS
  Renders the local Keycloak import. Acceptance mode provisions the canonical
  Acceptance v2 personas and keeps distinct passwords outside Git.
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [ValidateSet("Demo", "Acceptance")]
    [string]$Profile = "Demo",
    [string]$DemoPassword = "HrAdmin123!",
    [switch]$RotateAcceptanceCredentials
)

$ErrorActionPreference = "Stop"

function Read-DotEnv([string]$Path) {
    if (-not (Test-Path $Path)) { throw ".env not found at $Path. Copy .env.example and fill KEYCLOAK_HR_GATEWAY_CLIENT_SECRET." }
    $result = @{}
    Get-Content $Path | ForEach-Object {
        if ($_ -match '^\s*#' -or $_ -match '^\s*$') { return }
        $i = $_.IndexOf('=')
        if ($i -lt 1) { return }
        $key = $_.Substring(0, $i).Trim()
        $value = $_.Substring($i + 1).Trim()
        if ($value.StartsWith('"') -and $value.EndsWith('"') -and $value.Length -ge 2) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $result[$key] = $value
    }
    return $result
}

function New-AcceptancePassword {
    $bytes = New-Object byte[] 18
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $hex = -join ($bytes | ForEach-Object { $_.ToString("x2") })
    return "Av2!$hex"
}

function Get-AcceptanceKeycloakUserId([string]$Username) {
    $payload = [System.Text.Encoding]::UTF8.GetBytes(
        "HRM|acceptance-v2|acceptance-v2|KEYCLOAK|$Username")
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($payload) } finally { $sha.Dispose() }
    $guidBytes = New-Object byte[] 16
    [Array]::Copy($hash, $guidBytes, 16)
    $guidBytes[6] = [byte](($guidBytes[6] -band 0x0F) -bor 0x40)
    $guidBytes[8] = [byte](($guidBytes[8] -band 0x3F) -bor 0x80)
    return (New-Object System.Guid (,$guidBytes)).ToString("D")
}

function Get-PropertyValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

# The acceptance persona file stores the stable site/ordinal pair. Resolve that pair
# through the same deterministic employee topology used by AcceptanceSeedCatalog so
# ESS tokens can point at the canonical HR.Core employee without duplicating IDs.
$acceptanceSiteEmployeeCounts = @{
    "AGENCY-A" = [ordered]@{ HQ = 326; RIF = 271; ALEP = 213; HOMS = 167; HAMA = 103; LAT = 79; TAR = 51 }
    "AGENCY-B" = [ordered]@{ HQ = 118; ALEP = 73; HOMS = 49; HAMA = 31 }
    "AGENCY-C" = [ordered]@{ HQ = 63; RIF = 44; LAT = 29 }
}

function Get-AcceptanceEmployeeId($Persona) {
    $agency = [string]$Persona.tenantCode
    $site = [string]$Persona.employeeSiteCode
    if ([string]::IsNullOrWhiteSpace($agency) -or [string]::IsNullOrWhiteSpace($site)) {
        return $null
    }

    $ordinal = if ($null -eq $Persona.employeeOrdinal) { 0 } else { [int]$Persona.employeeOrdinal }
    if ($agency -eq "AGENCY-A" -and $site -eq "RIF" -and $ordinal -eq 0) { return "a0000001-0000-4000-8000-000000000201" }
    if ($agency -eq "AGENCY-A" -and $site -eq "RIF" -and $ordinal -eq 1) { return "a0000001-0000-4000-8000-000000000202" }
    if ($agency -eq "AGENCY-A" -and $site -eq "ALEP" -and $ordinal -eq 0) { return "a0000001-0000-4000-8000-000000000203" }
    if ($agency -eq "AGENCY-B" -and $site -eq "HQ" -and $ordinal -eq 0) { return "a0000002-0000-4000-8000-000000000201" }

    if (-not $acceptanceSiteEmployeeCounts.ContainsKey($agency)) {
        throw "Unknown acceptance agency '$agency' for persona '$($Persona.id)'."
    }
    $sites = $acceptanceSiteEmployeeCounts[$agency]
    if (-not $sites.Contains($site)) {
        throw "Unknown acceptance site '$site' for persona '$($Persona.id)'."
    }
    if ($ordinal -lt 0 -or $ordinal -ge [int]$sites[$site]) {
        throw "Employee ordinal '$ordinal' is outside site '$site' for persona '$($Persona.id)'."
    }

    $globalIndex = $ordinal
    foreach ($entry in $sites.GetEnumerator()) {
        if ($entry.Key -eq $site) { break }
        $globalIndex += [int]$entry.Value
    }
    $value = "{0}|employee|{1:D5}" -f $agency, $globalIndex
    $payload = [System.Text.Encoding]::UTF8.GetBytes("HRM|acceptance-v2|$value")
    # SHA256.Create().ComputeHash (not the .NET 5+ static HashData) so Windows PowerShell 5.1 can run it (#Requires 5.1).
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($payload) } finally { $sha.Dispose() }
    $guidBytes = [byte[]]$hash[0..15]
    $guidBytes[6] = [byte](($guidBytes[6] -band 0x0F) -bor 0x40)
    $guidBytes[8] = [byte](($guidBytes[8] -band 0x3F) -bor 0x80)
    return (New-Object System.Guid (,$guidBytes)).ToString("D")
}

$vars = Read-DotEnv (Join-Path $RepoRoot ".env")
$gatewaySecret = $vars['KEYCLOAK_HR_GATEWAY_CLIENT_SECRET']
if ([string]::IsNullOrWhiteSpace($gatewaySecret)) {
    throw "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET is missing in .env"
}

$templatePath = Join-Path $RepoRoot "docker/keycloak/realm-hrmanagement-dev.template.json"
$importDir = Join-Path $RepoRoot "docker/keycloak/import"
$outPath = Join-Path $importDir "realm-hrmanagement.json"
New-Item -ItemType Directory -Force -Path $importDir | Out-Null

$templateJson = Get-Content $templatePath -Raw -Encoding UTF8
$templateJson = $templateJson.Replace("__KEYCLOAK_HR_GATEWAY_CLIENT_SECRET__", $gatewaySecret)
$templateJson = $templateJson.Replace("__DEMO_HRADMIN_PASSWORD__", $DemoPassword)
$templateJson = $templateJson.Replace("__SVC_SECRET__", "dev-svc-secret-local-only")

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
if ($Profile -eq "Demo") {
    $null = $templateJson | ConvertFrom-Json
    [System.IO.File]::WriteAllText($outPath, $templateJson, $utf8NoBom)
    Write-Host "Wrote $outPath"
    Write-Host "Demo user: hradmin / $DemoPassword (role hr-admin)"
    return
}

$personasPath = Join-Path $RepoRoot "testdata/acceptance/personas.json"
$personaFile = Get-Content $personasPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($personaFile.schemaVersion -ne "acceptance-v2" -or @($personaFile.personas).Count -ne 61) {
    throw "Acceptance persona catalog must use acceptance-v2 and contain exactly 61 personas."
}

$localDir = Join-Path $RepoRoot ".acceptance"
$credentialsPath = Join-Path $localDir "credentials.json"
$accountsMarkdownPath = Join-Path $localDir "accounts.local.md"
$accountsCsvPath = Join-Path $localDir "accounts.local.csv"
New-Item -ItemType Directory -Force -Path $localDir | Out-Null

$passwords = [ordered]@{}
$existingState = $null
if ((Test-Path $credentialsPath) -and -not $RotateAcceptanceCredentials) {
    $existingState = Get-Content $credentialsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($property in $existingState.passwords.PSObject.Properties) {
        $passwords[$property.Name] = [string]$property.Value
    }
}

$gatewayCallerProofSecret = [string](Get-PropertyValue $existingState "gatewayCallerProofSecret")
if ($RotateAcceptanceCredentials -or [string]::IsNullOrWhiteSpace($gatewayCallerProofSecret)) {
    $gatewayCallerProofSecret = New-AcceptancePassword
}

$usedPasswords = New-Object System.Collections.Generic.HashSet[string]
foreach ($value in $passwords.Values) { $null = $usedPasswords.Add([string]$value) }
foreach ($persona in $personaFile.personas) {
    if (-not $passwords.Contains($persona.username) -or $RotateAcceptanceCredentials) {
        do { $candidate = New-AcceptancePassword } while (-not $usedPasswords.Add($candidate))
        $passwords[$persona.username] = $candidate
    }
}
$credentialState = [ordered]@{
    schemaVersion = "acceptance-v2"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    gatewayCallerProofSecret = $gatewayCallerProofSecret
    passwords = $passwords
}
[System.IO.File]::WriteAllText(
    $credentialsPath,
    ($credentialState | ConvertTo-Json -Depth 5),
    $utf8NoBom)

$realm = $templateJson | ConvertFrom-Json
$realm.displayName = "HR Management Acceptance v2"
$roleNames = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
$roles = New-Object System.Collections.Generic.List[object]
foreach ($role in @($realm.roles.realm)) {
    if ($roleNames.Add([string]$role.name)) { $roles.Add($role) }
}
foreach ($persona in $personaFile.personas) {
    foreach ($roleName in @($persona.roles)) {
        if ($roleNames.Add([string]$roleName)) {
            $roles.Add([PSCustomObject]@{
                name = [string]$roleName
                description = "Acceptance v2 role: $roleName"
                composite = $false
                clientRole = $false
                containerId = "hrmanagement"
                attributes = [PSCustomObject]@{}
            })
        }
    }
}
$realm.roles.realm = [object[]]$roles.ToArray()

$users = New-Object System.Collections.Generic.List[object]
$guideRows = New-Object System.Collections.Generic.List[object]
foreach ($persona in $personaFile.personas) {
    $password = [string]$passwords[$persona.username]
    $override = [Environment]::GetEnvironmentVariable([string]$persona.passwordEnv)
    if (-not [string]::IsNullOrWhiteSpace($override)) { $password = $override }

    $attributes = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace([string]$persona.tenantId)) {
        $attributes["tenant_id"] = @([string]$persona.tenantId)
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$persona.scopeOrgUnitId)) {
        $attributes["scope_org_unit_id"] = @([string]$persona.scopeOrgUnitId)
    }
    $employeeId = Get-AcceptanceEmployeeId $persona
    if (-not [string]::IsNullOrWhiteSpace([string]$employeeId)) {
        $attributes["employee_id"] = @([string]$employeeId)
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$persona.fixture)) {
        $attributes["acceptance_fixture"] = @([string]$persona.fixture)
    }

    $realmRoles = @($persona.roles | ForEach-Object { [string]$_ })
    if ($realmRoles -notcontains "offline_access") { $realmRoles += "offline_access" }
    $users.Add([PSCustomObject]@{
        id = Get-AcceptanceKeycloakUserId ([string]$persona.username)
        username = [string]$persona.username
        enabled = [bool]$persona.enabled
        emailVerified = $true
        firstName = "Acceptance"
        lastName = [string]$persona.id
        email = "$($persona.username)@acceptance.test"
        credentials = @([PSCustomObject]@{ type = "password"; value = $password; temporary = $false })
        realmRoles = $realmRoles
        attributes = [PSCustomObject]$attributes
    })

    $guideRows.Add([PSCustomObject]@{
        Username = [string]$persona.username
        Password = $password
        Enabled = [bool]$persona.enabled
        Tenant = if ([string]::IsNullOrWhiteSpace([string]$persona.tenantCode)) { "(missing)" } else { [string]$persona.tenantCode }
        Scope = if ([string]::IsNullOrWhiteSpace([string]$persona.scopeOrgUnitId)) { "(all)" } else { [string]$persona.scopeOrgUnitId }
        Roles = (@($persona.roles) -join ", ")
        Purpose = [string]$persona.purpose
    })
}
$realm.users = [object[]]$users.ToArray()

$realmJson = $realm | ConvertTo-Json -Depth 100
$null = $realmJson | ConvertFrom-Json
[System.IO.File]::WriteAllText($outPath, $realmJson, $utf8NoBom)

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add("# Acceptance v2 local accounts")
$markdown.Add("")
$markdown.Add("Generated locally. This file is excluded from Git. Disabled and quarantined personas are expected to reject authentication.")
$markdown.Add("")
$markdown.Add("| Username | Password | Enabled | Tenant | Scope | Roles | Purpose |")
$markdown.Add("|---|---|---:|---|---|---|---|")
foreach ($row in $guideRows) {
    $markdown.Add("| $($row.Username) | $($row.Password) | $($row.Enabled) | $($row.Tenant) | $($row.Scope) | $($row.Roles) | $($row.Purpose) |")
}
[System.IO.File]::WriteAllLines($accountsMarkdownPath, $markdown, $utf8NoBom)
[System.IO.File]::WriteAllText($accountsCsvPath, (($guideRows | ConvertTo-Csv -NoTypeInformation) -join [Environment]::NewLine), $utf8NoBom)

Write-Host "Wrote Acceptance v2 Keycloak realm with 61 personas: $outPath"
Write-Host "Local credentials and account guide: $localDir"
Write-Host "Passwords were not printed and are excluded from Git."
