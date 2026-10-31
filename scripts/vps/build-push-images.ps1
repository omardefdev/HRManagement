<#
.SYNOPSIS
  PLAN-36 (section 6, D-OPS-1): builds the 15 service images for linux/amd64 from the committed HEAD and pushes them
  to a registry with the 12-character commit SHA as the only tag.

.DESCRIPTION
  - Build context: a detached `git worktree` of HEAD under artifacts/vps/build-src/<tag>, so neither uncommitted edits nor
    untracked files can reach an image. A dirty working tree is still refused (unless -AllowDirty) so that the tag
    never pretends to contain local changes; global.json is ignored in that check because the owner keeps a local
    SDK roll-forward edit there that never enters the build context.
  - docker/service-build-matrix.json lists the services; scripts/docker-generate-build-manifest.ps1 prepares the
    restore layer in the build context.
  - Images are built with `docker buildx bake` (linux/amd64) in batches, then pushed one by one; every push is timed.
  - Writes artifacts/vps/images-<tag>.json: registry, tag, per image the digest, the compressed size in the registry
    and the push time, plus totals. The tag `latest` is refused.
  PowerShell 5.1:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/build-push-images.ps1 -Registry localhost:5001/hrmanagement
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Registry,
    [string]$Tag = '',
    [switch]$AllowDirty,
    [int]$BatchSize = 4,
    [string[]]$Services = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
$IgnoredLocalChanges = @('global.json')

function ConvertTo-ArgumentString([string[]]$Arguments) {
    ($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments, [switch]$Stream) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ConvertTo-ArgumentString $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    return @{ ExitCode = $proc.ExitCode; StdOut = $stdout; StdErr = $stderrTask.Result }
}

function Assert-Native($Result, [string]$What, [string]$LogPath = '') {
    if ($LogPath) {
        [System.IO.File]::AppendAllText($LogPath, "### $What`n$($Result.StdOut)`n$($Result.StdErr)`n", (New-Object System.Text.UTF8Encoding($false)))
    }
    if ($Result.ExitCode -ne 0) {
        $tail = (($Result.StdErr + "`n" + $Result.StdOut) -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 15) -join "`n"
        throw "$What failed (exit $($Result.ExitCode)):`n$tail"
    }
}

Push-Location $repoRoot
try {
    $head = (Invoke-Native 'git' @('rev-parse', '--short=12', 'HEAD')).StdOut.Trim()
    if (-not $Tag) { $Tag = $head }
    if ($Tag -eq 'latest' -or $Tag -match '(?i)^latest$') { throw 'The tag latest is refused (PLAN-36 section 6 rule 2).' }
    if ($Tag -notmatch '^[0-9a-f]{12}$') { throw "Tag '$Tag' is not a 12-character commit SHA." }
    if ($Tag -ne $head) { throw "Tag $Tag is not HEAD ($head); images are built from HEAD only." }
    if ($Registry -match ':latest$' -or $Registry.EndsWith('/')) { throw "Registry '$Registry' must be a repository path such as ghcr.io/owner/hrmanagement." }
    if ($Registry -cne $Registry.ToLowerInvariant()) { throw "Registry '$Registry' must be lowercase (GHCR and Docker refuse uppercase repository names)." }

    $dirty = (Invoke-Native 'git' @('status', '--porcelain', '--untracked-files=no')).StdOut -split "`n" |
        Where-Object { $_.Trim() } |
        Where-Object { $IgnoredLocalChanges -notcontains ($_.Substring(3).Trim()) }
    if ($dirty -and -not $AllowDirty) {
        throw "The working tree has uncommitted changes (use -AllowDirty to build HEAD anyway):`n$($dirty -join "`n")"
    }

    $artifacts = Join-Path $repoRoot 'artifacts\vps'
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    $log = Join-Path $artifacts "build-push-$Tag.log"
    [System.IO.File]::WriteAllText($log, "build-push $Tag $(Get-Date -Format o)`n", (New-Object System.Text.UTF8Encoding($false)))

    # Clean build context from HEAD: a detached worktree (git archive + Windows tar.exe cannot extract the repository's
    # non-ASCII file names). .dockerignore excludes the worktree's .git file.
    $src = Join-Path $artifacts "build-src\$Tag"
    if (Test-Path $src) {
        [void](Invoke-Native 'git' @('worktree', 'remove', '--force', $src))
        if (Test-Path $src) { Remove-Item -Recurse -Force $src }
    }
    [void](Invoke-Native 'git' @('worktree', 'prune'))
    Assert-Native (Invoke-Native 'git' @('worktree', 'add', '--detach', $src, $Tag)) 'git worktree add (clean build context)' $log
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\docker-generate-build-manifest.ps1') -RepoRoot $src | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker-generate-build-manifest.ps1 failed' }

    $matrix = [System.IO.File]::ReadAllText((Join-Path $src 'docker\service-build-matrix.json')) | ConvertFrom-Json
    if ($Services.Count -gt 0) { $matrix = @($matrix | Where-Object { $Services -contains $_.service }) }
    if (@($matrix).Count -eq 0) { throw 'No service selected.' }

    # Build in batches (parallel inside a batch) with buildx bake.
    $buildWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $batches = @()
    for ($i = 0; $i -lt @($matrix).Count; $i += $BatchSize) { $batches += , @($matrix[$i..([Math]::Min($i + $BatchSize, @($matrix).Count) - 1)]) }
    $n = 0
    foreach ($batch in $batches) {
        $n++
        $targets = [ordered]@{}
        foreach ($m in $batch) {
            $targets[$m.service] = [ordered]@{
                context    = $src.Replace('\', '/')
                dockerfile = 'docker/DotnetService.Dockerfile'
                platforms  = @('linux/amd64')
                tags       = @("$Registry/$($m.service):$Tag")
                args       = [ordered]@{
                    PROJECT_CSPROJ    = $m.projectCsproj
                    ASSEMBLY_NAME     = $m.assemblyName
                    NEEDS_KRB5        = $m.needsKrb5
                    GATEWAY_AUDIT_DIR = $m.gatewayAuditDir
                }
                labels     = [ordered]@{ 'org.opencontainers.image.revision' = $Tag; 'org.opencontainers.image.source' = 'hrmanagement' }
            }
        }
        $bake = [ordered]@{ group = [ordered]@{ default = [ordered]@{ targets = @($targets.Keys) } }; target = $targets }
        $bakeFile = Join-Path $artifacts "bake-$Tag-$n.json"
        [System.IO.File]::WriteAllText($bakeFile, ($bake | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "[build] batch $n/$($batches.Count): $((@($targets.Keys)) -join ', ')"
        Assert-Native (Invoke-Native 'docker' @('buildx', 'bake', '--progress=plain', '-f', $bakeFile, '--load')) "buildx bake batch $n" $log
    }
    $buildWatch.Stop()
    Write-Host ("[build] {0} images in {1:N0} s" -f @($matrix).Count, $buildWatch.Elapsed.TotalSeconds)

    # Push one by one, timed; read digest and compressed size from the registry manifest.
    $images = @()
    $pushWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $layers = @{}
    foreach ($m in $matrix) {
        $ref = "$Registry/$($m.service):$Tag"
        $w = [System.Diagnostics.Stopwatch]::StartNew()
        Assert-Native (Invoke-Native 'docker' @('push', $ref)) "push $ref" $log
        $w.Stop()
        $raw = Invoke-Native 'docker' @('buildx', 'imagetools', 'inspect', '--raw', $ref)
        Assert-Native $raw "inspect $ref" ''
        $manifest = $raw.StdOut | ConvertFrom-Json
        if ($manifest.PSObject.Properties['manifests']) {
            $amd64 = @($manifest.manifests | Where-Object { $_.platform -and $_.platform.architecture -eq 'amd64' })[0]
            $raw = Invoke-Native 'docker' @('buildx', 'imagetools', 'inspect', '--raw', "$Registry/$($m.service)@$($amd64.digest)")
            Assert-Native $raw "inspect $ref (amd64)" ''
            $manifest = $raw.StdOut | ConvertFrom-Json
        }
        $size = [int64]$manifest.config.size
        foreach ($l in $manifest.layers) { $size += [int64]$l.size; $layers[$l.digest] = [int64]$l.size }
        $inspect = Invoke-Native 'docker' @('buildx', 'imagetools', 'inspect', $ref, '--format', '{{json .Manifest}}')
        Assert-Native $inspect "digest $ref" ''
        $digest = ($inspect.StdOut | ConvertFrom-Json).digest
        $images += [ordered]@{
            service           = $m.service
            image             = $ref
            digest            = $digest
            compressedBytes   = $size
            pushSeconds       = [Math]::Round($w.Elapsed.TotalSeconds, 1)
        }
        Write-Host ("[push] {0,-22} {1,8:N1} MB {2,6:N1} s {3}" -f $m.service, ($size / 1MB), $w.Elapsed.TotalSeconds, $digest)
    }
    $pushWatch.Stop()
    $unique = 0; foreach ($v in $layers.Values) { $unique += $v }
    $report = [ordered]@{
        registry                  = $Registry
        tag                       = $Tag
        builtAtUtc                = (Get-Date).ToUniversalTime().ToString('o')
        buildSeconds              = [Math]::Round($buildWatch.Elapsed.TotalSeconds, 1)
        pushSeconds               = [Math]::Round($pushWatch.Elapsed.TotalSeconds, 1)
        totalCompressedBytes      = ($images | ForEach-Object { $_.compressedBytes } | Measure-Object -Sum).Sum
        uniqueLayerBytes          = $unique
        images                    = $images
    }
    $out = Join-Path $artifacts "images-$Tag.json"
    [System.IO.File]::WriteAllText($out, ($report | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("[done] build {0:N0} s, push {1:N0} s, {2:N1} MB in the registry ({3:N1} MB of unique layers) -> {4}" -f `
        $report.buildSeconds, $report.pushSeconds, ($report.totalCompressedBytes / 1MB), ($unique / 1MB), $out)
    [void](Invoke-Native 'git' @('worktree', 'remove', '--force', $src))
}
finally {
    Pop-Location
}
