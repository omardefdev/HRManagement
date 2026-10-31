<#
.SYNOPSIS
  PLAN-36 owner decision (c): builds the release images on the server instead of uploading them (a documented
  deviation from plan section 6 rule 1, for a ~0.16 MB/s upload link).

.DESCRIPTION
  1. git archive of the committed tag (default HEAD), without what .dockerignore keeps out of every image build anyway
     (docs/**/*.md, docs/output, docs/thesis_diagrams) and without acceptance-evidence, reports and artifacts (~31 MB);
  2. scp of the archive and of deploy/vps/build-on-host.sh as committed in the tag;
  3. ssh: build-on-host.sh builds the 15 images, pushes them to the server's loopback registry and removes the source,
     the build cache and the SDK image.
  Writes artifacts/vps/build-on-host-<tag>.json with the upload and build timings.
  PowerShell 5.1:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/build-on-host.ps1 [-Tag <sha12>] [-SshHost hrm-vps]
#>
[CmdletBinding()]
param(
    [string]$Tag = '',
    [string]$SshHost = 'hrm-vps',
    [string]$Registry = '127.0.0.1:5050/hrmanagement',
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$repoRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
$artifacts = Join-Path $repoRoot 'artifacts\vps'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

function Invoke-Git([string[]]$Arguments) {
    $out = & git -C $repoRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed" }
    return $out
}

$head = (Invoke-Git @('rev-parse', '--short=12', 'HEAD')).Trim()
if (-not $Tag) { $Tag = $head }
if ($Tag -notmatch '^[0-9a-f]{12}$') { throw "Tag '$Tag' is not a 12-character commit SHA (latest is refused)." }
$dirty = @(Invoke-Git @('status', '--porcelain', '--untracked-files=no') | Where-Object { $_ -and $_.Substring(3).Trim() -ne 'global.json' })
if ($Tag -eq $head -and $dirty.Count -gt 0 -and -not $AllowDirty) {
    throw "The working tree has uncommitted changes; the tag would not contain them:`n$($dirty -join "`n")"
}

$archive = Join-Path $artifacts "src-$Tag.tar.gz"
$script = Join-Path $artifacts "build-on-host-$Tag.sh"
Invoke-Git @('archive', '--format=tar.gz', '-o', $archive, $Tag, '--', '.', ':(exclude)docs/thesis_diagrams', ':(exclude)docs/output',
    ':(glob,exclude)docs/**/*.md', ':(exclude)acceptance-evidence', ':(exclude)reports', ':(exclude)artifacts') | Out-Null
$committedScript = Invoke-Git @('show', "${Tag}:deploy/vps/build-on-host.sh")
[System.IO.File]::WriteAllText($script, (($committedScript -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
$size = (Get-Item $archive).Length
Write-Host ("[upload] {0:N1} MB source archive of {1}" -f ($size / 1MB), $Tag)

$watch = [System.Diagnostics.Stopwatch]::StartNew()
foreach ($attempt in 1..3) {
    & scp -q -o BatchMode=yes -o ServerAliveInterval=15 $archive $script "${SshHost}:/opt/hrm/"
    if ($LASTEXITCODE -eq 0) { break }
    if ($attempt -eq 3) { throw 'scp failed three times' }
    Write-Host "[upload] attempt $attempt failed, retrying"
}
$watch.Stop()
$uploadSeconds = [Math]::Round($watch.Elapsed.TotalSeconds)
Write-Host "[upload] done in $uploadSeconds s"

$remote = "bash /opt/hrm/build-on-host-$Tag.sh $Tag /opt/hrm/src-$Tag.tar.gz $Registry; rc=`$?; rm -f /opt/hrm/build-on-host-$Tag.sh; exit `$rc"
$output = & ssh -o BatchMode=yes -o ServerAliveInterval=15 $SshHost $remote 2>&1
$rc = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($rc -ne 0) { throw "build on $SshHost failed (exit $rc)" }

$timings = [ordered]@{ upload_seconds = $uploadSeconds; archive_bytes = $size }
foreach ($line in $output) { if ("$line" -match '^timing (\w+)=(\d+)$') { $timings[$Matches[1]] = [int]$Matches[2] } }
$record = [ordered]@{ tag = $Tag; host = $SshHost; registry = $Registry; atUtc = (Get-Date).ToUniversalTime().ToString('o'); timings = $timings }
[System.IO.File]::WriteAllText((Join-Path $artifacts "build-on-host-$Tag.json"), ($record | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
Remove-Item $archive, $script -ErrorAction SilentlyContinue
Write-Host "[done] images of $Tag built on $SshHost and pushed to $Registry"
