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
    $proofProperty = $credentials.PSObject.Properties["gatewayCallerProofSecret"]
    $proofSecret = if ($null -eq $proofProperty) { $null } else { [string]$proofProperty.Value }
    if ([string]::IsNullOrWhiteSpace($proofSecret)) {
        throw "Acceptance gateway caller-proof secret is missing. Regenerate the local Acceptance credentials."
    }

    $env:ACCEPTANCE_GATEWAY_CALLER_PROOF_SECRET = $proofSecret
    if (-not $env:RABBITMQ_HOST) { $env:RABBITMQ_HOST = "localhost" }
    if (-not $env:RABBITMQ_PORT) { $env:RABBITMQ_PORT = "6573" }
    if (-not $env:RABBITMQ_DEFAULT_USER) {
        $env:RABBITMQ_DEFAULT_USER = Get-HrmDotEnvValue -RepoRoot $RepoRoot -Name "RABBITMQ_DEFAULT_USER"
    }
    if (-not $env:RABBITMQ_DEFAULT_PASS) {
        $env:RABBITMQ_DEFAULT_PASS = Get-HrmDotEnvValue -RepoRoot $RepoRoot -Name "RABBITMQ_DEFAULT_PASS"
    }
}
