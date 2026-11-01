<#
.SYNOPSIS
  PLAN-36 (section 6): builds the deploy bundle artifacts/vps/hrm-deploy-<tag>.tar.gz for the single host.

.DESCRIPTION
  The bundle holds what the server needs besides the images and its own .env: the compose files of the single-host
  chain, deploy/vps/* (Caddyfile, clamd.conf, host-setup.sh, remote-deploy.sh, smoke.sh, .env.vps.example), the
  generated VpsDemo realm (artifacts/vps/import, placeholders only), the PostgreSQL init scripts, the backup script
  and a RELEASE file. Files come from the committed HEAD (git show), never from the working tree.
  It fails when the bundle would contain a file named .env (or .env.<anything> other than .env.vps.example) or when
  gitleaks (the pinned image of docs/qa/tool-pins.json, repository .gitleaks.toml) finds a secret in it.
  PowerShell 5.1:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/make-deploy-bundle.ps1 [-Tag <sha12>]
#>
[CmdletBinding()]
param(
    [string]$Tag = '',
    [string]$RealmImportDir = '',
    [switch]$SkipSecretScan
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
if (-not $RealmImportDir) { $RealmImportDir = Join-Path $repoRoot 'artifacts\vps\import' }

function ConvertTo-ArgumentString([string[]]$Arguments) {
    ($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ConvertTo-ArgumentString $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    return @{ ExitCode = $proc.ExitCode; StdOut = $stdout; StdErr = $stderrTask.Result }
}

# Committed files of the bundle (relative to the repository root). The release directory on the server mirrors them.
$BundleFiles = @(
    'docker-compose.yml',
    'docker-compose.services.yml',
    'docker-compose.images.yml',
    'docker-compose.production.yml',
    'docker-compose.vps.yml',
    'docker-compose.vps-demo.yml',
    'docker-compose.vps-local.yml',
    # PLAN-38 (D-38-2): added by remote-deploy.sh only when HRM_MQ_POSTURE=admin.
    'docker-compose.posture-mq-admin.yml',
    'deploy/vps/Caddyfile',
    'deploy/vps/clamav/clamd.conf',
    'deploy/vps/host-setup.sh',
    'deploy/vps/remote-deploy.sh',
    'deploy/vps/smoke.sh',
    'deploy/vps/.env.vps.example',
    # PLAN-38: fills only the missing keys of the server's .env (names printed, never values).
    'deploy/vps/init-secrets.sh',
    # PLAN-37: keycloak-sync-clients.sh and smoke.sh read the wiring matrix; smoke.sh compares the legal pack's SHA-256.
    'deploy/vps/keycloak-sync-clients.sh',
    'deploy/wiring/service-wiring.json',
    'docs/syr/service-regimes.json',
    # PLAN-38: the postgres container's first-init scripts are the bootstrap's own idempotent SQL.
    'deploy/bootstrap/ensure-databases.sql',
    'deploy/bootstrap/ensure-roles.sql',
    'deploy/bootstrap/postgres-roles.conf',
    # PLAN-38: init-secrets.sh reads the broker users' password variables from the matrix.
    'deploy/bootstrap/rabbitmq-users.json',
    'docker/init-service-roles.sh',
    'scripts/backup-postgres.sh',
    'scripts/lib/backup-catalog.sh'
)

Push-Location $repoRoot
try {
    $head = (Invoke-Native 'git' @('rev-parse', '--short=12', 'HEAD')).StdOut.Trim()
    if (-not $Tag) { $Tag = $head }
    if ($Tag -notmatch '^[0-9a-f]{12}$') { throw "Tag '$Tag' is not a 12-character commit SHA." }

    $artifacts = Join-Path $repoRoot 'artifacts\vps'
    $stage = Join-Path $artifacts "bundle-stage\hrm-deploy-$Tag"
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    foreach ($rel in $BundleFiles) {
        $dest = Join-Path $stage ($rel -replace '/', '\')
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        # git show writes the committed bytes (LF line endings for .sh, as .gitattributes stores them).
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = 'git'
        $psi.Arguments = ConvertTo-ArgumentString @('show', "${Tag}:$rel")
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        $fs = [System.IO.File]::Create($dest)
        try { $proc.StandardOutput.BaseStream.CopyTo($fs) } finally { $fs.Dispose() }
        $err = $proc.StandardError.ReadToEnd()
        $proc.WaitForExit()
        if ($proc.ExitCode -ne 0) { throw "git show ${Tag}:$rel failed: $err" }
    }

    # The VpsDemo realm (generated, git-ignored): placeholders only, resolved by Keycloak on the server.
    $realmFiles = @(Get-ChildItem -Path $RealmImportDir -Filter '*.json' -File -ErrorAction SilentlyContinue)
    if ($realmFiles.Count -eq 0) {
        throw "No realm file in $RealmImportDir. Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-keycloak-import.ps1 -Profile VpsDemo -AppDomain <APP_DOMAIN>"
    }
    $importDest = Join-Path $stage 'deploy\vps\keycloak-import'
    New-Item -ItemType Directory -Force -Path $importDest | Out-Null
    foreach ($f in $realmFiles) { Copy-Item $f.FullName (Join-Path $importDest $f.Name) }

    [System.IO.File]::WriteAllText((Join-Path $stage 'RELEASE'), "tag=$Tag`ncommit=$((Invoke-Native 'git' @('rev-parse', $Tag)).StdOut.Trim())`nbuiltAtUtc=$((Get-Date).ToUniversalTime().ToString('o'))`n", (New-Object System.Text.UTF8Encoding($false)))

    # Refuse any .env file (the server's .env lives in /opt/hrm/shared and is never shipped).
    $envFiles = @(Get-ChildItem -Path $stage -Recurse -Force -File | Where-Object { $_.Name -eq '.env' -or ($_.Name -like '.env.*' -and $_.Name -ne '.env.vps.example') })
    if ($envFiles.Count -gt 0) { throw "The bundle would contain an env file: $(($envFiles | ForEach-Object { $_.FullName.Substring($stage.Length) }) -join ', ')" }

    if (-not $SkipSecretScan) {
        $pins = [System.IO.File]::ReadAllText((Join-Path $repoRoot 'docs\qa\tool-pins.json')) | ConvertFrom-Json
        $ref = $pins.tools.gitleaks.ref
        $scanOut = Join-Path $artifacts 'bundle-scan'
        New-Item -ItemType Directory -Force -Path $scanOut | Out-Null
        $report = Join-Path $scanOut "gitleaks-bundle-$Tag.json"
        Remove-Item $report -ErrorAction SilentlyContinue
        $scan = Invoke-Native 'docker' @('run', '--rm', '-v', "${stage}:/bundle:ro", '-v', "${scanOut}:/out",
            '-v', "$(Join-Path $repoRoot '.gitleaks.toml'):/config/.gitleaks.toml:ro", $ref,
            'detect', '--no-git', '--source', '/bundle', '--config', '/config/.gitleaks.toml', '--redact', '--no-banner',
            '--no-color', '--exit-code', '1', '--report-format', 'json', '--report-path', "/out/gitleaks-bundle-$Tag.json")
        if ($scan.ExitCode -ne 0) {
            $count = 'unknown'
            if (Test-Path $report) { $count = @(Get-Content -Raw $report | ConvertFrom-Json).Count }
            throw "gitleaks found $count potential secret(s) in the bundle (redacted report: $report)`n$($scan.StdErr)"
        }
        Write-Host "[scan] gitleaks: no secrets in the bundle"
    }

    $archive = Join-Path $artifacts "hrm-deploy-$Tag.tar.gz"
    Remove-Item $archive -ErrorAction SilentlyContinue
    $tar = Invoke-Native 'tar.exe' @('-czf', $archive, '-C', (Split-Path -Parent $stage), "hrm-deploy-$Tag")
    if ($tar.ExitCode -ne 0) { throw "tar failed: $($tar.StdErr)" }
    Remove-Item -Recurse -Force $stage
    $size = (Get-Item $archive).Length
    Write-Host ("[done] {0} ({1:N0} KB, {2} files + realm)" -f $archive, ($size / 1KB), $BundleFiles.Count)
    Write-Output $archive
}
finally {
    Pop-Location
}
