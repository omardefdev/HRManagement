# QA-034: run the CI build-and-test steps (scripts/ci/steps.json) in a local Linux container, the same way
# .github/workflows/ci.yml runs them on ubuntu-latest.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1                 # all steps, clean HEAD
#   pwsh scripts/ci-local.ps1 -ContinueOnError -Out artifacts/ci-local/baseline
#   pwsh scripts/ci-local.ps1 -Step "Unit tests"
#   pwsh scripts/ci-local.ps1 -SkipTestcontainers      # the step is reported "not-run", never silently dropped
#   pwsh scripts/ci-local.ps1 -Source WorkingTree      # tracked + untracked (not ignored) files of the working tree
#
# -Source Head (default): the container clones the committed HEAD of this repository and runs there, so the result
# is evidence for HEAD exactly; the local global.json edit and the Windows bin/obj folders are never used.
# summary.json (in -Out) records gitSha, dirtyTracked, knownLocalOverrides (and the host working tree under "host").
# Mounts: the repository (read for the clone, written only for -Out), the Docker socket (Testcontainers, compose
# build), a named volume for the NuGet cache. Works on Windows PowerShell 5.1 and PowerShell 7. ASCII only.

param(
    [string]$Step = "",
    [switch]$ContinueOnError,
    [string]$Out = "artifacts/ci-local",
    [switch]$SkipTestcontainers,
    [string[]]$Skip = @(),
    [ValidateSet("Head", "WorkingTree")]
    [string]$Source = "Head",
    [switch]$RebuildImage,
    [string]$Image = ""
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dockerfile = Join-Path $repo "docker/ci-local/Dockerfile"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "docker CLI not found" }
# Native commands write progress to stderr; Windows PowerShell 5.1 would turn that into terminating errors under
# "Stop". From here on every native call is checked through $LASTEXITCODE instead.
$ErrorActionPreference = "Continue"

# Image tag follows the Dockerfile content, so a Dockerfile change rebuilds the image.
if (-not $Image) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.IO.File]::ReadAllBytes($dockerfile)
    $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').Substring(0, 12).ToLowerInvariant()
    $Image = "hrm-ci-local:$hash"
}
$haveImage = $false
& docker image inspect $Image *> $null
if ($LASTEXITCODE -eq 0) { $haveImage = $true }
if ($RebuildImage -or -not $haveImage) {
    Write-Host "Building $Image from docker/ci-local/Dockerfile"
    & docker build -t $Image -f $dockerfile (Join-Path $repo "docker/ci-local")
    if ($LASTEXITCODE -ne 0) { throw "docker build of $Image failed" }
}

function ConvertTo-ShellQuoted([string]$value) { return ("'" + ($value -replace "'", "'\''") + "'") }

$runArgs = @()
if ($Step) { $runArgs += "--step"; $runArgs += (ConvertTo-ShellQuoted $Step) } else { $runArgs += "--all" }
if ($ContinueOnError) { $runArgs += "--continue-on-error" }
$skips = @($Skip)
if ($SkipTestcontainers) { $skips += "Integration tests (Testcontainers)" }
foreach ($s in $skips) { if ($s) { $runArgs += "--skip"; $runArgs += (ConvertTo-ShellQuoted $s) } }
$outRel = ($Out -replace '\\', '/').TrimEnd('/')
# run-steps.sh arguments, shell-quoted; passed by environment because Windows PowerShell 5.1 mangles embedded
# double quotes in native-command arguments.
$argLine = $runArgs -join " "
Write-Host "ci-local: run-steps.sh $argLine"

$sourceMode = "head-clone"
if ($Source -eq "WorkingTree") { $sourceMode = "working-tree" }

$dockerArgs = @(
    "run", "--rm",
    "-v", "${repo}:/src",
    "-v", "/var/run/docker.sock:/var/run/docker.sock",
    "-v", "hrm-ci-local-nuget:/root/.nuget/packages",
    # Testcontainers bind-mounts files the tests write to the temp folder (e.g. the Keycloak realm of
    # Qa003KeycloakTokenMatrixTests). The daemon resolves bind sources on the Docker host, so the temp folder is a
    # host directory mounted at the same path inside this container.
    "-v", "/tmp/hrm-ci-local:/tmp/hrm-ci-local",
    "-e", "TMPDIR=/tmp/hrm-ci-local",
    "--add-host", "host.docker.internal:host-gateway",
    "-e", "CI=true",
    "-e", "CI_LOCAL_SOURCE=$sourceMode",
    "-e", "CI_SOURCE_DIR=/src",
    "-e", "CI_LOCAL_OUT=$outRel",
    "-e", "CI_LOCAL_ARGS=$argLine",
    "-e", "TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal",
    "-e", "TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock",
    "-w", "/work",
    $Image,
    "bash", "/src/scripts/ci/ci-local-entry.sh"
)

Write-Host "ci-local: image=$Image source=$sourceMode out=$outRel"
$started = Get-Date
& docker @dockerArgs
$code = $LASTEXITCODE
$elapsed = [int]((Get-Date) - $started).TotalSeconds

$summary = Join-Path $repo ($outRel -replace '/', [IO.Path]::DirectorySeparatorChar)
$summary = Join-Path $summary "summary.json"
if (Test-Path $summary) {
    $s = Get-Content -Raw -Path $summary | ConvertFrom-Json
    Write-Host ("ci-local: ok={0} complete={1} passed={2} failed={3} skipped={4} not-run={5} sha={6} in {7}s" -f `
        $s.ok, $s.complete, $s.counts.passed, $s.counts.failed, $s.counts.skipped, $s.counts.'not-run', $s.gitSha, $elapsed)
    foreach ($st in @($s.steps)) {
        if ($st.status -ne "passed") { Write-Host ("  {0,-8} {1}" -f $st.status.ToUpperInvariant(), $st.name) }
    }
    if ($s.host -and @($s.host.dirtyTracked).Count -gt 0) {
        Write-Host ("ci-local: host working tree has tracked changes not under test: {0}" -f (@($s.host.dirtyTracked) -join ", "))
    }
}
else {
    Write-Host "ci-local: no summary.json written (exit $code)"
}
exit $code
