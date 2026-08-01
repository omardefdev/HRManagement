# NFR-002: document/execute secret rotation drill checklist against SERVICE_SECRET_ROTATION.md
param(
    [string]$ResultsRoot = 'chaos/results',
    [switch]$DryRun
)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $ResultsRoot "$stamp-secret-rotation"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$steps = @(
    'Identify one non-root secret (e.g. module client secret)',
    'Rotate in Keycloak / secret store',
    'Clear ServiceIdentity token cache / restart one consumer',
    'Verify other services stay healthy (health/ready)',
    'Confirm failed auth is explicit 401 — not silent success'
)

$notes = @"
# Secret rotation drill (NFR-002)
Follow docs/SERVICE_SECRET_ROTATION.md

Steps:
$(($steps | ForEach-Object { "- [ ] $_" }) -join "`n")

DryRun=$DryRun
"@
$notes | Set-Content (Join-Path $runDir 'notes.md') -Encoding UTF8
@{ status = $(if ($DryRun) { 'dry-run' } else { 'checklist-ready' }); steps = $steps } |
    ConvertTo-Json | Set-Content (Join-Path $runDir 'summary.json') -Encoding UTF8
Write-Host "Secret rotation drill → $runDir"
