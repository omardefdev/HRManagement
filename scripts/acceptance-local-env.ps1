#Requires -Version 5.1

function Get-HrmDotEnvValue {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Name
    )

    $path = Join-Path $RepoRoot ".env"
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $line = Get-Content -LiteralPath $path |
        Where-Object { $_ -match "^\s*$([regex]::Escape($Name))=" } |
        Select-Object -First 1
    if (-not $line) { return $null }
    return $line.Split("=", 2)[1].Trim().Trim('"').Trim("'")
}

function Import-AcceptanceLocalEnvironment {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $credentialsPath = Join-Path $RepoRoot ".acceptance/credentials.json"
    if (-not (Test-Path -LiteralPath $credentialsPath)) {
        throw "Acceptance credentials are missing. Run scripts/prepare-keycloak-import.ps1 -Profile Acceptance first."
    }

    $credentials = Get-Content -LiteralPath $credentialsPath -Raw -Encoding UTF8 | ConvertFrom-Json

    # PLAN-42: the gateway proof pair, made once here (.acceptance is git-ignored). No key is ever in the repository.
    $proofKeysPath = Join-Path $RepoRoot ".acceptance/gateway-proof-keys.json"
    # Called in this process: the runner has pwsh, this machine Windows PowerShell.
    & (Join-Path (Join-Path $RepoRoot "scripts") "s2s042-new-gateway-proof-keys.ps1") -OutFile $proofKeysPath -RepoRoot $RepoRoot | Out-Null
    $proofKeys = Get-Content -LiteralPath $proofKeysPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $env:ACCEPTANCE_GATEWAY_PROOF_PRIVATE_KEY_B64 = [string]$proofKeys.privateKeyB64
    $env:ACCEPTANCE_GATEWAY_PROOF_PUBLIC_KEY_B64 = [string]$proofKeys.publicKeyB64

    # PLAN-42 (P-6): the secret of the svc-* clients of the acceptance realm, generated with the personas.
    $serviceProperty = $credentials.PSObject.Properties["serviceClientSecret"]
    $serviceSecret = if ($null -eq $serviceProperty) { $null } else { [string]$serviceProperty.Value }
    if ([string]::IsNullOrWhiteSpace($serviceSecret)) {
        throw "Acceptance service client secret is missing. Run scripts/prepare-keycloak-import.ps1 -Profile Acceptance."
    }
    $env:ACCEPTANCE_SVC_CLIENT_SECRET = $serviceSecret
    if (-not $env:RABBITMQ_HOST) { $env:RABBITMQ_HOST = "localhost" }
    if (-not $env:RABBITMQ_PORT) { $env:RABBITMQ_PORT = "6573" }
    if (-not $env:RABBITMQ_DEFAULT_USER) {
        $env:RABBITMQ_DEFAULT_USER = Get-HrmDotEnvValue -RepoRoot $RepoRoot -Name "RABBITMQ_DEFAULT_USER"
    }
    if (-not $env:RABBITMQ_DEFAULT_PASS) {
        $env:RABBITMQ_DEFAULT_PASS = Get-HrmDotEnvValue -RepoRoot $RepoRoot -Name "RABBITMQ_DEFAULT_PASS"
    }
}
