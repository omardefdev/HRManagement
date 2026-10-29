#Requires -Version 5.1
<#
.SYNOPSIS
  Builds and starts the isolated Acceptance v2 stack.
#>
param(
    [switch]$SkipComposeUp,
    [switch]$NoBuild,
    [switch]$SkipLiveVerification,
    [switch]$RequireCleanBaseline,
    [switch]$UseLocalBaseImages,
    [string]$GatewayBaseUrl = $(if ($env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL } else { "http://localhost:6200" })
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$projectName = "hrm-acceptance"
$env:COMPOSE_PROJECT_NAME = $projectName
$env:GATEWAY_BASE_URL = $GatewayBaseUrl
$env:KEYCLOAK_BASE_URL = "http://localhost:6388"
$env:POSTGRES_HOST = "localhost"
$env:POSTGRES_PORT = "6433"

Write-Host "== Preparing the canonical Acceptance v2 Keycloak realm =="
& "$PSScriptRoot/prepare-keycloak-import.ps1" -Profile Acceptance
. "$PSScriptRoot/acceptance-local-env.ps1"
Import-AcceptanceLocalEnvironment -RepoRoot $root

$composeFiles = @(
    "docker-compose.yml",
    "docker-compose.services.yml",
    "docker-compose.dev-profiles.yml",
    "docker-compose.full-stack-gateway.yml",
    "docker-compose.acceptance.yml"
)
$composeArgs = $composeFiles | ForEach-Object { "-f", $_ }

Write-Host "== Validating isolated Compose configuration =="
docker compose -p $projectName @composeArgs --profile full-stack config --quiet
if ($LASTEXITCODE -ne 0) { throw "Acceptance Compose configuration is invalid." }

if (-not $SkipComposeUp) {
    if (-not $NoBuild) {
        Write-Host "== Building all Acceptance v2 services from the current source =="
        $buildArgs = @("build")
        $mcrDnsAvailable = $false
        try {
            $mcrDnsAvailable = [System.Net.Dns]::GetHostAddresses("mcr.microsoft.com").Count -gt 0
        }
        catch {
            $mcrDnsAvailable = $false
        }

        $sdkImage = "mcr.microsoft.com/dotnet/sdk:10.0.400"
        $runtimeSource = "$projectName-hr-gateway:latest"
        $localSdk = "hrm-local-dotnet-sdk:10.0.400"
        $localRuntime = "hrm-local-dotnet-runtime:10.0"
        docker image inspect $localSdk *> $null
        $hasLocalSdk = $LASTEXITCODE -eq 0
        docker image inspect $localRuntime *> $null
        $hasLocalRuntime = $LASTEXITCODE -eq 0

        # A cached runtime already contains curl/Kerberos. Reusing it avoids fourteen
        # identical apt downloads while every service is still published from source.
        if ($UseLocalBaseImages -or ($hasLocalSdk -and $hasLocalRuntime) -or -not $mcrDnsAvailable) {
            if (-not ($hasLocalSdk -and $hasLocalRuntime)) {
                docker image inspect $sdkImage *> $null
                $hasSdkSource = $LASTEXITCODE -eq 0
                docker image inspect $runtimeSource *> $null
                $hasRuntimeSource = $LASTEXITCODE -eq 0
                if (-not ($hasSdkSource -and $hasRuntimeSource)) {
                    throw "Local Acceptance SDK/runtime base images do not exist and the remote base cannot be bootstrapped. Restore connectivity once to create them."
                }

                docker tag $sdkImage $localSdk
                docker tag $runtimeSource $localRuntime
            }

            $buildArgs += @(
                "--build-arg", "SDK_BASE_IMAGE=$localSdk",
                "--build-arg", "RUNTIME_BASE_IMAGE=$localRuntime",
                "--build-arg", "REUSE_LOCAL_RUNTIME_BASE=true"
            )
            Write-Host "Using the verified local .NET base layers for this Acceptance build."
        }

        docker compose -p $projectName @composeArgs --profile full-stack @buildArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Acceptance image build failed with exit code $LASTEXITCODE"
        }
    }

    # Named volumes are created as root-owned on a clean Docker host. Prepare
    # the shared key ring before non-root ASP.NET containers attempt to write it.
    $dataProtectionVolume = "${projectName}_acceptance_data_protection"
    docker volume create $dataProtectionVolume | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the Acceptance Data Protection volume."
    }
    docker run --rm --user 0:0 --entrypoint sh `
        -v "${dataProtectionVolume}:/var/hrm/dpkeys" `
        "${projectName}-hr-gateway:latest" `
        -c "mkdir -p /var/hrm/dpkeys && chown -R 1654:1654 /var/hrm/dpkeys"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not prepare ownership of the Acceptance Data Protection volume."
    }

    # PLAN-30 (COM-030): host folder of the candidate notification FileDrop (bind-mounted into hr-core-web). Create it
    # before compose does (as root) and let the non-root app user (1654) write it; a no-op on Docker Desktop for Windows.
    $candidateNotifications = Join-Path $root "artifacts/acceptance/candidate-notifications"
    New-Item -ItemType Directory -Force -Path $candidateNotifications | Out-Null
    docker run --rm --user 0:0 --entrypoint sh `
        -v "${candidateNotifications}:/var/hrm/candidate-notifications" `
        "${projectName}-hr-gateway:latest" `
        -c "chown 1654:1654 /var/hrm/candidate-notifications && chmod 0775 /var/hrm/candidate-notifications"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not prepare the candidate notification FileDrop folder."
    }

    Write-Host "== Starting hrm-acceptance with Seed__Profile=Acceptance =="
    $upArgs = @("up", "-d", "--wait", "--no-build")
    docker compose -p $projectName @composeArgs --profile full-stack @upArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose up failed with exit code $LASTEXITCODE"
    }
}

$keycloakContainer = (docker compose -p $projectName @composeArgs ps -q keycloak).Trim()
if ([string]::IsNullOrWhiteSpace($keycloakContainer)) {
    throw "Acceptance Keycloak container is not running."
}

Write-Host "== Syncing Keycloak service clients and scopes =="
& "$PSScriptRoot/sync-keycloak-dev-realm.ps1" -ContainerName $keycloakContainer

Write-Host "== Applying PostgreSQL per-service roles by Compose service name =="
docker compose -p $projectName @composeArgs exec -T postgresql bash /docker-entrypoint-initdb.d/02-init-service-roles.sh
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL service-role initialization failed." }

if (-not $SkipLiveVerification) {
    Write-Host "== Verifying gateway and all fourteen services =="
    & "$PSScriptRoot/verify-staging-stack.ps1" -GatewayBaseUrl $GatewayBaseUrl
    if (Test-Path "$PSScriptRoot/acceptance-verify.ps1") {
        $verificationParameters = @{
            ComposeProjectName = $projectName
        }
        if (-not $RequireCleanBaseline) {
            $verificationParameters.OperationalRun = $true
        }

        if ($PSVersionTable.PSVersion.Major -ge 7) {
            & "$PSScriptRoot/acceptance-verify.ps1" @verificationParameters
        }
        else {
            $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
            if ($null -eq $pwsh) {
                throw "Acceptance verification requires PowerShell 7. Install pwsh or run acceptance-up.ps1 with -SkipLiveVerification."
            }
            Write-Host "== Running Acceptance verification with PowerShell 7 =="
            $verificationArguments = @(
                "-NoProfile",
                "-File", "$PSScriptRoot/acceptance-verify.ps1",
                "-ComposeProjectName", $projectName
            )
            if (-not $RequireCleanBaseline) {
                $verificationArguments += "-OperationalRun"
            }
            & $pwsh.Source @verificationArguments
            if ($LASTEXITCODE -ne 0) {
                throw "Acceptance verification failed with exit code $LASTEXITCODE"
            }
        }
    }
}

Write-Host "Acceptance v2 is ready."
Write-Host "Gateway: $GatewayBaseUrl"
Write-Host "Keycloak: http://localhost:6388"
Write-Host "Local account guide: $root\.acceptance\accounts.local.md"
Write-Host "Run tests with: pwsh scripts/acceptance-run-all.ps1"
