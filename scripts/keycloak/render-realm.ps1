#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-39 (D-39-1, P-1): renders the Keycloak realm "hrmanagement" for one profile from its two sources:
    docker/keycloak/realm-manifest.json      roles, gateway client, directory-sync, built-in scopes, user profile,
                                             security settings, users per profile (no secret)
    deploy/wiring/service-wiring.json        the svc-* clients, their scopes, the scope audiences, the secret variables

.DESCRIPTION
  Production  one bootstrap administrator (HRM_BOOTSTRAP_ADMIN_USER) with UPDATE_PASSWORD at first login
  Demo        the demo users of the manifest
  Acceptance  every persona of testdata/acceptance/personas.json, gateway password grant on (acceptance harness)

  Production and Demo: every secret is a ${NAME} placeholder (client secrets, DEMO_USERS_PASSWORD, the bootstrap
  administrator's temporary password). Keycloak resolves them from its own environment at import, on the server, and
  deploy/keycloak/reconcile.sh sets the client secrets from the same environment on every deploy. This script reads no
  secret: from -EnvFile it takes APP_DOMAIN and HRM_BOOTSTRAP_ADMIN_USER only (host and user names).
  Acceptance keeps its lab secrets (P-1): the gateway secret of the local .env, the shared lab service secret, and the
  persona passwords generated into the git-ignored .acceptance/credentials.json.

  Output: artifacts/keycloak/<profile>/realm-hrmanagement.json (git-ignored), or -OutFile.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/keycloak/render-realm.ps1 -Profile Demo -AppDomain hr.example.org
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/keycloak/render-realm.ps1 -Profile Acceptance -EnvFile .env
#>
param(
    [Parameter(Mandatory)][ValidateSet("Production", "Demo", "Acceptance")][string]$Profile,
    [string]$EnvFile = "",
    [string]$AppDomain = "",
    [string]$OutFile = "",
    [string]$RepoRoot = "",
    [switch]$RotateAcceptanceCredentials,
    # Tests: skip the git-ignored credential files of the Acceptance profile (passwords become fixed lab values).
    [switch]$NoAcceptanceCredentialFiles
)

$ErrorActionPreference = "Stop"
# $PSScriptRoot is empty in the param block of an advanced script on Windows PowerShell 5.1.
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path }
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$labServiceSecret = "dev-svc-secret-local-only"

function Read-Json([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
}

# Values of the named keys only; every other line of the file is skipped unread.
function Read-EnvKeys([string]$Path, [string[]]$Keys) {
    $result = @{}
    if ([string]::IsNullOrWhiteSpace($Path)) { return $result }
    if (-not (Test-Path $Path)) { throw "Env file not found: $Path" }
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $i = $line.IndexOf('=')
        if ($i -lt 1) { continue }
        $key = $line.Substring(0, $i).Trim()
        if ($Keys -notcontains $key) { continue }
        $value = $line.Substring($i + 1).Trim()
        if ($value.Length -ge 2 -and $value.StartsWith('"') -and $value.EndsWith('"')) { $value = $value.Substring(1, $value.Length - 2) }
        $result[$key] = $value
    }
    return $result
}

function Copy-Plain($Value) {
    # Deep copy of a ConvertFrom-Json value without the manifest's "about" notes.
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Array]) {
        $list = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Value) { $list.Add((Copy-Plain $item)) }
        return , $list.ToArray()
    }
    if ($Value -is [pscustomobject]) {
        $copy = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.Name -eq "about") { continue }
            $copy[$property.Name] = Copy-Plain $property.Value
        }
        return $copy
    }
    return $Value
}

function Placeholder([string]$Name) { return '${' + $Name + '}' }

$manifest = Read-Json (Join-Path $RepoRoot "docker/keycloak/realm-manifest.json")
$wiring = Read-Json (Join-Path $RepoRoot "deploy/wiring/service-wiring.json")
if ($manifest.schemaVersion -ne "realm-manifest-v1") { throw "Unexpected manifest schemaVersion '$($manifest.schemaVersion)'." }
$profileSpec = $manifest.profiles.$Profile
if ($null -eq $profileSpec) { throw "Profile $Profile is not in the manifest." }

$envValues = Read-EnvKeys $EnvFile @("APP_DOMAIN", "HRM_BOOTSTRAP_ADMIN_USER", "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET")
$labSecrets = ($Profile -eq "Acceptance")
if (-not $labSecrets) { $envValues.Remove("KEYCLOAK_HR_GATEWAY_CLIENT_SECRET") }

# ---- application URLs (redirect URIs) ----------------------------------------------------------------------------
$appUrls = @()
if ($profileSpec.appUrls -is [System.Array]) {
    $appUrls = @($profileSpec.appUrls | ForEach-Object { [string]$_ })
}
else {
    if ([string]::IsNullOrWhiteSpace($AppDomain)) { $AppDomain = [string]$envValues["APP_DOMAIN"] }
    if ([string]::IsNullOrWhiteSpace($AppDomain) -or $AppDomain -notmatch '^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$') {
        throw "Profile $Profile needs the public application host name: -AppDomain or APP_DOMAIN in -EnvFile (for example hr.161-97-185-71.sslip.io)."
    }
    $appUrls = @(([string]$profileSpec.appUrls).Replace("{APP_DOMAIN}", $AppDomain))
}
# @() around the if: a one-element array would otherwise unroll to a string.
$webOrigins = @(if ($null -ne $profileSpec.webOrigins) { $profileSpec.webOrigins | ForEach-Object { [string]$_ } } else { $appUrls })

# ---- realm --------------------------------------------------------------------------------------------------------
$realm = Copy-Plain $manifest.realm
foreach ($field in $manifest.security.PSObject.Properties) {
    if ($field.Name -eq "about") { continue }
    $realm[$field.Name] = $field.Value
}
$realm["roles"] = [ordered]@{ realm = @($manifest.roles | ForEach-Object { [ordered]@{ name = [string]$_.name; description = [string]$_.description } }) }
$realm["defaultDefaultClientScopes"] = @($manifest.defaultDefaultClientScopes | ForEach-Object { [string]$_ })
$realm["defaultOptionalClientScopes"] = @($manifest.defaultOptionalClientScopes | ForEach-Object { [string]$_ })

# Client scopes: Keycloak creates no built-in scope when a realm file lists clientScopes, so the manifest carries the
# built-ins the clients use; every service scope comes from the matrix with one audience mapper per audience.
$scopes = New-Object System.Collections.Generic.List[object]
foreach ($scope in @($manifest.builtinClientScopes)) { $scopes.Add((Copy-Plain $scope)) }
foreach ($scopeProperty in @($wiring.scopes.PSObject.Properties | Sort-Object Name)) {
    $mappers = @()
    foreach ($audience in @($scopeProperty.Value.audiences)) {
        $mappers += [ordered]@{
            name            = "audience-$audience"
            protocol        = "openid-connect"
            protocolMapper  = "oidc-audience-mapper"
            consentRequired = $false
            config          = [ordered]@{
                "included.client.audience"  = [string]$audience
                "id.token.claim"            = "false"
                "access.token.claim"        = "true"
                "introspection.token.claim" = "true"
            }
        }
    }
    $scopes.Add([ordered]@{
        name            = $scopeProperty.Name
        description     = [string]$scopeProperty.Value.description
        protocol        = "openid-connect"
        attributes      = [ordered]@{ "include.in.token.scope" = "true"; "display.on.consent.screen" = "false" }
        protocolMappers = $mappers
    })
}
$realm["clientScopes"] = $scopes.ToArray()

# PLAN-42 (P-6): the persona step writes .acceptance/credentials.json, which holds the generated service secret. It runs
# first, because the clients below need that secret. A test render writes no file and keeps the fixed lab value.
$acceptancePersonaUsers = @()
if ($Profile -eq "Acceptance") {
    . (Join-Path $PSScriptRoot "acceptance-personas.ps1")
    $acceptancePersonaUsers = Get-AcceptancePersonaUsers -RepoRoot $RepoRoot -Rotate:$RotateAcceptanceCredentials -NoCredentialFiles:$NoAcceptanceCredentialFiles
    if (-not $NoAcceptanceCredentialFiles) {
        $acceptanceCredentials = Read-Json (Join-Path $RepoRoot ".acceptance/credentials.json")
        $labServiceSecret = [string]$acceptanceCredentials.serviceClientSecret
    }
}

# ---- clients ------------------------------------------------------------------------------------------------------
$clients = New-Object System.Collections.Generic.List[object]

$gateway = Copy-Plain $manifest.gatewayClient
$gatewaySecretEnv = [string]$gateway["secretEnv"]
$gateway.Remove("secretEnv")
if ($labSecrets) {
    $labGatewaySecret = [string]$envValues["KEYCLOAK_HR_GATEWAY_CLIENT_SECRET"]
    if ([string]::IsNullOrWhiteSpace($labGatewaySecret)) { throw "Acceptance needs KEYCLOAK_HR_GATEWAY_CLIENT_SECRET in -EnvFile (the lab .env)." }
    $gateway["secret"] = $labGatewaySecret
}
else {
    $gateway["secret"] = Placeholder $gatewaySecretEnv
}
$gateway["redirectUris"] = @($appUrls | ForEach-Object { "$_/signin-oidc" })
$gateway["webOrigins"] = $webOrigins
$gateway["attributes"] = [ordered]@{ "post.logout.redirect.uris" = (@($appUrls | ForEach-Object { "$_/signout-callback-oidc" }) -join "##") }
if ($null -ne $profileSpec.acceptanceExceptions -and $profileSpec.acceptanceExceptions.gatewayDirectAccessGrantsEnabled) {
    $gateway["directAccessGrantsEnabled"] = $true
}
$clients.Add($gateway)

$directorySync = Copy-Plain $manifest.directorySyncClient
$directorySyncEnv = [string]$directorySync["secretEnv"]
$directorySync.Remove("secretEnv")
$directorySync["secret"] = if ($labSecrets) { $labServiceSecret } else { Placeholder $directorySyncEnv }
$clients.Add($directorySync)

foreach ($service in @($wiring.services | Where-Object { $_.serviceIdentity } | Sort-Object clientId)) {
    $client = Copy-Plain $manifest.serviceClientDefaults
    $ordered = [ordered]@{ clientId = [string]$service.clientId }
    foreach ($key in $client.Keys) { $ordered[$key] = $client[$key] }
    $ordered["secret"] = if ($labSecrets) { $labServiceSecret } else { Placeholder ([string]$service.secretEnv) }
    $ordered["optionalClientScopes"] = @($service.scopes | ForEach-Object { [string]$_ })
    $clients.Add($ordered)
}
$realm["clients"] = $clients.ToArray()

# ---- user profile -------------------------------------------------------------------------------------------------
$profileConfig = [ordered]@{
    attributes = @(@($manifest.userProfile.builtinAttributes) + @($manifest.userProfile.attributes) | ForEach-Object { Copy-Plain $_ })
    groups     = @($manifest.userProfile.groups | ForEach-Object { Copy-Plain $_ })
}
$realm["components"] = [ordered]@{
    "org.keycloak.userprofile.UserProfileProvider" = @([ordered]@{
        providerId    = "declarative-user-profile"
        subComponents = [ordered]@{}
        config        = [ordered]@{ "kc.user.profile.config" = @(($profileConfig | ConvertTo-Json -Depth 20 -Compress)) }
    })
}

# ---- users --------------------------------------------------------------------------------------------------------
$users = New-Object System.Collections.Generic.List[object]
switch ($Profile) {
    "Production" {
        $admin = $manifest.bootstrapAdmin
        $username = [string]$envValues[[string]$admin.usernameEnv]
        if ([string]::IsNullOrWhiteSpace($username)) { $username = Placeholder ([string]$admin.usernameEnv) }
        $users.Add([ordered]@{
            username        = $username
            enabled         = $true
            emailVerified   = $false
            firstName       = [string]$admin.firstName
            lastName        = [string]$admin.lastName
            requiredActions = @($admin.requiredActions | ForEach-Object { [string]$_ })
            credentials     = @([ordered]@{ type = "password"; value = (Placeholder ([string]$admin.passwordEnv)); temporary = $true })
            realmRoles      = @($admin.realmRoles | ForEach-Object { [string]$_ })
            attributes      = Copy-Plain $admin.attributes
        })
    }
    "Demo" {
        foreach ($demoUser in @($manifest.demoUsers)) {
            $user = Copy-Plain $demoUser
            $ordered = [ordered]@{ username = $user["username"]; enabled = $true; emailVerified = $true }
            foreach ($key in $user.Keys) { if ($key -ne "username") { $ordered[$key] = $user[$key] } }
            $ordered["credentials"] = @([ordered]@{ type = "password"; value = (Placeholder "DEMO_USERS_PASSWORD"); temporary = $false })
            $users.Add($ordered)
        }
    }
    "Acceptance" {
        foreach ($user in $acceptancePersonaUsers) { $users.Add($user) }
    }
}
$realm["users"] = $users.ToArray()

# ---- checks and output --------------------------------------------------------------------------------------------
$json = $realm | ConvertTo-Json -Depth 100
$null = $json | ConvertFrom-Json
foreach ($leftover in @("__KEYCLOAK_HR_GATEWAY_CLIENT_SECRET__", "__DEMO_HRADMIN_PASSWORD__", "__SVC_SECRET__")) {
    if ($json.Contains($leftover)) { throw "Rendered realm still contains the template marker '$leftover'." }
}
if (-not $labSecrets) {
    foreach ($forbidden in @($labServiceSecret, "localhost")) {
        if ($json.Contains($forbidden)) { throw "Profile $Profile must not contain '$forbidden'." }
    }
}

if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path $RepoRoot ("artifacts/keycloak/{0}/realm-hrmanagement.json" -f $Profile.ToLowerInvariant())
}
$outDir = Split-Path $OutFile -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
[System.IO.File]::WriteAllText($OutFile, $json, $utf8NoBom)

# ---- reconcile plan (P-2) -----------------------------------------------------------------------------------------
# deploy/keycloak/reconcile.sh runs in the Keycloak image (no JSON tool there), so the same realm is also written as a
# flat, tab-separated plan next to the realm file: names, flags, URLs and the NAMES of the secret variables, never a
# secret value. Keycloak imports only *.json from its import directory and ignores this file. "-" marks an empty list.
function Join-List($Items) { $list = @($Items | ForEach-Object { [string]$_ } | Where-Object { $_ -ne "" }); if ($list.Count -eq 0) { return "-" } return ($list -join " ") }
$tab = "`t"
$plan = New-Object System.Collections.Generic.List[string]
$plan.Add("# PLAN-39 reconcile plan, profile $Profile, rendered by scripts/keycloak/render-realm.ps1 (no secret values)")
$plan.Add("realm${tab}" + [string]$realm["realm"])
# Read by remote-deploy.sh: the profile must match HRM_DEPLOYMENT_MODE on the server (review F3). Reconcile ignores it.
$plan.Add("profile${tab}$Profile")
foreach ($field in $manifest.security.PSObject.Properties) {
    if ($field.Name -eq "about") { continue }
    $value = if ($field.Value -is [bool]) { ([string]$field.Value).ToLowerInvariant() } else { [string]$field.Value }
    $plan.Add("security${tab}$($field.Name)${tab}$value")
}
# PLAN-43: the realm roles; reconcile creates the ones an existing realm lacks.
foreach ($role in @($manifest.roles)) { $plan.Add("role${tab}$([string]$role.name)${tab}$([string]$role.description)") }
foreach ($scopeProperty in @($wiring.scopes.PSObject.Properties | Sort-Object Name)) {
    $plan.Add("scope${tab}$($scopeProperty.Name)${tab}$(Join-List $scopeProperty.Value.audiences)${tab}$([string]$scopeProperty.Value.description)")
}
$flagNames = @("enabled", "publicClient", "standardFlowEnabled", "implicitFlowEnabled", "directAccessGrantsEnabled", "serviceAccountsEnabled", "fullScopeAllowed")
foreach ($client in $clients) {
    $id = [string]$client["clientId"]
    $kind = switch ($id) { "hr-gateway-client" { "gateway" } "directory-sync" { "directory-sync" } default { "service" } }
    $secretEnv = switch ($kind) {
        "gateway" { $gatewaySecretEnv }
        "directory-sync" { $directorySyncEnv }
        default { [string](@($wiring.services | Where-Object { $_.clientId -eq $id })[0].secretEnv) }
    }
    $optional = if ($client.Contains("optionalClientScopes")) { $client["optionalClientScopes"] } else { @() }
    $default = if ($client.Contains("defaultClientScopes")) { $client["defaultClientScopes"] } else { @() }
    $plan.Add("client${tab}$id${tab}$kind${tab}$secretEnv${tab}$(Join-List $optional)${tab}$(Join-List $default)")
    $flags = @($flagNames | Where-Object { $client.Contains($_) } | ForEach-Object { "$_=" + ([string]$client[$_]).ToLowerInvariant() })
    $plan.Add("flags${tab}$id${tab}$(Join-List $flags)")
    if ($kind -eq "gateway") {
        $plan.Add("urls${tab}$id${tab}$(Join-List $client['redirectUris'])${tab}$(Join-List $client['webOrigins'])${tab}$([string]$client['attributes']['post.logout.redirect.uris'])")
    }
}
$planText = ($plan -join "`n") + "`n"
foreach ($forbidden in @($labServiceSecret) + @($envValues.Values | Where-Object { $_ -and $_ -ne $AppDomain -and $_ -ne $envValues["HRM_BOOTSTRAP_ADMIN_USER"] })) {
    if ($planText.Contains([string]$forbidden)) { throw "The reconcile plan would contain a secret value." }
}
[System.IO.File]::WriteAllText((Join-Path $outDir "reconcile-plan.tsv"), $planText, $utf8NoBom)

$secretNote = if ($labSecrets) { "lab secrets" } else { 'secrets are ${NAME} placeholders resolved by Keycloak' }
Write-Host ("Rendered profile {0}: {1} clients, {2} client scopes, {3} users -> {4} ({5}); reconcile plan reconcile-plan.tsv" -f $Profile, $clients.Count, $scopes.Count, $users.Count, $OutFile, $secretNote)
