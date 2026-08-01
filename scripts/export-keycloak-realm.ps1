# Export Keycloak realm (without users) for reproducible deploy.
# Usage: .\scripts\export-keycloak-realm.ps1 [-Container hrm-keycloak] [-Realm hrmanagement]
param(
    [string]$Container = "hrm-keycloak",
    [string]$Realm = "hrmanagement"
)

$ErrorActionPreference = "Stop"
$Output = Join-Path $PSScriptRoot "..\docker\keycloak\realm-$Realm-export.json"
$OutputDir = Split-Path $Output -Parent
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "Exporting realm '$Realm' from container '$Container'..."
docker exec $Container mkdir -p /tmp/export
docker exec $Container /opt/keycloak/bin/kc.sh export `
  --dir /tmp/export `
  --realm $Realm `
  --users skip

$candidates = @(
    "/tmp/export/${Realm}-realm.json",
    "/tmp/export/${Realm}.json"
)

$found = $null
foreach ($c in $candidates) {
    docker exec $Container test -f $c 2>$null
    if ($LASTEXITCODE -eq 0) {
        $found = $c
        break
    }
}

if (-not $found) {
    Write-Host "Export finished but realm JSON not found under /tmp/export. Listing:"
    docker exec $Container ls -la /tmp/export
    exit 1
}

docker cp "${Container}:${found}" $Output
Write-Host "Exported to $Output"
Write-Host "IMPORTANT (IAM-004): scrub client secrets before committing."
Write-Host "Prefer the sanitized template docker/keycloak/realm-hr-services.json (placeholders only)."
Write-Host "See docs/SERVICE_TRUST_INVENTORY.md and docs/KEYCLOAK_AND_SECRETS.md."
