#Requires -Version 5.1
# PLAN-39: the Acceptance users of the realm, dot-sourced by render-realm.ps1 (moved here from
# prepare-keycloak-import.ps1, unchanged in behaviour). Every persona of testdata/acceptance/personas.json becomes a
# user with a distinct lab password kept outside Git in .acceptance/credentials.json (plus the local account guides).

function New-AcceptancePassword {
    $bytes = New-Object byte[] 18
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $hex = -join ($bytes | ForEach-Object { $_.ToString("x2") })
    return "Av2!$hex"
}

function Get-AcceptanceHashGuid([string]$Text) {
    $payload = [System.Text.Encoding]::UTF8.GetBytes($Text)
    # SHA256.Create().ComputeHash (not the .NET 5+ static HashData) so Windows PowerShell 5.1 can run it.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($payload) } finally { $sha.Dispose() }
    $guidBytes = New-Object byte[] 16
    [Array]::Copy($hash, $guidBytes, 16)
    $guidBytes[6] = [byte](($guidBytes[6] -band 0x0F) -bor 0x40)
    $guidBytes[8] = [byte](($guidBytes[8] -band 0x3F) -bor 0x80)
    return (New-Object System.Guid (, $guidBytes)).ToString("D")
}

# C# twin: AcceptanceKeycloakIds (HRManagement.ServiceDefaults/Seeding).
function Get-AcceptanceKeycloakUserId([string]$Username) {
    return Get-AcceptanceHashGuid "HRM|acceptance-v2|acceptance-v2|KEYCLOAK|$Username"
}

# The persona file stores the stable site/ordinal pair. Resolve it through the same deterministic employee topology
# that AcceptanceSeedCatalog uses, so ESS tokens point at the canonical HR.Core employee.
$script:AcceptanceSiteEmployeeCounts = @{
    "AGENCY-A" = [ordered]@{ HQ = 326; RIF = 271; ALEP = 213; HOMS = 167; HAMA = 103; LAT = 79; TAR = 51 }
    "AGENCY-B" = [ordered]@{ HQ = 118; ALEP = 73; HOMS = 49; HAMA = 31 }
    "AGENCY-C" = [ordered]@{ HQ = 63; RIF = 44; LAT = 29 }
}

function Get-AcceptanceEmployeeId($Persona) {
    $agency = [string]$Persona.tenantCode
    $site = [string]$Persona.employeeSiteCode
    if ([string]::IsNullOrWhiteSpace($agency) -or [string]::IsNullOrWhiteSpace($site)) { return $null }

    $ordinal = if ($null -eq $Persona.employeeOrdinal) { 0 } else { [int]$Persona.employeeOrdinal }
    if ($agency -eq "AGENCY-A" -and $site -eq "RIF" -and $ordinal -eq 0) { return "a0000001-0000-4000-8000-000000000201" }
    if ($agency -eq "AGENCY-A" -and $site -eq "RIF" -and $ordinal -eq 1) { return "a0000001-0000-4000-8000-000000000202" }
    if ($agency -eq "AGENCY-A" -and $site -eq "ALEP" -and $ordinal -eq 0) { return "a0000001-0000-4000-8000-000000000203" }
    if ($agency -eq "AGENCY-B" -and $site -eq "HQ" -and $ordinal -eq 0) { return "a0000002-0000-4000-8000-000000000201" }

    if (-not $script:AcceptanceSiteEmployeeCounts.ContainsKey($agency)) { throw "Unknown acceptance agency '$agency' for persona '$($Persona.id)'." }
    $sites = $script:AcceptanceSiteEmployeeCounts[$agency]
    if (-not $sites.Contains($site)) { throw "Unknown acceptance site '$site' for persona '$($Persona.id)'." }
    if ($ordinal -lt 0 -or $ordinal -ge [int]$sites[$site]) { throw "Employee ordinal '$ordinal' is outside site '$site' for persona '$($Persona.id)'." }

    $globalIndex = $ordinal
    foreach ($entry in $sites.GetEnumerator()) {
        if ($entry.Key -eq $site) { break }
        $globalIndex += [int]$entry.Value
    }
    return Get-AcceptanceHashGuid ("HRM|acceptance-v2|{0}|employee|{1:D5}" -f $agency, $globalIndex)
}

function Get-AcceptancePersonaUsers {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [switch]$Rotate,
        [switch]$NoCredentialFiles
    )
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    $personaFile = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "testdata/acceptance/personas.json"), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($personaFile.schemaVersion -ne "acceptance-v2" -or @($personaFile.personas).Count -ne 61) {
        throw "Acceptance persona catalog must use acceptance-v2 and contain exactly 61 personas."
    }

    $passwords = [ordered]@{}
    $gatewayCallerProofSecret = ""
    $localDir = Join-Path $RepoRoot ".acceptance"
    $credentialsPath = Join-Path $localDir "credentials.json"
    if (-not $NoCredentialFiles) {
        New-Item -ItemType Directory -Force -Path $localDir | Out-Null
        if ((Test-Path $credentialsPath) -and -not $Rotate) {
            $existing = [System.IO.File]::ReadAllText($credentialsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            foreach ($property in $existing.passwords.PSObject.Properties) { $passwords[$property.Name] = [string]$property.Value }
            if ($null -ne $existing.PSObject.Properties["gatewayCallerProofSecret"]) { $gatewayCallerProofSecret = [string]$existing.gatewayCallerProofSecret }
        }
        if ($Rotate -or [string]::IsNullOrWhiteSpace($gatewayCallerProofSecret)) { $gatewayCallerProofSecret = New-AcceptancePassword }
    }

    $used = New-Object System.Collections.Generic.HashSet[string]
    foreach ($value in $passwords.Values) { $null = $used.Add([string]$value) }
    foreach ($persona in $personaFile.personas) {
        if (-not $passwords.Contains($persona.username) -or $Rotate) {
            if ($NoCredentialFiles) {
                # Test renders only: a fixed lab value per persona, no file written.
                $passwords[$persona.username] = "Av2!test-" + (Get-AcceptanceHashGuid "test|$($persona.username)").Replace("-", "")
            }
            else {
                do { $candidate = New-AcceptancePassword } while (-not $used.Add($candidate))
                $passwords[$persona.username] = $candidate
            }
        }
    }

    $users = New-Object System.Collections.Generic.List[object]
    $guideRows = New-Object System.Collections.Generic.List[object]
    foreach ($persona in $personaFile.personas) {
        $password = [string]$passwords[$persona.username]
        $override = [Environment]::GetEnvironmentVariable([string]$persona.passwordEnv)
        if (-not [string]::IsNullOrWhiteSpace($override)) { $password = $override }

        $attributes = [ordered]@{}
        if (-not [string]::IsNullOrWhiteSpace([string]$persona.tenantId)) { $attributes["tenant_id"] = @([string]$persona.tenantId) }
        if (-not [string]::IsNullOrWhiteSpace([string]$persona.scopeOrgUnitId)) { $attributes["scope_org_unit_id"] = @([string]$persona.scopeOrgUnitId) }
        $employeeId = Get-AcceptanceEmployeeId $persona
        if (-not [string]::IsNullOrWhiteSpace([string]$employeeId)) { $attributes["employee_id"] = @([string]$employeeId) }
        if (-not [string]::IsNullOrWhiteSpace([string]$persona.fixture)) { $attributes["acceptance_fixture"] = @([string]$persona.fixture) }

        $realmRoles = @($persona.roles | ForEach-Object { [string]$_ })
        if ($realmRoles -notcontains "offline_access") { $realmRoles += "offline_access" }
        $users.Add([ordered]@{
            id            = Get-AcceptanceKeycloakUserId ([string]$persona.username)
            username      = [string]$persona.username
            enabled       = [bool]$persona.enabled
            emailVerified = $true
            firstName     = "Acceptance"
            lastName      = [string]$persona.id
            email         = "$($persona.username)@acceptance.test"
            credentials   = @([ordered]@{ type = "password"; value = $password; temporary = $false })
            realmRoles    = $realmRoles
            attributes    = $attributes
        })
        $guideRows.Add([PSCustomObject]@{
            Username = [string]$persona.username
            Password = $password
            Enabled  = [bool]$persona.enabled
            Tenant   = if ([string]::IsNullOrWhiteSpace([string]$persona.tenantCode)) { "(missing)" } else { [string]$persona.tenantCode }
            Scope    = if ([string]::IsNullOrWhiteSpace([string]$persona.scopeOrgUnitId)) { "(all)" } else { [string]$persona.scopeOrgUnitId }
            Roles    = (@($persona.roles) -join ", ")
            Purpose  = [string]$persona.purpose
        })
    }

    if (-not $NoCredentialFiles) {
        $state = [ordered]@{
            schemaVersion            = "acceptance-v2"
            generatedAtUtc           = [DateTime]::UtcNow.ToString("O")
            gatewayCallerProofSecret = $gatewayCallerProofSecret
            passwords                = $passwords
        }
        [System.IO.File]::WriteAllText($credentialsPath, ($state | ConvertTo-Json -Depth 5), $utf8NoBom)
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
        [System.IO.File]::WriteAllLines((Join-Path $localDir "accounts.local.md"), $markdown, $utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $localDir "accounts.local.csv"), (($guideRows | ConvertTo-Csv -NoTypeInformation) -join [Environment]::NewLine), $utf8NoBom)
    }
    return , $users.ToArray()
}
