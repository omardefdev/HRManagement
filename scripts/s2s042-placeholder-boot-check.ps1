#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-42 (AC-S2S-42e): the hr-core-web image with a placeholder as the gateway proof public key must refuse to start, and say which
  key without printing its value. ASCII only, Windows PowerShell 5.1 and PowerShell 7.

.DESCRIPTION
  Runs the image alone with no network, with the environment of the running hr-core-web container, so that the only wrong value is the
  public key. The guard stops the start before anything connects. The environment passes through a temporary file that is deleted at
  the end, and no value is printed. Exit 0 when the container exited non-zero and its log names the key; exit 1 otherwise.

.PARAMETER Image
  Default: the image of the running container.

.PARAMETER Container
  Default: the running container of the Compose service hr-core-web.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/s2s042-placeholder-boot-check.ps1
#>
param(
    [string]$Image = "",
    [string]$Container = ""
)

$ErrorActionPreference = "Continue"
$placeholder = "USE_ENV_OR_SECRET_MANAGER"
$key = "GatewayCallerProof:PublicKeyPem"

if ([string]::IsNullOrWhiteSpace($Container)) {
    $Container = @(& docker ps --filter "label=com.docker.compose.service=hr-core-web" --format "{{.Names}}") | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($Container)) {
    Write-Host "s2s042-placeholder-boot-check: FAIL: no running hr-core-web container"
    exit 1
}
if ([string]::IsNullOrWhiteSpace($Image)) {
    $Image = (& docker inspect -f "{{.Config.Image}}" $Container | Out-String).Trim()
}

$envFile = Join-Path ([System.IO.Path]::GetTempPath()) ("hrm-boot-" + [guid]::NewGuid().ToString("N") + ".env")
try {
    $environment = (& docker inspect -f "{{json .Config.Env}}" $Container | Out-String) | ConvertFrom-Json
    # A multi-line value cannot pass through an env file. The key under test and the environment name come from -e below.
    $lines = @($environment | Where-Object { $_ -notmatch "[\r\n]" -and $_ -notmatch "^(ASPNETCORE_ENVIRONMENT|GatewayCallerProof__PublicKeyPem)=" })
    [System.IO.File]::WriteAllLines($envFile, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))

    $output = & docker run --rm --network none --env-file $envFile `
        -e ASPNETCORE_ENVIRONMENT=Production `
        -e "GatewayCallerProof__PublicKeyPem=$placeholder" `
        $Image 2>&1 | Out-String
    $exit = $LASTEXITCODE
}
finally {
    Remove-Item -LiteralPath $envFile -Force -ErrorAction SilentlyContinue
}

if ($exit -eq 0) {
    Write-Host "s2s042-placeholder-boot-check: FAIL: the container exited 0"
    exit 1
}
if ($output -notmatch [regex]::Escape($key)) {
    $reason = @($output -split "`n" | Where-Object { $_ -match "required|refus|Unhandled" } | Select-Object -First 3) -join " / "
    Write-Host "s2s042-placeholder-boot-check: FAIL: exit $exit, but the log does not name $key. First reasons: $reason"
    exit 1
}
if ($output -match [regex]::Escape($placeholder)) {
    Write-Host "s2s042-placeholder-boot-check: FAIL: the log prints the value"
    exit 1
}

Write-Host "s2s042-placeholder-boot-check: PASS: exit $exit, $key named, value not shown"
exit 0
