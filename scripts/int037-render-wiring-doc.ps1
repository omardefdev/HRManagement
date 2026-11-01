#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-37 (section 1): renders deploy/wiring/service-wiring.json to docs/SERVICE_WIRING_MATRIX.md.
.DESCRIPTION
  The JSON is the single source of truth; the Markdown is generated and must not be edited by hand.
  -Check compares the rendered text with the committed file and exits 1 when they differ (used by the unit tests).
  PowerShell 5.1: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/int037-render-wiring-doc.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [string]$OutFile = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')).Path }
if (-not $OutFile) { $OutFile = Join-Path $RepoRoot 'docs\SERVICE_WIRING_MATRIX.md' }

$matrixPath = Join-Path $RepoRoot 'deploy\wiring\service-wiring.json'
$matrix = [System.IO.File]::ReadAllText($matrixPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json

function Get-Names($obj) { if ($null -eq $obj) { return @() } return @($obj.PSObject.Properties | ForEach-Object { $_.Name }) }
function Join-Code([object[]]$items) {
    $list = @($items | Where-Object { $_ })
    if ($list.Count -eq 0) { return '-' }
    return (($list | ForEach-Object { '`' + $_ + '`' }) -join ', ')
}
function Escape-Cell([string]$text) { if (-not $text) { return '' } return $text.Replace('|', '\|') }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Service wiring matrix (generated)')
$lines.Add('')
$lines.Add('> Generated from `deploy/wiring/service-wiring.json` by `scripts/int037-render-wiring-doc.ps1`. Do not edit by hand:')
$lines.Add('> change the JSON and re-run the script. `ProductionWiringMatrixTests` fails when this file is stale.')
$lines.Add('')
$lines.Add('## Contract for later plans (PLAN-37, P-8)')
$lines.Add('')
$lines.Add('Any plan that adds a service-to-service client, call or scope, changes a server policy for an endpoint that a')
$lines.Add('service calls, or removes a registration updates `deploy/wiring/service-wiring.json` **in the same commit**.')
$lines.Add('`ProductionWiringMatrixTests` enforces it: code -> matrix (every registration and URL key), matrix -> server')
$lines.Add('policies (every call passes with its scope and fails without it; allow-lists admit only matrix callers), matrix ->')
$lines.Add('deployment (both production compose chains and k8s), prod-like parity, and the Keycloak scripts.')
$lines.Add('`scripts/int037-wiring-check.ps1` repeats the deployment check on the rendered `docker compose config` and')
$lines.Add('`kubectl kustomize` output. Known follow-ups: PLAN-42 (`dsar.subject.erase`, new `api/internal/*` policies, a')
$lines.Add('secret per service, an authenticated legal-hold gate), PLAN-46 (approval start from IDCard and Compliance),')
$lines.Add('PLAN-49 (removing unused policy-enforce registrations), PLAN-50 (the internal delegation endpoint), PLAN-39')
$lines.Add('(the production realm generated from this matrix).')
$lines.Add('')
$lines.Add('## Services')
$lines.Add('')
$lines.Add('| Service | Keycloak client | Secret variable | URLs | Scopes | Legal packs |')
$lines.Add('|---|---|---|---|---|---|')
foreach ($s in $matrix.services) {
    $urls = @(Get-Names $s.urls | ForEach-Object { $_ + '=' + $s.urls.$_ })
    $client = if ($s.serviceIdentity) { '`' + $s.clientId + '`' } elseif ($s.clientId) { '(no service identity; OIDC `' + $s.clientId + '`)' } else { '(none)' }
    $secret = if ($s.serviceIdentity) { '`' + $s.secretEnv + '`' } else { '-' }
    $lines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} |' -f $s.composeService, $client, $secret, (Join-Code $urls), (Join-Code @($s.scopes)), (Join-Code @($s.legalPacks))))
}
$lines.Add('')
$lines.Add('## Calls')
$lines.Add('')
$lines.Add('| Caller | Registration | Endpoint | Target | Server policies | Scope | Note |')
$lines.Add('|---|---|---|---|---|---|---|')
foreach ($s in $matrix.services) {
    foreach ($c in @($s.calls)) {
        $e = $matrix.endpoints.($c.endpoint)
        $target = $e.target
        if ($e.PSObject.Properties['via'] -and $e.via) { $target = $e.target + ' (via ' + $e.via + ')' }
        $note = ''
        if ($c.PSObject.Properties['authenticated'] -and $c.authenticated -eq $false) { $note = 'no service token yet (' + $c.owner + ')' }
        $lines.Add(('| `{0}` | `{1}` | `{2} {3}` | {4} | {5} | `{6}` | {7} |' -f $s.composeService, $c.client, $e.method, $e.path, $target, (Join-Code @($e.serverPolicies)), $c.scope, (Escape-Cell $note)))
    }
}
$lines.Add('')
$lines.Add('## Registrations')
$lines.Add('')
$lines.Add('| Registration | Defined in | URL keys | Registered by |')
$lines.Add('|---|---|---|---|')
foreach ($name in (Get-Names $matrix.registrations)) {
    $r = $matrix.registrations.$name
    $users = @($matrix.services | Where-Object { @($_.registrations) -contains $name } | ForEach-Object { $_.composeService })
    $lines.Add(('| `{0}` | `{1}` | {2} | {3} |' -f $name, $r.definedIn, (Join-Code @($r.urlKeys)), (Join-Code $users)))
}
$lines.Add('')
$lines.Add('Registered but never injected (they keep the URL, get no scope for it; removal is PLAN-49):')
$lines.Add('')
foreach ($s in $matrix.services) {
    if ($s.PSObject.Properties['unusedRegistrations'] -and $s.unusedRegistrations) {
        foreach ($name in (Get-Names $s.unusedRegistrations)) {
            $lines.Add(('- `{0}`: `{1}` - {2}' -f $s.composeService, $name, $s.unusedRegistrations.$name))
        }
    }
}
$lines.Add('')
$lines.Add('## Scopes')
$lines.Add('')
$lines.Add('| Scope | Audiences | Granted to | Description |')
$lines.Add('|---|---|---|---|')
foreach ($name in (Get-Names $matrix.scopes)) {
    $sc = $matrix.scopes.$name
    $granted = @($matrix.services | Where-Object { @($_.scopes) -contains $name } | ForEach-Object { $_.clientId })
    $lines.Add(('| `{0}` | {1} | {2} | {3} |' -f $name, (Join-Code @($sc.audiences)), (Join-Code $granted), (Escape-Cell $sc.description)))
}
$lines.Add('')
$lines.Add('## Allow-list entries without a call (not widened; removal is PLAN-42)')
$lines.Add('')
foreach ($x in @($matrix.allowListExceptions)) {
    $lines.Add(('- `{0}`: {1} - {2}' -f $x.policy, (Join-Code @($x.clients)), $x.reason))
}
$lines.Add('')
$lines.Add('## Legal packs')
$lines.Add('')
foreach ($name in (Get-Names $matrix.legalPacks)) {
    $p = $matrix.legalPacks.$name
    $readers = @($matrix.services | Where-Object { @($_.legalPacks) -contains $name } | ForEach-Object { $_.composeService })
    $lines.Add(('- `{0}`: source `{1}`, shipped as `{2}` in {3}' -f $name, $p.source, $p.publishPath, (Join-Code $readers)))
}

$text = ($lines -join "`n") + "`n"
if ($Check) {
    $current = if (Test-Path $OutFile) { [System.IO.File]::ReadAllText($OutFile, [System.Text.Encoding]::UTF8).Replace("`r`n", "`n") } else { '' }
    if ($current -ne $text) {
        Write-Host "[FAIL] $OutFile is stale: run scripts/int037-render-wiring-doc.ps1"
        exit 1
    }
    Write-Host "[PASS] $OutFile is up to date"
    exit 0
}
[System.IO.File]::WriteAllText($OutFile, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $OutFile"
