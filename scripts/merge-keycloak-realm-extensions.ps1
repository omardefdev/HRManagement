# Merges realm-qa004-e2e.json and realm-acceptance.json users into docker/keycloak/import/realm-hrmanagement.json
#Uses Python json merge to preserve template structure (clientScopes, etc.).
#Requires -Version 5.1
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$importPath = Join-Path $RepoRoot "docker/keycloak/import/realm-hrmanagement.json"
if (-not (Test-Path $importPath)) {
    throw "Run prepare-keycloak-import.ps1 first. Missing $importPath"
}

$py = Join-Path $PSScriptRoot "merge-keycloak-realm-extensions.py"
# `python` on Windows / CI images; many Linux distributions ship only `python3`.
$python = @('python', 'python3') | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
if (-not $python) { throw "Python not found (tried python, python3). Install Python 3 to merge the realm extensions." }
& $python $py $RepoRoot
if ($LASTEXITCODE -ne 0) { throw "merge-keycloak-realm-extensions.py failed with exit code $LASTEXITCODE" }
