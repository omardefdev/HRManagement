<#
.SYNOPSIS
    BLD-002: copies *.csproj and packages.lock.json into docker/build-manifest/ for cached restore layers.
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$manifestRoot = Join-Path $RepoRoot 'docker/build-manifest'

if (Test-Path $manifestRoot) {
    Remove-Item $manifestRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $manifestRoot -Force | Out-Null

$patterns = @('*.csproj', 'packages.lock.json', 'Directory.Build.props', 'Directory.Packages.props')
$files = Get-ChildItem -Path $RepoRoot -Recurse -File -Include $patterns -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules|TestResults)\\' }

foreach ($file in $files) {
    $relative = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
    $dest = Join-Path $manifestRoot $relative
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Copy-Item $file.FullName $dest -Force -ErrorAction Stop
}

$count = ($files | Measure-Object).Count
Write-Host "Generated docker/build-manifest with $count project/lock files" -ForegroundColor Green
