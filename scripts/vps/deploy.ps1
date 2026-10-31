<#
.SYNOPSIS
  PLAN-36 (section 6): deploys a release of the single-host chain to the server over ssh (default host hrm-vps), or
  to this machine with -Local, and rolls back with -Rollback.

.DESCRIPTION
  Remote (default):
    1. reads APP_DOMAIN from the server's /opt/hrm/shared/.env (a host name, not a secret) and renders the VpsDemo
       realm for it (scripts/prepare-keycloak-import.ps1 -Profile VpsDemo; placeholders only);
    2. builds the bundle (scripts/vps/make-deploy-bundle.ps1), copies it with scp and extracts it to
       /opt/hrm/releases/<tag>;
    3. runs deploy/vps/remote-deploy.sh check-env (key names and lengths only), then deploy <tag> (pull, up, wait for
       healthy), then the self-test (scripts/vps/smoke.ps1);
    4. remote-deploy.sh keeps the previous tag; -Rollback redeploys it and runs the self-test.
  -Local -EnvFile deploy/vps/.env.vps-local: the same remote-deploy.sh through Git Bash on this machine, with
  HRM_HOME=artifacts/vps/local-home, project hrm-vps-local and docker-compose.vps-local.yml (Caddy "tls internal").
  The server's .env is never copied, read or printed by this script; the local env file is lab-only.
  Writes artifacts/vps/deploy-<target>-<tag>.json with the timings (pull, up, healthy).
  PowerShell 5.1:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/deploy.ps1 -Tag <sha12>
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/deploy.ps1 -Local -EnvFile deploy/vps/.env.vps-local -Tag <sha12>
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/deploy.ps1 -Rollback
#>
[CmdletBinding()]
param(
    [string]$Tag = '',
    [string]$SshHost = 'hrm-vps',
    [switch]$Local,
    [string]$EnvFile = '',
    [switch]$Rollback,
    [switch]$SkipSmoke,
    [switch]$AllowStagingCa,
    [string]$RemoteHome = '/opt/hrm'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
$artifacts = Join-Path $repoRoot 'artifacts\vps'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

function ConvertTo-ArgumentString([string[]]$Arguments) {
    ($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
}

# Runs a native command, echoes its output lines (stdout and stderr) as they come, and returns them.
function Invoke-Streamed([string]$FilePath, [string[]]$Arguments, [hashtable]$Environment = @{}) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ConvertTo-ArgumentString $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    $lines = New-Object System.Collections.Concurrent.ConcurrentQueue[string]
    $handler = { if ($null -ne $EventArgs.Data) { $Event.MessageData.Enqueue($EventArgs.Data) } }
    $o = Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived -Action $handler -MessageData $lines
    $e = Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived -Action $handler -MessageData $lines
    [void]$proc.Start()
    $proc.BeginOutputReadLine(); $proc.BeginErrorReadLine()
    $all = New-Object System.Collections.Generic.List[string]
    $line = $null
    while (-not $proc.HasExited) {
        while ($lines.TryDequeue([ref]$line)) { Write-Host $line; $all.Add($line) }
        Start-Sleep -Milliseconds 200
    }
    $proc.WaitForExit()
    Start-Sleep -Milliseconds 300
    while ($lines.TryDequeue([ref]$line)) { Write-Host $line; $all.Add($line) }
    Unregister-Event -SourceIdentifier $o.Name; Unregister-Event -SourceIdentifier $e.Name
    return @{ ExitCode = $proc.ExitCode; Lines = $all }
}

function Invoke-Captured([string]$FilePath, [string[]]$Arguments) {
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

function Get-GitBash {
    $exec = (Invoke-Captured 'git' @('--exec-path')).StdOut.Trim()
    # <Git>\mingw64\libexec\git-core -> <Git>\bin\bash.exe (never WSL's System32\bash.exe)
    $gitRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $exec))
    $bash = Join-Path $gitRoot 'bin\bash.exe'
    if (-not (Test-Path $bash)) { throw "Git for Windows bash not found at $bash" }
    return $bash
}

function Ssh([string]$Command, [switch]$Stream) {
    $sshArgs = @('-o', 'BatchMode=yes', $SshHost, $Command)
    if ($Stream) { return Invoke-Streamed 'ssh' $sshArgs }
    return Invoke-Captured 'ssh' $sshArgs
}

function Get-Timings($Lines) {
    $t = [ordered]@{}
    foreach ($l in $Lines) { if ($l -match '^timing (\w+)=(\d+)$') { $t[$Matches[1]] = [int]$Matches[2] } }
    return $t
}

function Save-Record([string]$Target, [string]$Action, [string]$ReleaseTag, $Timings, [double]$TotalSeconds) {
    $record = [ordered]@{
        target       = $Target
        action       = $Action
        tag          = $ReleaseTag
        atUtc        = (Get-Date).ToUniversalTime().ToString('o')
        totalSeconds = [Math]::Round($TotalSeconds, 1)
        timings      = $Timings
    }
    $path = Join-Path $artifacts ("deploy-{0}-{1}-{2}.json" -f $Target, $Action, $ReleaseTag)
    [System.IO.File]::WriteAllText($path, ($record | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "[deploy] record: $path"
}

function Read-EnvKey([string]$Path, [string]$Key) {
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ($line -match "^$Key=(.*)$") { return $Matches[1].Trim('"') }
    }
    return ''
}

$watch = [System.Diagnostics.Stopwatch]::StartNew()
if (-not $Rollback) {
    if (-not $Tag) { $Tag = (Invoke-Captured 'git' @('rev-parse', '--short=12', 'HEAD')).StdOut.Trim() }
    if ($Tag -notmatch '^[0-9a-f]{12}$') { throw "Tag '$Tag' is not a 12-character commit SHA (latest is refused)." }
}

if ($Local) {
    if (-not $EnvFile) { throw '-Local needs -EnvFile (deploy/vps/.env.vps-local).' }
    $EnvFile = (Resolve-Path $EnvFile).Path
    $localHome = Join-Path $artifacts 'local-home'
    New-Item -ItemType Directory -Force -Path (Join-Path $localHome 'releases'), (Join-Path $localHome 'shared') | Out-Null
    Copy-Item $EnvFile (Join-Path $localHome 'shared\.env') -Force
    $bash = Get-GitBash
    $envVars = @{ HRM_HOME = $localHome.Replace('\', '/'); HRM_PROJECT = 'hrm-vps-local'; HRM_EXTRA_COMPOSE_FILES = 'docker-compose.vps-local.yml' }

    if ($Rollback) {
        $prev = (Get-Content (Join-Path $localHome 'previous-tag') -ErrorAction Stop | Select-Object -First 1).Trim()
        $script = Join-Path $localHome "releases\$prev\deploy\vps\remote-deploy.sh"
        $run = Invoke-Streamed $bash @($script.Replace('\', '/'), 'rollback') $envVars
        if ($run.ExitCode -ne 0) { throw "local rollback failed (exit $($run.ExitCode))" }
        Save-Record 'local' 'rollback' $prev (Get-Timings $run.Lines) $watch.Elapsed.TotalSeconds
        $Tag = $prev
    }
    else {
        $appDomain = Read-EnvKey $EnvFile 'APP_DOMAIN'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\prepare-keycloak-import.ps1') -Profile VpsDemo -AppDomain $appDomain
        if ($LASTEXITCODE -ne 0) { throw 'prepare-keycloak-import.ps1 -Profile VpsDemo failed' }
        $bundle = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptDir 'make-deploy-bundle.ps1') -Tag $Tag | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0) { throw 'make-deploy-bundle.ps1 failed' }
        $release = Join-Path $localHome "releases\$Tag"
        if (Test-Path $release) { Remove-Item -Recurse -Force $release }
        $tar = Invoke-Captured 'tar.exe' @('-xzf', $bundle, '-C', (Join-Path $localHome 'releases'))
        if ($tar.ExitCode -ne 0) { throw "extract failed: $($tar.StdErr)" }
        Rename-Item (Join-Path $localHome "releases\hrm-deploy-$Tag") $release
        $run = Invoke-Streamed $bash @((Join-Path $release 'deploy\vps\remote-deploy.sh').Replace('\', '/'), 'deploy', $Tag) $envVars
        if ($run.ExitCode -ne 0) { throw "local deploy failed (exit $($run.ExitCode))" }
        Save-Record 'local' 'deploy' $Tag (Get-Timings $run.Lines) $watch.Elapsed.TotalSeconds
    }
    if (-not $SkipSmoke) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptDir 'smoke.ps1') -Local -EnvFile $EnvFile
        if ($LASTEXITCODE -ne 0) { throw 'smoke test failed' }
    }
    exit 0
}

# Remote.
$probe = Ssh "test -f $RemoteHome/shared/.env && echo env-present"
if ($probe.ExitCode -ne 0 -or $probe.StdOut.Trim() -ne 'env-present') {
    throw "ssh $SshHost failed or $RemoteHome/shared/.env is missing (generate it on the server first: docs/OPS_VPS_SINGLE_HOST.md)."
}

if ($Rollback) {
    $run = Ssh "bash $RemoteHome/current/deploy/vps/remote-deploy.sh rollback" -Stream
    if ($run.ExitCode -ne 0) { throw "rollback failed (exit $($run.ExitCode))" }
    $Tag = (Ssh "basename `$(readlink $RemoteHome/current)").StdOut.Trim()
    Save-Record $SshHost 'rollback' $Tag (Get-Timings $run.Lines) $watch.Elapsed.TotalSeconds
}
else {
    $appDomain = (Ssh "grep -E '^APP_DOMAIN=' $RemoteHome/shared/.env | tail -1 | cut -d= -f2-").StdOut.Trim().Trim('"')
    if ($appDomain -notmatch '^[a-z0-9.-]+$') { throw "APP_DOMAIN on the server is empty or invalid." }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\prepare-keycloak-import.ps1') -Profile VpsDemo -AppDomain $appDomain
    if ($LASTEXITCODE -ne 0) { throw 'prepare-keycloak-import.ps1 -Profile VpsDemo failed' }
    $bundle = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptDir 'make-deploy-bundle.ps1') -Tag $Tag | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { throw 'make-deploy-bundle.ps1 failed' }

    $scpWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $scp = Invoke-Captured 'scp' @('-q', '-o', 'BatchMode=yes', $bundle, "${SshHost}:$RemoteHome/releases/")
    if ($scp.ExitCode -ne 0) { throw "scp failed: $($scp.StdErr)" }
    $scpWatch.Stop()
    $name = Split-Path -Leaf $bundle
    $extract = Ssh ("set -e; cd $RemoteHome/releases; rm -rf $Tag hrm-deploy-$Tag; tar -xzf $name; mv hrm-deploy-$Tag $Tag; rm -f $name; " +
        "bash $Tag/deploy/vps/remote-deploy.sh check-env $RemoteHome/releases/$Tag/deploy/vps/.env.vps.example")
    Write-Host $extract.StdOut.Trim()
    if ($extract.ExitCode -ne 0) { throw "extract or env check failed: $($extract.StdErr.Trim())" }

    $run = Ssh "cd $RemoteHome/releases/$Tag && bash deploy/vps/remote-deploy.sh deploy $Tag" -Stream
    if ($run.ExitCode -ne 0) { throw "deploy failed (exit $($run.ExitCode)); roll back with -Rollback" }
    $timings = Get-Timings $run.Lines
    $timings['scp_seconds'] = [int][Math]::Round($scpWatch.Elapsed.TotalSeconds)
    Save-Record $SshHost 'deploy' $Tag $timings $watch.Elapsed.TotalSeconds
}

if (-not $SkipSmoke) {
    $smokeArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $scriptDir 'smoke.ps1'), '-SshHost', $SshHost)
    if ($AllowStagingCa) { $smokeArgs += '-AllowStagingCa' }
    & powershell @smokeArgs
    if ($LASTEXITCODE -ne 0) { throw 'smoke test failed' }
}
exit 0
