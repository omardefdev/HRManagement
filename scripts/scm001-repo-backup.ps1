# SCM-001: create a local git mirror backup of this repository.
param(
    [string]$RepoRoot = "",
    [string]$OutDir = "artifacts/scm-backups"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$destRoot = Join-Path $RepoRoot ($OutDir -replace '/', [IO.Path]::DirectorySeparatorChar)
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$mirror = Join-Path $destRoot "HRManagement-$stamp.git"

Write-Host "Creating mirror at $mirror ..."
git -C $RepoRoot clone --mirror $RepoRoot $mirror
if ($LASTEXITCODE -ne 0) { throw "git clone --mirror failed" }

# Lightweight integrity check
$heads = git -C $mirror show-ref --heads
if (-not $heads) { throw "mirror has no heads" }

Write-Host "SCM-001 backup OK: $mirror"
Write-Host "Next: encrypt and copy off-box; log restore drill in docs/scm-governance/restore-drill-log.md"
exit 0
