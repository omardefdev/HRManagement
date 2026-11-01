#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-38 (section 3, P-1): fills only the MISSING keys of a local lab env file (for example .env.leastpriv) and never
  overwrites a value. The server counterpart is deploy/vps/init-secrets.sh.
.DESCRIPTION
  Required keys = every ${VAR:?} of the given compose files + every key of the example file + the role and broker
  matrices (deploy/bootstrap/postgres-roles.conf, rabbitmq-users.json). For each missing or empty key:
    - a secret (name ends in PASSWORD, _PASS, SECRET, _KEY or TOKEN) is generated: 32 random bytes from
      RandomNumberGenerator, base64url;
    - a non-secret takes the example's non-empty default;
    - a non-secret without a default is REQUIRED: give it with -Set KEY=VALUE (refused for secret names) or the run
      fails, writing nothing.
  The target must be outside the git work tree or git-ignored. Prints key NAMES only, never a value.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/init-secrets.ps1 -EnvFile .env.leastpriv `
    -Example deploy/acceptance/leastpriv.env.example -ComposeFile docker-compose.yml,docker-compose.acceptance-leastpriv.yml
#>
param(
    [Parameter(Mandatory = $true)][string]$EnvFile,
    [string]$Example = 'deploy/vps/.env.vps.example',
    [string[]]$ComposeFile = @('docker-compose.yml', 'docker-compose.services.yml', 'docker-compose.images.yml',
        'docker-compose.production.yml', 'docker-compose.vps.yml', 'docker-compose.vps-demo.yml'),
    [string[]]$Set = @(),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
$allowEmpty = @('ACME_EMAIL', 'ADMIN_ALLOWED_CIDRS', 'IMAGE_TAG')

function Write-Log([string]$Message) { Write-Host "[init-secrets] $Message" }
function Test-Secret([string]$Key) { return $Key -match '(PASSWORD|_PASS|SECRET|_KEY|TOKEN)$' }
function Resolve-RepoPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return (Join-Path $repoRoot $Path)
}
function New-Secret {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([Convert]::ToBase64String($bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
function Read-EnvValues([string]$Path) {
    $values = @{}
    if (Test-Path -LiteralPath $Path) {
        foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
            if ($line -match '^([A-Z][A-Z0-9_]*)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim('"') }
        }
    }
    return $values
}

$envPath = Resolve-RepoPath $EnvFile
$examplePath = Resolve-RepoPath $Example
if (-not (Test-Path -LiteralPath $examplePath)) { throw "Example file not found: $examplePath" }

# A target inside the git work tree must be git-ignored.
$envDir = Split-Path -Parent $envPath
if (-not (Test-Path -LiteralPath $envDir)) { New-Item -ItemType Directory -Force -Path $envDir | Out-Null }
# PS 5.1 turns a native command's stderr into an error record; under 'Stop' that would end the script.
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$null = & git -C $envDir rev-parse --is-inside-work-tree 2>&1
$insideWorkTree = ($LASTEXITCODE -eq 0)
$ignored = $false
if ($insideWorkTree) {
    $null = & git -C $envDir check-ignore -q -- $envPath 2>&1
    $ignored = ($LASTEXITCODE -eq 0)
}
$ErrorActionPreference = $previousPreference
if ($insideWorkTree -and -not $ignored) {
    throw "$envPath is inside the git work tree and not git-ignored (add it to .gitignore first)."
}

$setValues = @{}
# Under powershell -File a comma list arrives as one string: split before every KEY=.
foreach ($kv in ($Set | ForEach-Object { $_ -split ',(?=[A-Z][A-Z0-9_]*=)' })) {
    $k = ($kv -split '=', 2)[0]
    if ($k -notmatch '^[A-Z][A-Z0-9_]*$') { throw "-Set: invalid key name" }
    if (Test-Secret $k) { throw "-Set refuses secret key $k (secrets are generated, never passed on a command line)." }
    $setValues[$k] = ($kv -split '=', 2)[1]
}

# ---- required key names ------------------------------------------------------------------------------------------
$keys = New-Object 'System.Collections.Generic.SortedSet[string]' ([System.StringComparer]::Ordinal)
foreach ($f in $ComposeFile) {
    $p = Resolve-RepoPath $f
    if (-not (Test-Path -LiteralPath $p)) { throw "Compose file not found: $p" }
    foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($p), '\$\{([A-Z_][A-Z0-9_]*):\?')) { [void]$keys.Add($m.Groups[1].Value) }
}
$exampleValues = Read-EnvValues $examplePath
foreach ($k in $exampleValues.Keys) { [void]$keys.Add($k) }
$roles = Join-Path $repoRoot 'deploy\bootstrap\postgres-roles.conf'
foreach ($line in [System.IO.File]::ReadAllLines($roles)) {
    if ($line -and -not $line.StartsWith('#')) {
        $prefix = ($line -split '\|')[3]
        [void]$keys.Add("${prefix}_APP_PASSWORD"); [void]$keys.Add("${prefix}_MIGRATOR_PASSWORD")
    }
}
$mq = Join-Path $repoRoot 'deploy\bootstrap\rabbitmq-users.json'
if (Test-Path -LiteralPath $mq) {
    foreach ($u in ([System.IO.File]::ReadAllText($mq) | ConvertFrom-Json).users) { [void]$keys.Add($u.passwordEnv) }
}

$current = Read-EnvValues $envPath
$new = [ordered]@{}
$generated = @(); $defaults = @(); $fromSet = @(); $empties = @(); $missing = @(); $kept = 0
foreach ($k in $keys) {
    if ($current.ContainsKey($k) -and $current[$k] -ne '') { $kept++; continue }
    if ($setValues.ContainsKey($k)) { $new[$k] = $setValues[$k]; $fromSet += $k; continue }
    if (Test-Secret $k) { $new[$k] = New-Secret; $generated += $k; continue }
    if ($exampleValues.ContainsKey($k) -and $exampleValues[$k] -ne '') { $new[$k] = $exampleValues[$k]; $defaults += $k; continue }
    if ($allowEmpty -contains $k) { if (-not $current.ContainsKey($k)) { $new[$k] = ''; $empties += $k }; continue }
    $missing += $k
}

if ($missing.Count -gt 0) {
    Write-Log "ERROR: required non-secret key(s) without a value (give them with -Set KEY=VALUE): $($missing -join ' ')"
    exit 2
}
if ($Check) {
    if ($new.Count -eq 0) { Write-Log "check: all $($keys.Count) keys present (values not shown)"; exit 0 }
    Write-Log "check: missing: $(@($new.Keys) -join ' ')"; exit 1
}

if ($new.Count -gt 0) {
    $out = New-Object System.Collections.Generic.List[string]
    $done = @{}
    if (Test-Path -LiteralPath $envPath) {
        foreach ($line in [System.IO.File]::ReadAllLines($envPath)) {
            if ($line -match '^([A-Z][A-Z0-9_]*)=(.*)$' -and $new.Contains($Matches[1]) -and $Matches[2] -eq '') {
                $out.Add("$($Matches[1])=$($new[$Matches[1]])"); $done[$Matches[1]] = $true
            } else { $out.Add($line) }
        }
    }
    foreach ($k in $new.Keys) { if (-not $done.ContainsKey($k)) { $out.Add("$k=$($new[$k])") } }
    # LF line endings and no BOM: docker compose would otherwise keep a trailing CR in every value.
    $text = ($out -join "`n") + "`n"
    [System.IO.File]::WriteAllText($envPath, $text, (New-Object System.Text.UTF8Encoding($false)))
}

Write-Log "env file ${envPath}: $kept key(s) kept unchanged"
if ($generated.Count) { Write-Log "generated ($($generated.Count)): $($generated -join ' ')" }
if ($defaults.Count) { Write-Log "example defaults ($($defaults.Count)): $($defaults -join ' ')" }
if ($fromSet.Count) { Write-Log "set from -Set ($($fromSet.Count)): $($fromSet -join ' ')" }
if ($empties.Count) { Write-Log "added empty (allowed): $($empties -join ' ')" }
if ($new.Count -eq 0) { Write-Log 'nothing to add' }
