#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-42 (P-2): makes the ECDSA P-256 key pair of the gateway proof. ASCII only, Windows PowerShell 5.1 and PowerShell 7.

.DESCRIPTION
  The keys are written as a single-line base64 of the DER (PKCS#8 private, SPKI public), because .env and Compose do not carry
  multi-line values safely. Windows PowerShell 5.1 runs on .NET Framework, which cannot export those formats, so the pair comes
  from openssl when it is on the PATH, otherwise from the small tool tools/S2sKeyGen (net10, no network). No online service.

  It prints the SHA-256 fingerprint of the public key only, never a key.

.PARAMETER OutFile
  JSON file with privateKeyB64, publicKeyB64 and publicKeySha256. An existing file is kept, unless -Force.

.PARAMETER EnvFile
  Adds GATEWAY_PROOF_PRIVATE_KEY_B64 and GATEWAY_PROOF_PUBLIC_KEY_B64 to a dotenv file when both are missing or empty. Existing
  values are never overwritten, and a file with only one of the two is refused.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/s2s042-new-gateway-proof-keys.ps1 -OutFile .acceptance/gateway-proof-keys.json
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/s2s042-new-gateway-proof-keys.ps1 -EnvFile .env.leastpriv
#>
param(
    [string]$OutFile = "",
    [string]$EnvFile = "",
    [switch]$Force,
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($OutFile) -and [string]::IsNullOrWhiteSpace($EnvFile)) { throw "Give -OutFile or -EnvFile." }
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function New-KeyPairDer([string]$Directory) {
    $private = Join-Path $Directory "private.der"
    $public = Join-Path $Directory "public.der"
    $openssl = Get-Command openssl -ErrorAction SilentlyContinue
    if ($null -ne $openssl) {
        $pem = Join-Path $Directory "key.pem"
        & openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out $pem
        if ($LASTEXITCODE -ne 0) { throw "openssl genpkey failed." }
        # pkcs8 -topk8, not pkey: pkey -outform DER writes the SEC1 form, which the loader refuses.
        & openssl pkcs8 -topk8 -nocrypt -in $pem -outform DER -out $private
        if ($LASTEXITCODE -ne 0) { throw "openssl pkcs8 failed." }
        & openssl pkey -in $pem -pubout -outform DER -out $public
        if ($LASTEXITCODE -ne 0) { throw "openssl pkey -pubout failed." }
    }
    else {
        & dotnet run --project (Join-Path (Join-Path $RepoRoot "tools") "S2sKeyGen") -- $private $public
        if ($LASTEXITCODE -ne 0) { throw "tools/S2sKeyGen failed." }
    }
    return @{ Private = [System.IO.File]::ReadAllBytes($private); Public = [System.IO.File]::ReadAllBytes($public) }
}

function New-KeyPair {
    $directory = Join-Path ([System.IO.Path]::GetTempPath()) ("hrm-proof-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $directory | Out-Null
    try {
        $der = New-KeyPairDer -Directory $directory
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $fingerprint = (([System.BitConverter]::ToString($sha.ComputeHash($der.Public))) -replace '-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
        return [ordered]@{
            privateKeyB64   = [Convert]::ToBase64String($der.Private)
            publicKeyB64    = [Convert]::ToBase64String($der.Public)
            publicKeySha256 = $fingerprint
        }
    }
    finally {
        Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    if ((Test-Path -LiteralPath $OutFile) -and -not $Force) {
        Write-Host "s2s042-new-gateway-proof-keys: $OutFile exists, kept"
    }
    else {
        $pair = New-KeyPair
        $parent = Split-Path -Parent $OutFile
        if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
        [System.IO.File]::WriteAllText($OutFile, (($pair | ConvertTo-Json) + "`n"), $utf8NoBom)
        Write-Host "s2s042-new-gateway-proof-keys: public key sha256 $($pair.publicKeySha256)"
    }
}

if (-not [string]::IsNullOrWhiteSpace($EnvFile)) {
    $names = @("GATEWAY_PROOF_PRIVATE_KEY_B64", "GATEWAY_PROOF_PUBLIC_KEY_B64")
    $lines = if (Test-Path -LiteralPath $EnvFile) { @([System.IO.File]::ReadAllLines($EnvFile)) } else { @() }
    $filled = @($names | Where-Object { $name = $_; @($lines | Where-Object { $_ -match "^$name=.+" }).Count -gt 0 })
    if ($filled.Count -eq 1) { throw "$EnvFile holds only $($filled[0]): a pair is replaced whole, by hand." }
    if ($filled.Count -eq 2) {
        Write-Host "s2s042-new-gateway-proof-keys: $EnvFile already has the pair, kept"
    }
    else {
        $pair = New-KeyPair
        $kept = @($lines | Where-Object { $_ -notmatch "^GATEWAY_PROOF_(PRIVATE|PUBLIC)_KEY_B64=" })
        $kept += "GATEWAY_PROOF_PRIVATE_KEY_B64=$($pair.privateKeyB64)"
        $kept += "GATEWAY_PROOF_PUBLIC_KEY_B64=$($pair.publicKeyB64)"
        [System.IO.File]::WriteAllText($EnvFile, (($kept -join "`n") + "`n"), $utf8NoBom)
        Write-Host "s2s042-new-gateway-proof-keys: public key sha256 $($pair.publicKeySha256)"
    }
}
exit 0
