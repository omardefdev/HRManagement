<#
.SYNOPSIS
    BLD-002: run HR.Core locally (uses host NuGet cache — fast rebuild).
.DESCRIPTION
    Runs against the infrastructure started by scripts/dev-infra-up.ps1. appsettings.Development.json carries no
    secrets, so the credentials are read from .env and passed as environment variables pointing at the published host
    ports (PostgreSQL 5433, RabbitMQ 5673, Keycloak 8088). A variable already set in the environment wins over .env.
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Urls = "http://localhost:5010"
)

$ErrorActionPreference = 'Stop'
Push-Location $RepoRoot
try {
    if (-not (Test-Path '.env')) {
        throw ".env not found in $RepoRoot. Create it first (for example: bash scripts/prepare-ci-compose-env.sh)."
    }
    $dotenv = @{}
    foreach ($line in Get-Content '.env') {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') { $dotenv[$Matches[1]] = $Matches[2].Trim() }
    }
    function Get-Setting([string]$Key) {
        if (-not $dotenv.ContainsKey($Key) -or -not $dotenv[$Key]) { throw "$Key is missing or empty in .env" }
        return $dotenv[$Key]
    }
    function Set-IfUnset([string]$Name, [string]$Value) {
        if (-not [Environment]::GetEnvironmentVariable($Name)) { [Environment]::SetEnvironmentVariable($Name, $Value) }
    }

    Set-IfUnset 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-IfUnset 'ConnectionStrings__HRCoreDb' ("Host=localhost;Port=5433;Database=hr_core_db;Username={0};Password={1}" -f `
            (Get-Setting 'POSTGRES_USER'), (Get-Setting 'POSTGRES_PASSWORD'))
    Set-IfUnset 'RabbitMQ__Host' 'localhost'
    Set-IfUnset 'RabbitMQ__Port' '5673'
    Set-IfUnset 'RabbitMQ__VirtualHost' '/'
    Set-IfUnset 'RabbitMQ__Username' (Get-Setting 'RABBITMQ_DEFAULT_USER')
    Set-IfUnset 'RabbitMQ__Password' (Get-Setting 'RABBITMQ_DEFAULT_PASS')
    Set-IfUnset 'Keycloak__Authority' 'http://localhost:8088/realms/hrmanagement'
    Set-IfUnset 'FileStorage__Provider' 'Local'
    Set-IfUnset 'FileStorage__LocalRootPath' (Join-Path ([System.IO.Path]::GetTempPath()) 'hrm-file-storage')
    Set-IfUnset 'ServiceIdentity__ClientId' 'svc-hr-core'
    if ($dotenv['SVC_HR_CORE_CLIENT_SECRET']) { Set-IfUnset 'ServiceIdentity__ClientSecret' $dotenv['SVC_HR_CORE_CLIENT_SECRET'] }
    # A stable, per-service key ring outside the repo: the national IDs HR.Core protects stay readable across restarts,
    # and the key-ring canary checks this ring instead of reporting "(none)".
    $appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    Set-IfUnset 'DataProtection__KeyRingPath' (Join-Path $appData 'hrm/dpkeys/hr-core')

    dotnet run --project src/Services/HRCore/HR.Core.Web/HR.Core.Web.csproj --urls $Urls
}
finally {
    Pop-Location
}
