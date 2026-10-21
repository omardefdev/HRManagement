#Requires -Version 5.1
<#
.SYNOPSIS
  Syncs IAM-004 dev Keycloak artifacts into a running realm (idempotent).
.DESCRIPTION
  Keycloak --import-realm only runs on first boot. After template changes (e.g. svc-admin-logistics,
  hr-core.lookup scopes), existing dev databases keep the old realm. This script patches the live realm.
  Run after prepare-keycloak-import.ps1 when starting dev profiles.
.EXAMPLE
  .\scripts\sync-keycloak-dev-realm.ps1
#>
param(
    [string]$ContainerName = "hrm-keycloak",
    [string]$Realm = "hrmanagement",
    [string]$SvcSecret = "dev-svc-secret-local-only"
)

$ErrorActionPreference = "Stop"

function Invoke-Kcadm {
    param([Parameter(Mandatory)][string[]]$Args)
    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    try {
        # Windows PowerShell 5.1 converts native stderr into ErrorRecord objects.
        # Merging 2>&1 then appends strings such as "docker :" to otherwise valid
        # JSON. Capture both streams separately so JSON consumers receive stdout only.
        $previousErrorAction = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            & docker exec $ContainerName /opt/keycloak/bin/kcadm.sh @Args 1> $stdoutPath 2> $stderrPath
            $exitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorAction
        }
        $stdout = if ((Get-Item -LiteralPath $stdoutPath).Length -gt 0) {
            Get-Content -LiteralPath $stdoutPath -Raw
        } else { "" }
        $stderr = if ((Get-Item -LiteralPath $stderrPath).Length -gt 0) {
            Get-Content -LiteralPath $stderrPath -Raw
        } else { "" }
        if ($exitCode -ne 0) {
            throw "kcadm failed (exit $exitCode): $stderr$stdout"
        }
        return $stdout.Trim()
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function ConvertFrom-KeycloakJsonArray {
    param([AllowEmptyString()][string]$Json)
    if ([string]::IsNullOrWhiteSpace($Json)) { return }

    # Windows PowerShell 5.1 emits a JSON array as one nested Object[] while
    # PowerShell 7 enumerates it. Re-emit every item for identical callers.
    $parsed = $Json | ConvertFrom-Json
    $parsed | ForEach-Object { Write-Output $_ }
}

function Get-KeycloakAdminPassword {
    $line = docker inspect $ContainerName --format "{{range .Config.Env}}{{println .}}{{end}}" |
        Where-Object { $_ -like "KEYCLOAK_ADMIN_PASSWORD=*" } |
        Select-Object -First 1
    if (-not $line) { throw "Could not read KEYCLOAK_ADMIN_PASSWORD from $ContainerName" }
    return $line.Split("=", 2)[1]
}

function Test-ClientExists {
    param([string]$ClientId)
    $json = Invoke-Kcadm @("get", "clients", "-r", $Realm, "-q", "clientId=$ClientId", "--fields", "id")
    return $json -match '"id"\s*:'
}

function Get-ClientScopeId {
    param([string]$Name)
    $raw = Invoke-Kcadm @("get", "client-scopes", "-r", $Realm, "--fields", "id,name")
    foreach ($m in [regex]::Matches($raw, '"id"\s*:\s*"([^"]+)"\s*,\s*"name"\s*:\s*"([^"]+)"')) {
        if ($m.Groups[2].Value -eq $Name) { return $m.Groups[1].Value }
    }
    foreach ($m in [regex]::Matches($raw, '"name"\s*:\s*"([^"]+)"\s*,\s*"id"\s*:\s*"([^"]+)"')) {
        if ($m.Groups[1].Value -eq $Name) { return $m.Groups[2].Value }
    }
    return $null
}

function Ensure-ClientScope {
    param(
        [string]$Name,
        [string]$Description,
        [string]$Audience
    )
    $scopeId = Get-ClientScopeId -Name $Name
    if (-not $scopeId) {
        Write-Host "  + client scope $Name"
        $createJson = @"
{
  "name": "$Name",
  "description": "$Description",
  "protocol": "openid-connect",
  "attributes": {
    "include.in.token.scope": "true",
    "display.on.consent.screen": "false"
  }
}
"@
        $createJson | docker exec -i $ContainerName sh -c "cat > /tmp/kc-scope.json"
        try {
            $scopeId = Invoke-Kcadm @("create", "client-scopes", "-r", $Realm, "-f", "/tmp/kc-scope.json", "-i")
            $scopeId = $scopeId.Trim()
        }
        catch {
            $scopeId = Get-ClientScopeId -Name $Name
            if (-not $scopeId) { throw }
            Write-Host "    (scope already existed)"
        }
    }
    else {
        Write-Host "  = client scope $Name (exists)"
    }

    $mappers = Invoke-Kcadm @("get", "client-scopes/$scopeId/protocol-mappers/models", "-r", $Realm, "--fields", "name")
    if ($mappers -notmatch "audience-$Audience") {
        Write-Host "    + audience mapper -> $Audience"
        $mapperJson = @"
{
  "name": "audience-$Audience",
  "protocol": "openid-connect",
  "protocolMapper": "oidc-audience-mapper",
  "consentRequired": false,
  "config": {
    "included.client.audience": "$Audience",
    "id.token.claim": "false",
    "access.token.claim": "true",
    "introspection.token.claim": "true"
  }
}
"@
        $mapperJson | docker exec -i $ContainerName sh -c "cat > /tmp/kc-mapper.json"
        Invoke-Kcadm @("create", "client-scopes/$scopeId/protocol-mappers/models", "-r", $Realm, "-f", "/tmp/kc-mapper.json") | Out-Null
    }

    return $scopeId
}

function Get-ClientInternalId {
    param([string]$ClientId)
    $json = Invoke-Kcadm @("get", "clients", "-r", $Realm, "-q", "clientId=$ClientId", "--fields", "id")
    if ($json -match '"id"\s*:\s*"([^"]+)"') { return $Matches[1] }
    throw "Client not found: $ClientId"
}

function Ensure-ServiceClient {
    param(
        [string]$ClientId,
        [string[]]$OptionalScopes
    )
    $clientUuid = $null
    if (-not (Test-ClientExists -ClientId $ClientId)) {
        Write-Host "  + client $ClientId"
        $clientJson = @"
{
  "clientId": "$ClientId",
  "enabled": true,
  "publicClient": false,
  "secret": "$SvcSecret",
  "standardFlowEnabled": false,
  "serviceAccountsEnabled": true,
  "protocol": "openid-connect",
  "fullScopeAllowed": false
}
"@
        $clientJson | docker exec -i $ContainerName sh -c "cat > /tmp/kc-client.json"
        Invoke-Kcadm @("create", "clients", "-r", $Realm, "-f", "/tmp/kc-client.json") | Out-Null
        $clientUuid = Get-ClientInternalId -ClientId $ClientId
    }
    else {
        Write-Host "  = client $ClientId (exists)"
        $clientUuid = Get-ClientInternalId -ClientId $ClientId
        try {
            Invoke-Kcadm @(
                "update", "clients/$clientUuid", "-r", $Realm,
                "-s", "secret=$SvcSecret",
                "-s", "enabled=true",
                "-s", "publicClient=false",
                "-s", "standardFlowEnabled=false",
                "-s", "serviceAccountsEnabled=true",
                "-s", "fullScopeAllowed=false") | Out-Null
        }
        catch {
            Write-Host "    (client update skipped: $($_.Exception.Message))"
        }
    }

    if (-not $clientUuid) {
        $clientUuid = Get-ClientInternalId -ClientId $ClientId
    }

    foreach ($scopeName in $OptionalScopes) {
        $scopeId = Get-ClientScopeId -Name $scopeName
        if (-not $scopeId) { throw "Missing client scope $scopeName" }
        try {
            Write-Host "    + optional scope $scopeName"
            Invoke-Kcadm @("update", "clients/$clientUuid/optional-client-scopes/$scopeId", "-r", $Realm) | Out-Null
        }
        catch {
            Write-Host "    = optional scope $scopeName (already linked or skipped)"
        }
    }
}

function Ensure-ClientScopeLinked {
    param(
        [string]$ClientUuid,
        [string]$ScopeName,
        [ValidateSet("default", "optional")]
        [string]$LinkKind = "default"
    )
    $scopeId = Get-ClientScopeId -Name $ScopeName
    if (-not $scopeId) { throw "Missing client scope $ScopeName" }
    $path = if ($LinkKind -eq "optional") {
        "clients/$ClientUuid/optional-client-scopes/$scopeId"
    }
    else {
        "clients/$ClientUuid/default-client-scopes/$scopeId"
    }
    try {
        Write-Host "    + $LinkKind scope $ScopeName on client"
        Invoke-Kcadm @("update", $path, "-r", $Realm) | Out-Null
    }
    catch {
        Write-Host "    = $LinkKind scope $ScopeName (already linked or skipped)"
    }
}

function Ensure-StandardOidcScope {
    param(
        [string]$Name,
        [string]$Description,
        [hashtable]$Attributes = @{ "include.in.token.scope" = "false"; "display.on.consent.screen" = "false" },
        [string]$MapperJson = ""
    )
    $scopeId = Get-ClientScopeId -Name $Name
    if (-not $scopeId) {
        Write-Host "  + standard scope $Name"
        $attrLines = ($Attributes.GetEnumerator() | ForEach-Object { "    `"$($_.Key)`": `"$($_.Value)`"" }) -join ",`n"
        $createJson = @"
{
  "name": "$Name",
  "description": "$Description",
  "protocol": "openid-connect",
  "attributes": {
$attrLines
  }
}
"@
        $createJson | docker exec -i $ContainerName sh -c "cat > /tmp/kc-std-scope.json"
        try {
            $scopeId = Invoke-Kcadm @("create", "client-scopes", "-r", $Realm, "-f", "/tmp/kc-std-scope.json", "-i")
            $scopeId = $scopeId.Trim()
        }
        catch {
            $scopeId = Get-ClientScopeId -Name $Name
            if (-not $scopeId) { throw }
            Write-Host "    (scope already existed)"
        }
    }
    else {
        Write-Host "  = standard scope $Name (exists)"
    }

    if ($MapperJson) {
        $mappers = Invoke-Kcadm @("get", "client-scopes/$scopeId/protocol-mappers/models", "-r", $Realm, "--fields", "name")
        if ($mappers -notmatch '"name"\s*:\s*"realm roles"') {
            Write-Host "    + protocol mapper for $Name"
            $MapperJson | docker exec -i $ContainerName sh -c "cat > /tmp/kc-std-mapper.json"
            Invoke-Kcadm @("create", "client-scopes/$scopeId/protocol-mappers/models", "-r", $Realm, "-f", "/tmp/kc-std-mapper.json") | Out-Null
        }
    }

    return $scopeId
}

function Ensure-GatewayOidcScopes {
    Write-Host "Standard OIDC scopes:"
    Ensure-StandardOidcScope -Name "openid" -Description "OpenID Connect scope for openid" | Out-Null
    Ensure-StandardOidcScope -Name "profile" -Description "OpenID Connect built-in scope: profile" -Attributes @{
        "include.in.token.scope" = "true"
        "display.on.consent.screen" = "true"
        "consent.screen.text" = '${profileScopeConsentText}'
    } | Out-Null
    Ensure-StandardOidcScope -Name "email" -Description "OpenID Connect built-in scope: email" -Attributes @{
        "include.in.token.scope" = "true"
        "display.on.consent.screen" = "true"
        "consent.screen.text" = '${emailScopeConsentText}'
    } | Out-Null
    Ensure-StandardOidcScope -Name "offline_access" -Description "OpenID Connect built-in scope: offline_access" -Attributes @{
        "display.on.consent.screen" = "true"
        "consent.screen.text" = '${offlineAccessScopeConsentText}'
    } | Out-Null
    $rolesMapper = @'
{
  "name": "realm roles",
  "protocol": "openid-connect",
  "protocolMapper": "oidc-usermodel-realm-role-mapper",
  "consentRequired": false,
  "config": {
    "multivalued": "true",
    "user.attribute": "foo",
    "access.token.claim": "true",
    "claim.name": "realm_access.roles",
    "jsonType.label": "String"
  }
}
'@
    Ensure-StandardOidcScope -Name "roles" -Description "OpenID Connect scope for add user roles to the access token" -Attributes @{
        "include.in.token.scope" = "false"
        "display.on.consent.screen" = "true"
        "consent.screen.text" = '${rolesScopeConsentText}'
    } -MapperJson $rolesMapper | Out-Null

    $gatewayUuid = Get-ClientInternalId -ClientId "hr-gateway-client"
    foreach ($scope in @("openid", "profile", "email", "roles")) {
        Ensure-ClientScopeLinked -ClientUuid $gatewayUuid -ScopeName $scope -LinkKind default
    }
    Ensure-ClientScopeLinked -ClientUuid $gatewayUuid -ScopeName "offline_access" -LinkKind optional
}

Write-Host "==> Syncing Keycloak dev realm '$Realm' in $ContainerName..."
$adminPassword = Get-KeycloakAdminPassword
$authenticated = $false
for ($attempt = 1; $attempt -le 60; $attempt++) {
    try {
        Invoke-Kcadm @("config", "credentials", "--server", "http://localhost:8080", "--realm", "master", "--user", "admin", "--password", $adminPassword) | Out-Null
        $authenticated = $true
        break
    }
    catch {
        if ($attempt -eq 60) {
            throw "Keycloak administration endpoint was not ready after 120 seconds. $($_.Exception.Message)"
        }
        Start-Sleep -Seconds 2
    }
}
if (-not $authenticated) { throw "Keycloak administration authentication failed." }

function Write-KeycloakJson {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][string]$ContainerPath
    )
    $json = $Value | ConvertTo-Json -Depth 30
    $json | docker exec -i $ContainerName sh -c "cat > $ContainerPath"
    if ($LASTEXITCODE -ne 0) { throw "Failed to write $ContainerPath in $ContainerName." }
}

function Ensure-GatewayClientUrls {
    $gatewayUuid = Get-ClientInternalId -ClientId "hr-gateway-client"
    $postLogoutUris = "http://localhost:5000/signout-callback-oidc##http://localhost:5200/signout-callback-oidc##http://localhost:6200/signout-callback-oidc"

    # A small update document avoids parsing kcadm's pretty-printed client JSON.
    # Windows PowerShell 5.1 can wrap a long JSON string at the console width and
    # split a URL in the middle, which makes ConvertFrom-Json fail.
    Write-Host "  = enforcing gateway browser redirect URLs, including Acceptance port 6200"
    $clientPatch = [pscustomobject][ordered]@{
        redirectUris = @(
            "http://localhost:5000/signin-oidc",
            "http://localhost:5200/signin-oidc",
            "http://localhost:6200/signin-oidc"
        )
        attributes = [pscustomobject][ordered]@{
            "post.logout.redirect.uris" = $postLogoutUris
        }
    }
    Write-KeycloakJson -Value $clientPatch -ContainerPath "/tmp/kc-gateway-client-urls.json"
    Invoke-Kcadm @("update", "clients/$gatewayUuid", "-r", $Realm, "-f", "/tmp/kc-gateway-client-urls.json") | Out-Null
}

function Ensure-AcceptanceUserProfile {
    $profile = (Invoke-Kcadm @("get", "users/profile", "-r", $Realm)) | ConvertFrom-Json
    $changed = $false
    $definitions = @(
        [pscustomobject][ordered]@{
            name = "tenant_id"
            displayName = "Tenant ID"
            validations = [ordered]@{ length = [ordered]@{ max = 36 } }
            permissions = [ordered]@{ view = @("admin", "user"); edit = @("admin") }
            multivalued = $false
        },
        [pscustomobject][ordered]@{
            name = "scope_org_unit_id"
            displayName = "Organization scope ID"
            validations = [ordered]@{ length = [ordered]@{ max = 36 } }
            permissions = [ordered]@{ view = @("admin", "user"); edit = @("admin") }
            multivalued = $false
        },
        [pscustomobject][ordered]@{
            name = "acceptance_fixture"
            displayName = "Acceptance fixture"
            validations = [ordered]@{ length = [ordered]@{ max = 64 } }
            permissions = [ordered]@{ view = @("admin"); edit = @("admin") }
            multivalued = $false
        }
    )

    foreach ($definition in $definitions) {
        if (-not ($profile.attributes | Where-Object { $_.name -eq $definition.name })) {
            $profile.attributes += $definition
            $changed = $true
        }
    }
    if (-not $changed) { return }

    Write-Host "  + managed tenant and organization-scope user attributes"
    Write-KeycloakJson -Value $profile -ContainerPath "/tmp/kc-user-profile.json"
    Invoke-Kcadm @("update", "users/profile", "-r", $Realm, "-f", "/tmp/kc-user-profile.json") | Out-Null
}

function Sync-AcceptanceUsers {
    param([string[]]$UsernamePrefixes = @("acc-", "e2e-"))

    $root = Split-Path -Parent $PSScriptRoot
    $realmPath = Join-Path $root "docker/keycloak/import/realm-hrmanagement.json"
    if (-not (Test-Path -LiteralPath $realmPath)) {
        Write-Warning "Generated realm source not found at $realmPath; user synchronization skipped."
        return
    }

    Ensure-AcceptanceUserProfile
    $sourceRealm = Get-Content -LiteralPath $realmPath -Raw | ConvertFrom-Json
    $sourceUsers = @($sourceRealm.users | Where-Object {
        $candidate = [string]$_.username
        $UsernamePrefixes | Where-Object { $candidate.StartsWith($_) }
    })
    if ($sourceUsers.Count -eq 0) { return }

    $managedRoleNames = @($sourceRealm.roles.realm | ForEach-Object {
        if ($_ -is [string]) { $_ } else { [string]$_.name }
    })
    $liveUsers = @(ConvertFrom-KeycloakJsonArray (Invoke-Kcadm @("get", "users", "-r", $Realm, "--fields", "id,username")))
    $liveByUsername = @{}
    foreach ($user in $liveUsers) { $liveByUsername[[string]$user.username] = $user }

    foreach ($sourceUser in $sourceUsers) {
        $username = [string]$sourceUser.username
        $liveUser = $liveByUsername[$username]
        $isNew = $null -eq $liveUser
        $payload = [pscustomobject][ordered]@{
            username = $username
            enabled = [bool]$sourceUser.enabled
            emailVerified = [bool]$sourceUser.emailVerified
            firstName = [string]$sourceUser.firstName
            lastName = [string]$sourceUser.lastName
            email = [string]$sourceUser.email
            attributes = if ($null -eq $sourceUser.attributes) { [pscustomobject]@{} } else { $sourceUser.attributes }
        }
        if ($isNew -and -not [string]::IsNullOrWhiteSpace([string]$sourceUser.id)) {
            $payload | Add-Member -NotePropertyName id -NotePropertyValue ([string]$sourceUser.id)
        }

        Write-KeycloakJson -Value $payload -ContainerPath "/tmp/kc-acceptance-user.json"
        if ($isNew) {
            Write-Host "  + user $username"
            Invoke-Kcadm @("create", "users", "-r", $Realm, "-f", "/tmp/kc-acceptance-user.json") | Out-Null
            $lookup = @(ConvertFrom-KeycloakJsonArray (Invoke-Kcadm @("get", "users", "-r", $Realm, "-q", "username=$username", "--fields", "id,username")))
            if ($lookup.Count -ne 1) { throw "Unable to resolve newly created user '$username'." }
            $liveUser = $lookup[0]
            $credential = @($sourceUser.credentials | Where-Object { $_.type -eq "password" } | Select-Object -First 1)
            if ($credential.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$credential[0].value)) {
                throw "Generated realm has no local password for newly created user '$username'."
            }
            Invoke-Kcadm @(
                "set-password", "-r", $Realm,
                "--username", $username,
                "--new-password", [string]$credential[0].value,
                "--temporary=false"
            ) | Out-Null
        } else {
            Invoke-Kcadm @("update", "users/$($liveUser.id)", "-r", $Realm, "-f", "/tmp/kc-acceptance-user.json") | Out-Null
        }

        $currentRoles = @(ConvertFrom-KeycloakJsonArray (Invoke-Kcadm @("get-roles", "-r", $Realm, "--uusername", $username)))
        $currentRoleNames = @($currentRoles | ForEach-Object { [string]$_.name })
        $expectedRoleNames = @($sourceUser.realmRoles | ForEach-Object { [string]$_ })
        foreach ($role in $expectedRoleNames) {
            if ($currentRoleNames -notcontains $role) {
                Invoke-Kcadm @("add-roles", "-r", $Realm, "--uusername", $username, "--rolename", $role) | Out-Null
            }
        }
        foreach ($role in $currentRoleNames) {
            if ($managedRoleNames -contains $role -and $expectedRoleNames -notcontains $role) {
                Invoke-Kcadm @("remove-roles", "-r", $Realm, "--uusername", $username, "--rolename", $role) | Out-Null
            }
        }
    }
}

Ensure-GatewayClientUrls
Ensure-GatewayOidcScopes

Write-Host "Acceptance / E2E identities:"
Sync-AcceptanceUsers

Write-Host "Client scopes:"
Ensure-ClientScope -Name "hr-core.lookup" -Description "HR.Core employee lookup (S2S)" -Audience "api-hr-core" | Out-Null
Ensure-ClientScope -Name "hr-core.workflow" -Description "HR.Core workflow + code generation (S2S)" -Audience "api-hr-core" | Out-Null
Ensure-ClientScope -Name "file-storage.references" -Description "File.Storage reference registration (S2S)" -Audience "api-file-storage" | Out-Null
Ensure-ClientScope -Name "dsar.subject.read" -Description "Compliance DSAR subject collection (S2S)" -Audience "api-hr-core" | Out-Null
Ensure-ClientScope -Name "dsar.subject.read" -Description "Compliance DSAR subject collection (S2S)" -Audience "api-payroll" | Out-Null
Ensure-ClientScope -Name "dsar.subject.read" -Description "Compliance DSAR subject collection (S2S)" -Audience "api-file-storage" | Out-Null
Ensure-ClientScope -Name "directory.sync" -Description "Auth.Directory synchronization (S2S)" -Audience "api-auth-directory" | Out-Null
Ensure-ClientScope -Name "evidence.ingest" -Description "Gateway evidence ingestion (S2S)" -Audience "hr-gateway-client" | Out-Null

Write-Host "Service clients:"
Ensure-ServiceClient -ClientId "svc-hr-core" -OptionalScopes @("file-storage.references")
Ensure-ServiceClient -ClientId "svc-hr-contracts" -OptionalScopes @("file-storage.references")
Ensure-ServiceClient -ClientId "svc-hr-health" -OptionalScopes @()
Ensure-ServiceClient -ClientId "svc-hr-movements" -OptionalScopes @("hr-core.lookup")
Ensure-ServiceClient -ClientId "svc-hr-training" -OptionalScopes @("file-storage.references")
Ensure-ServiceClient -ClientId "svc-payroll" -OptionalScopes @("hr-core.lookup")
Ensure-ServiceClient -ClientId "svc-idcard" -OptionalScopes @("file-storage.references")
Ensure-ServiceClient -ClientId "svc-records-archive" -OptionalScopes @("file-storage.references")
Ensure-ServiceClient -ClientId "svc-admin-logistics" -OptionalScopes @(
    "hr-core.lookup",
    "hr-core.workflow",
    "file-storage.references"
)
Ensure-ServiceClient -ClientId "svc-it-infra" -OptionalScopes @("directory.sync")
Ensure-ServiceClient -ClientId "svc-dev-analytics" -OptionalScopes @()
Ensure-ServiceClient -ClientId "svc-compliance" -OptionalScopes @("hr-core.lookup", "dsar.subject.read")
Ensure-ServiceClient -ClientId "svc-auth-directory" -OptionalScopes @()
Ensure-ServiceClient -ClientId "svc-file-storage" -OptionalScopes @()

Write-Host "Done. Verify token:"
Write-Host "  curl -X POST http://localhost:8088/realms/$Realm/protocol/openid-connect/token \"
Write-Host "    -d grant_type=client_credentials -d client_id=svc-admin-logistics -d client_secret=$SvcSecret \"
Write-Host "    -d scope=`"hr-core.workflow`""
