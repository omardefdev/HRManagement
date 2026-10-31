#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-37 (section 2): the rendered wiring check. Compares deploy/wiring/service-wiring.json with what the deployment
  really sets: `docker compose config --format json` of both production chains and of the prod-like acceptance stack,
  and `kubectl kustomize` of the k8s overlays (production-ha, staging-ha, dr-secondary) when kubectl is installed.
.DESCRIPTION
  The chain definitions and the lint-only .env come from scripts/vps/compose-chains.ps1 (shared with
  scripts/vps/lint-compose.ps1). Lint-only values only; the rendered output stays under -OutDir (git-ignored) and is
  never printed. Writes artifacts/int037/wiring-report.json and exits 1 on any gap.
  For every matrix service: each URL key has the matrix value, ServiceIdentity__ClientId is the matrix client,
  ServiceIdentity__Scope is exactly the matrix scopes, no other wiring key is set, and the secret comes from the
  matrix variable (production chains: the lint value of SVC_<NAME>_CLIENT_SECRET; prod-like: the lab secret). The
  prod-like wiring equals the production wiring, service by service (only the secret value may differ).
  PowerShell 5.1: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/int037-wiring-check.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [string]$OutDir = '',
    [switch]$SkipKubernetes
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')).Path }
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'artifacts\int037' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$renderDir = Join-Path $OutDir 'rendered'
New-Item -ItemType Directory -Force -Path $renderDir | Out-Null

. (Join-Path $RepoRoot 'scripts\vps\compose-chains.ps1')

$WiringKey = '^(Integration__\w+|Services__\w+__BaseUrl|HrCore__BaseUrl|HrGateway__InternalBaseUrl|ServiceIdentity__ClientId|ServiceIdentity__Scope)$'
$LabSecret = 'dev-svc-secret-local-only'
$matrix = [System.IO.File]::ReadAllText((Join-Path $RepoRoot 'deploy\wiring\service-wiring.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json

function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function Get-Expected($service) {
    $expected = [ordered]@{}
    foreach ($u in @($service.urls.PSObject.Properties)) { $expected[$u.Name] = [string]$u.Value }
    if ($service.serviceIdentity) {
        $expected['ServiceIdentity__ClientId'] = [string]$service.clientId
        $expected['ServiceIdentity__Scope'] = (@($service.scopes) -join ' ')
    }
    return $expected
}

function Test-ScopeEqual([string]$a, [string]$b) {
    $x = @($a -split ' ' | Where-Object { $_ } | Sort-Object)
    $y = @($b -split ' ' | Where-Object { $_ } | Sort-Object)
    if ($x.Count -ne $y.Count) { return $false }
    for ($i = 0; $i -lt $x.Count; $i++) { if ($x[$i] -ne $y[$i]) { return $false } }
    return $true
}

# Compares one service's environment (hashtable name -> value) with the matrix; returns the problems.
function Compare-Wiring([string]$Label, $Service, [hashtable]$Actual) {
    $problems = New-Object System.Collections.Generic.List[string]
    $expected = Get-Expected $Service
    foreach ($key in $expected.Keys) {
        if (-not $Actual.ContainsKey($key)) { $problems.Add("$Label lacks $key (expected $($expected[$key]))"); continue }
        $same = if ($key -eq 'ServiceIdentity__Scope') { Test-ScopeEqual $Actual[$key] $expected[$key] } else { [string]$Actual[$key] -ceq [string]$expected[$key] }
        if (-not $same) { $problems.Add("$Label $key='$($Actual[$key])', expected '$($expected[$key])'") }
    }
    foreach ($key in $Actual.Keys) {
        if ($key -match $WiringKey -and -not $expected.Contains($key)) { $problems.Add("$Label sets $key, which the matrix does not list") }
    }
    return ,$problems
}

function ConvertTo-EnvTable($envObject) {
    $table = @{}
    if ($null -ne $envObject) { foreach ($p in $envObject.PSObject.Properties) { $table[$p.Name] = [string]$p.Value } }
    return $table
}

$report = [ordered]@{ generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o'); matrix = 'deploy/wiring/service-wiring.json'; chains = @(); kubernetes = $null; ok = $true }
$envFile = Join-Path $renderDir 'lint.env'
New-LintEnvFile -Path $envFile
$lintValues = @{}
foreach ($line in [System.IO.File]::ReadAllLines($envFile)) { $i = $line.IndexOf('='); if ($i -gt 0) { $lintValues[$line.Substring(0, $i)] = $line.Substring($i + 1) } }

$OutDirForCompose = $OutDir
$OutDir = $renderDir   # Invoke-ComposeConfig writes <chain>.json under $OutDir
$wiringByChain = @{}
foreach ($chain in @(Get-HrmProductionChains) + @(Get-HrmProdLikeChain)) {
    $problems = New-Object System.Collections.Generic.List[string]
    $result = Invoke-ComposeConfig -Name $chain.Name -Files $chain.Files -EnvFile $envFile -ChainProfiles @('full-stack')
    if (-not $result.Ok) {
        $problems.Add($result.Error)
    }
    else {
        $wiringByChain[$chain.Name] = @{}
        foreach ($service in $matrix.services) {
            $svcConfig = Get-Prop $result.Config.services $service.composeService
            if ($null -eq $svcConfig) { $problems.Add("$($chain.Name): $($service.composeService) is not in the rendered chain"); continue }
            $actual = ConvertTo-EnvTable (Get-Prop $svcConfig 'environment')
            foreach ($p in (Compare-Wiring "$($chain.Name): $($service.composeService)" $service $actual)) { $problems.Add($p) }
            $wiring = @{}
            foreach ($k in $actual.Keys) { if ($k -match $WiringKey) { $wiring[$k] = $actual[$k] } }
            $wiringByChain[$chain.Name][$service.composeService] = $wiring
            if ($service.serviceIdentity) {
                $secret = $actual['ServiceIdentity__ClientSecret']
                $want = if ($chain.Name -eq 'prodlike') { $LabSecret } else { $lintValues[[string]$service.secretEnv] }
                if ($secret -cne $want) {
                    $source = if ($chain.Name -eq 'prodlike') { 'the lab secret' } else { "`${$($service.secretEnv)}" }
                    $problems.Add("$($chain.Name): $($service.composeService) ServiceIdentity__ClientSecret does not come from $source")
                }
            }
        }
    }
    $report.chains += [ordered]@{ name = $chain.Name; files = $chain.Files; ok = ($problems.Count -eq 0); problems = @($problems) }
}
$OutDir = $OutDirForCompose

# Prod-like parity (section 2 rule 4).
$parity = New-Object System.Collections.Generic.List[string]
if ($wiringByChain.ContainsKey('production') -and $wiringByChain.ContainsKey('prodlike')) {
    foreach ($service in $matrix.services) {
        $a = $wiringByChain['production'][$service.composeService]; $b = $wiringByChain['prodlike'][$service.composeService]
        if ($null -eq $a -or $null -eq $b) { continue }
        foreach ($k in (@($a.Keys) + @($b.Keys) | Sort-Object -Unique)) {
            if ([string]$a[$k] -cne [string]$b[$k]) { $parity.Add("prod-like: $($service.composeService) $k='$($b[$k])' but production has '$($a[$k])'") }
        }
    }
}
$report.chains += [ordered]@{ name = 'prodlike-parity'; files = @(); ok = ($parity.Count -eq 0); problems = @($parity) }

# Kubernetes (kubectl kustomize), parsed line by line: kustomize prints normalized YAML (sorted keys, two-space lists).
function Read-KustomizeOutput([string]$Text) {
    $deployments = @{}; $secretKeys = New-Object System.Collections.Generic.HashSet[string]; $configMaps = @{}
    foreach ($doc in ($Text -split '(?m)^---\s*$')) {
        $lines = $doc -split "`r?`n"
        $kind = ($lines | Where-Object { $_ -match '^kind: (\S+)' } | Select-Object -First 1)
        if (-not $kind) { continue }
        $kind = ($kind -replace '^kind: ', '').Trim()
        $nameLine = $null; $inMeta = $false
        foreach ($l in $lines) {
            if ($l -match '^metadata:') { $inMeta = $true; continue }
            if ($inMeta -and $l -match '^  name: (\S+)') { $nameLine = $Matches[1]; break }
            if ($inMeta -and $l -match '^\S') { $inMeta = $false }
        }
        if ($kind -eq 'Deployment') {
            $env = @{}
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^(\s+)- name: (\S+)\s*$') {
                    $indent = $Matches[1].Length; $name = $Matches[2]; $next = if ($i + 1 -lt $lines.Count) { $lines[$i + 1] } else { '' }
                    $pad = ' ' * ($indent + 2)
                    if ($next.StartsWith($pad + 'value: ')) { $env[$name] = $next.Substring(($pad + 'value: ').Length).Trim().Trim('"') }
                    elseif ($next.StartsWith($pad + 'value:')) { $env[$name] = '' }
                    elseif ($next.StartsWith($pad + 'valueFrom:')) {
                        $key = $null; $sname = $null; $refKind = 'configMapKeyRef'; $j = $i + 2
                        while ($j -lt $lines.Count -and $lines[$j].StartsWith($pad + '  ')) {
                            if ($lines[$j] -match '^\s+key: (\S+)') { $key = $Matches[1] }
                            if ($lines[$j] -match '^\s+name: (\S+)') { $sname = $Matches[1] }
                            if ($lines[$j] -match 'secretKeyRef:') { $refKind = 'secretKeyRef' }
                            $j++
                        }
                        $env[$name] = "${refKind}:$sname/$key"
                    }
                }
            }
            $deployments[$nameLine] = $env
        }
        elseif ($kind -eq 'ExternalSecret' -and $nameLine -eq 'hr-app-secrets') {
            foreach ($l in $lines) { if ($l -match '^\s+(- )?secretKey: (\S+)') { [void]$secretKeys.Add($Matches[2]) } }
        }
        elseif ($kind -eq 'Secret' -and $nameLine -eq 'hr-app-secrets') {
            $inData = $false
            foreach ($l in $lines) {
                if ($l -match '^(stringData|data):') { $inData = $true; continue }
                if ($inData -and $l -match '^  ([A-Za-z0-9_]+):') { [void]$secretKeys.Add($Matches[1]) } elseif ($inData -and $l -match '^\S') { $inData = $false }
            }
        }
        elseif ($kind -eq 'ConfigMap') {
            $keys = @(); $inData = $false
            foreach ($l in $lines) {
                if ($l -match '^data:') { $inData = $true; continue }
                if ($inData -and $l -match '^  ([A-Za-z0-9_.\-]+):') { $keys += $Matches[1] } elseif ($inData -and $l -match '^\S') { $inData = $false }
            }
            $configMaps[$nameLine] = $keys
        }
    }
    return @{ Deployments = $deployments; SecretKeys = $secretKeys; ConfigMaps = $configMaps }
}

$kubectl = Get-Command kubectl -ErrorAction SilentlyContinue
if ($SkipKubernetes -or -not $kubectl) {
    $reason = if ($SkipKubernetes) { 'skipped (-SkipKubernetes)' } else { 'skipped: no kubectl' }
    Write-Host "[WARN] k8s rendered check $reason; ProductionWiringMatrixTests reads the k8s files directly"
    $report.kubernetes = [ordered]@{ skipped = $true; reason = $reason; overlays = @() }
}
else {
    $overlays = @()
    foreach ($overlay in @('production-ha', 'staging-ha', 'dr-secondary')) {
        $problems = New-Object System.Collections.Generic.List[string]
        $run = Invoke-Native -FilePath $kubectl.Source -Arguments @('kustomize', (Join-Path $RepoRoot "deploy\k8s\overlays\$overlay"))
        [System.IO.File]::WriteAllText((Join-Path $renderDir "k8s-$overlay.yaml"), $run.StdOut, (New-Object System.Text.UTF8Encoding($false)))
        if ($run.ExitCode -ne 0) { $problems.Add("$overlay`: kubectl kustomize failed: $($run.StdErr)") }
        else {
            $parsed = Read-KustomizeOutput $run.StdOut
            foreach ($key in @($parsed.ConfigMaps['hr-app-config'])) {
                if ($key -and ($key -match $WiringKey -or $key -eq 'ServiceIdentity__ClientSecret')) { $problems.Add("$overlay`: hr-app-config carries service wiring ($key)") }
            }
            foreach ($service in $matrix.services) {
                $label = "$overlay`: $($service.k8sDeployment)"
                $actual = $parsed.Deployments[[string]$service.k8sDeployment]
                if ($null -eq $actual) { $problems.Add("$label is not rendered"); continue }
                foreach ($p in (Compare-Wiring $label $service $actual)) { $problems.Add($p) }
                if ($service.serviceIdentity) {
                    $want = "secretKeyRef:hr-app-secrets/$($service.secretEnv)"
                    if ($actual['ServiceIdentity__ClientSecret'] -ne $want) { $problems.Add("$label ServiceIdentity__ClientSecret is '$($actual['ServiceIdentity__ClientSecret'])', expected $want") }
                    if (-not $parsed.SecretKeys.Contains([string]$service.secretEnv)) { $problems.Add("$label`: hr-app-secrets has no key $($service.secretEnv)") }
                }
            }
        }
        $overlays += [ordered]@{ name = $overlay; ok = ($problems.Count -eq 0); problems = @($problems) }
    }
    $report.kubernetes = [ordered]@{ skipped = $false; reason = $null; overlays = $overlays }
}

$all = @($report.chains | ForEach-Object { $_.problems }) + @($report.kubernetes.overlays | ForEach-Object { $_.problems })
$all = @($all | Where-Object { $_ })
$report.ok = ($all.Count -eq 0)
$reportPath = Join-Path $OutDir 'wiring-report.json'
[System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))

foreach ($c in $report.chains) { Write-Host ("[{0}] {1}" -f $(if ($c.ok) { 'PASS' } else { 'FAIL' }), $c.name); foreach ($p in $c.problems) { Write-Host "       - $p" } }
foreach ($o in @($report.kubernetes.overlays)) { Write-Host ("[{0}] k8s {1}" -f $(if ($o.ok) { 'PASS' } else { 'FAIL' }), $o.name); foreach ($p in $o.problems) { Write-Host "       - $p" } }
Write-Host "Report: $reportPath"
if (-not $report.ok) { exit 1 }
exit 0
