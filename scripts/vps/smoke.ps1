<#
.SYNOPSIS
  PLAN-36 (section 8): self-test of the single host from the owner's machine.

.DESCRIPTION
  Runs deploy/vps/smoke.sh where the stack runs (over ssh on the server, or through Git Bash for -Local, trusting only
  Caddy's exported internal root), then adds the checks only an outside client can make: 443 open and 80, 5000, 8080,
  5432, 5672, 15672, 5341, 5380 closed on the public address (an ISP may block outbound ports, so smoke.sh also checks
  the server's listening sockets). Writes one JSON file for EvidenceRecorder under artifacts/vps/ and exits 1 on any
  failure.
    -AllowStagingCa              accept only a Let's Encrypt staging certificate (first server run, P-6)
    -RequireIntegrationWiring    list the PLAN-37 wiring checks as Deferred(PLAN-37)
  PowerShell 5.1:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/smoke.ps1 [-SshHost hrm-vps] [-AllowStagingCa]
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/smoke.ps1 -Local -EnvFile deploy/vps/.env.vps-local
#>
[CmdletBinding()]
param(
    [string]$SshHost = 'hrm-vps',
    [switch]$Local,
    [string]$EnvFile = '',
    [string]$TargetIp = '',
    [switch]$AllowStagingCa,
    [switch]$RequireIntegrationWiring,
    [string]$RemoteHome = '/opt/hrm',
    [string]$OutFile = ''
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

function Invoke-Captured([string]$FilePath, [string[]]$Arguments, [hashtable]$Environment = @{}) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ConvertTo-ArgumentString $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    return @{ ExitCode = $proc.ExitCode; StdOut = $stdout; StdErr = $stderrTask.Result }
}

function Test-TcpPort([string]$Address, [int]$Port, [int]$TimeoutMs = 3000) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync($Address, $Port)
        if ($task.Wait($TimeoutMs)) { return $client.Connected }
        return $false
    }
    catch { return $false }
    finally { $client.Close() }
}

$smokeFlags = @()
if ($AllowStagingCa) { $smokeFlags += '--allow-staging-ca' }
if ($RequireIntegrationWiring) { $smokeFlags += '--require-integration-wiring' }

if ($Local) {
    if (-not $EnvFile) { throw '-Local needs -EnvFile.' }
    $localHome = Join-Path $artifacts 'local-home'
    $currentTag = (Get-Content (Join-Path $localHome 'current-tag') -ErrorAction Stop | Select-Object -First 1).Trim()
    $current = Get-Item (Join-Path $localHome "releases\$currentTag")
    $edge = (Invoke-Captured 'docker' @('ps', '-q', '--filter', 'label=com.docker.compose.project=hrm-vps-local', '--filter', 'label=com.docker.compose.service=edge')).StdOut.Trim()
    if (-not $edge) { throw 'local edge container not found (project hrm-vps-local)' }
    $rootCa = Join-Path $artifacts 'local-caddy-root.crt'
    $ca = Invoke-Captured 'docker' @('exec', $edge, 'cat', '/data/caddy/pki/authorities/local/root.crt')
    if ($ca.ExitCode -ne 0) { throw "could not export Caddy's internal root: $($ca.StdErr)" }
    [System.IO.File]::WriteAllText($rootCa, $ca.StdOut, (New-Object System.Text.UTF8Encoding($false)))
    $exec = (Invoke-Captured 'git' @('--exec-path')).StdOut.Trim()
    $bash = Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $exec))) 'bin\bash.exe'
    $smokeScript = (Join-Path $current.FullName 'deploy\vps\smoke.sh').Replace('\', '/')
    $run = Invoke-Captured $bash (@($smokeScript, '--cacert', $rootCa.Replace('\', '/')) + $smokeFlags) @{ HRM_HOME = $localHome.Replace('\', '/'); HRM_PROJECT = 'hrm-vps-local' }
    if (-not $TargetIp) { $TargetIp = '127.0.0.1' }
    $target = 'local'
}
else {
    $remoteCmd = "bash $RemoteHome/current/deploy/vps/smoke.sh $($smokeFlags -join ' ')"
    $run = Invoke-Captured 'ssh' @('-o', 'BatchMode=yes', $SshHost, $remoteCmd)
    if (-not $TargetIp) {
        $appDomain = (Invoke-Captured 'ssh' @('-o', 'BatchMode=yes', $SshHost, "grep -E '^APP_DOMAIN=' $RemoteHome/shared/.env | tail -1 | cut -d= -f2-")).StdOut.Trim()
        $TargetIp = ([System.Net.Dns]::GetHostAddresses($appDomain) | Where-Object { $_.AddressFamily -eq 'InterNetwork' } | Select-Object -First 1).IPAddressToString
    }
    $target = $SshHost
}

$run.StdErr -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { Write-Host $_ }
$jsonLine = ($run.StdOut -split "`n" | Where-Object { $_.Trim().StartsWith('{') } | Select-Object -Last 1)
if (-not $jsonLine) { throw "smoke.sh produced no JSON (exit $($run.ExitCode))" }
$report = $jsonLine | ConvertFrom-Json
$checks = New-Object System.Collections.Generic.List[object]
foreach ($c in $report.checks) { $checks.Add($c) }

# Outside view of the ports.
$open443 = Test-TcpPort $TargetIp 443
$checks.Add([pscustomobject]@{ id = 'external-443-open'; status = $(if ($open443) { 'Pass' } else { 'Fail' }); detail = "443 reachable: $open443" })
$closedPorts = @(80, 5000, 8080, 5432, 5672, 15672, 5341)
if (-not $Local) { $closedPorts += 5380 }
$openUnexpected = @($closedPorts | Where-Object { Test-TcpPort $TargetIp $_ 2000 })
$checks.Add([pscustomobject]@{
    id = 'external-ports-closed'
    status = $(if ($openUnexpected.Count -eq 0) { 'Pass' } else { 'Fail' })
    detail = $(if ($openUnexpected.Count -eq 0) { "closed: $($closedPorts -join ', ')" } else { "open: $($openUnexpected -join ', ')" })
})

$fails = @($checks | Where-Object { $_.status -eq 'Fail' }).Count
$masked = $TargetIp
if (-not $Local -and $TargetIp -match '^(\d+\.\d+)\.\d+\.\d+$') { $masked = "$($Matches[1]).x.x" }
$result = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    target         = $target
    targetIp       = $masked
    appDomain      = $report.appDomain
    ssoDomain      = $report.ssoDomain
    staging        = [bool]$AllowStagingCa
    summary        = [ordered]@{
        pass     = @($checks | Where-Object { $_.status -eq 'Pass' }).Count
        fail     = $fails
        warn     = @($checks | Where-Object { $_.status -eq 'Warn' }).Count
        deferred = @($checks | Where-Object { [string]$_.status -like 'Deferred*' }).Count
    }
    checks         = $checks
}
if (-not $OutFile) { $OutFile = Join-Path $artifacts ("smoke-{0}-{1}.json" -f $target, (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')) }
[System.IO.File]::WriteAllText($OutFile, ($result | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("[smoke] {0}: pass {1}, fail {2}, warn {3}, deferred {4} -> {5}" -f $target, $result.summary.pass, $fails, $result.summary.warn, $result.summary.deferred, $OutFile)
if ($fails -gt 0) { exit 1 }
exit 0
