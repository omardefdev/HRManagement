<#
.SYNOPSIS
  PLAN-36 (section 8): renders the production chain and the single-host chain with `docker compose config` and fails
  on any rule violation.

.DESCRIPTION
  Chains (all with --profile full-stack):
    production   docker-compose.yml + services + images + production
    single-host  production + docker-compose.vps.yml + docker-compose.vps-demo.yml
  Rules:
    - every service has an image, no build context, and no image tagged latest (or untagged);
    - no development value in any service environment: localhost:8088, ASPNETCORE_ENVIRONMENT=Development, YOUR_,
      USE_ENV, dev-svc-secret-local-only;
    - every container has a memory limit;
    - every container has a restart policy; "no" only for one-shot containers that others wait for with
      service_completed_successfully;
    - single-host: no published port except the edge's 443 (tcp, udp) and Seq on 127.0.0.1; no OpenTelemetry
      collector or Prometheus; the gateway trusts only the edge address; OpenTelemetry is off;
    - production: only the gateway and Keycloak publish ports (SEC-001);
    - with the minio profile: MINIO_IMAGE, MINIO_ROOT_USER and MINIO_ROOT_PASSWORD are not empty.
  The chains are rendered with lint-only values (never a real .env); the rendered JSON is written under
  artifacts/plan36/lint/ (git-ignored) and is never printed.
  PowerShell 5.1: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/lint-compose.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [string[]]$Profiles = @('full-stack'),
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# $PSScriptRoot is not usable in param defaults under -File on Windows PowerShell 5.1.
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path }
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'artifacts\plan36\lint' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$EdgeAddress = '172.28.0.10'
$Forbidden = @('localhost:8088', 'YOUR_', 'USE_ENV', 'dev-svc-secret-local-only')

# Lint-only values for every variable the chains require; names come from deploy/vps/.env.vps.example when present.
function New-LintEnvFile([string]$Path) {
    $values = [ordered]@{}
    $example = Join-Path $RepoRoot 'deploy\vps\.env.vps.example'
    if (Test-Path $example) {
        foreach ($line in Get-Content -Path $example) {
            # Keep the example's own non-secret defaults (HRM_OTEL_ENABLED=false, ...); fill only the empty keys.
            if ($line -match '^\s*([A-Z][A-Z0-9_]*)\s*=(.*)$') {
                $value = $Matches[2].Trim()
                if (-not $value) { $value = 'lint-only-' + $Matches[1].ToLowerInvariant().Replace('_', '-') }
                $values[$Matches[1]] = $value
            }
        }
    }
    $fixed = [ordered]@{
        APP_DOMAIN                       = 'hr.lint.test'
        SSO_DOMAIN                       = 'sso.lint.test'
        HRM_IMAGE_REPO                   = 'registry.lint.test/hrmanagement'
        GITHUB_REPOSITORY_OWNER          = 'lintowner'
        IMAGE_TAG                        = '0123456789ab'
        GATEWAY_CALLER_PROOF_SECRET      = 'lint-only-caller-proof-secret-0123456789abcdef'
        DEMO_USERS_PASSWORD              = 'lint-only-demo-password-0123'
        HRM_DEPLOYMENT_MODE              = 'demo'
        COMPOSE_PROFILES                 = 'full-stack'
        HRM_ACME_CA                      = 'https://acme-staging-v02.api.letsencrypt.org/directory'
        ACME_EMAIL                       = ''
        ADMIN_ALLOWED_CIDRS              = ''
        HRM_ORGANIZATION_NAME            = 'lint organization'
        POSTGRES_USER                    = 'lint_admin'
        RABBITMQ_DEFAULT_USER            = 'lint_admin'
        KEYCLOAK_ADMIN                   = 'lint_admin'
    }
    foreach ($k in $fixed.Keys) { $values[$k] = $fixed[$k] }
    foreach ($k in @('POSTGRES_PASSWORD', 'RABBITMQ_DEFAULT_PASS', 'KEYCLOAK_ADMIN_PASSWORD', 'KEYCLOAK_HR_GATEWAY_CLIENT_SECRET',
            'KEYCLOAK_DIRECTORY_SYNC_SECRET', 'HR_CORE_OTP_HMAC_KEY')) {
        if (-not $values.Contains($k)) { $values[$k] = 'lint-only-' + $k.ToLowerInvariant().Replace('_', '-') }
    }
    foreach ($svc in @('ADMIN_LOGISTICS', 'COMPLIANCE', 'DEV_ANALYTICS', 'HR_CONTRACTS', 'HR_CORE', 'HR_HEALTH', 'HR_MOVEMENTS',
            'HR_TRAINING', 'IDCARD', 'IT_INFRA', 'PAYROLL', 'RECORDS_ARCHIVE')) {
        $k = "SVC_${svc}_CLIENT_SECRET"
        if (-not $values.Contains($k)) { $values[$k] = 'lint-only-' + $k.ToLowerInvariant().Replace('_', '-') }
    }
    $lines = foreach ($k in $values.Keys) { "$k=$($values[$k])" }
    [System.IO.File]::WriteAllLines($Path, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))
}

function ConvertTo-ArgumentString([string[]]$Arguments) {
    ($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
}

# Runs a native command; stdout and stderr are returned as UTF-8 text (no PowerShell error records).
function Invoke-Native([string]$FilePath, [string[]]$Arguments, [hashtable]$Environment = @{}) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ConvertTo-ArgumentString $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    return @{ ExitCode = $proc.ExitCode; StdOut = $stdout; StdErr = $stderrTask.Result }
}

function Invoke-ComposeConfig([string]$Name, [string[]]$Files, [string]$EnvFile, [string[]]$ChainProfiles) {
    $cliArgs = @('compose', '--env-file', $EnvFile, '--project-directory', $RepoRoot)
    foreach ($f in $Files) { $cliArgs += @('-f', (Join-Path $RepoRoot $f)) }
    foreach ($p in $ChainProfiles) { $cliArgs += @('--profile', $p) }
    $cliArgs += @('config', '--format', 'json')
    $run = Invoke-Native -FilePath 'docker' -Arguments $cliArgs
    [System.IO.File]::WriteAllText((Join-Path $OutDir "$Name.json"), $run.StdOut, (New-Object System.Text.UTF8Encoding($false)))
    if ($run.ExitCode -ne 0) {
        $err = ($run.StdErr -split "`n" | Where-Object { $_ -and $_ -notmatch 'level=warning' }) -join ' '
        return @{ Ok = $false; Error = "docker compose config failed: $err" }
    }
    return @{ Ok = $true; Config = ($run.StdOut | ConvertFrom-Json) }
}

function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function Test-Chain([string]$Name, $Config, [bool]$SingleHost) {
    $violations = New-Object System.Collections.Generic.List[string]
    $services = $Config.services.PSObject.Properties
    $oneShot = @{}
    foreach ($s in $services) {
        $deps = Get-Prop $s.Value 'depends_on'
        if ($deps) {
            foreach ($d in $deps.PSObject.Properties) {
                if ((Get-Prop $d.Value 'condition') -eq 'service_completed_successfully') { $oneShot[$d.Name] = $true }
            }
        }
    }

    foreach ($s in $services) {
        $svc = $s.Name; $v = $s.Value
        $image = [string](Get-Prop $v 'image')
        if (-not $image) { $violations.Add("${svc}: no image") }
        elseif ($image -match ':latest$' -or ($image -notmatch ':[^/]+$' -and $image -notmatch '@sha256:')) {
            $violations.Add("${svc}: image '$image' is latest or untagged")
        }
        if (Get-Prop $v 'build') { $violations.Add("${svc}: has a build context") }

        $envBlock = Get-Prop $v 'environment'
        if ($envBlock) {
            foreach ($e in $envBlock.PSObject.Properties) {
                $value = [string]$e.Value
                if ($e.Name -eq 'ASPNETCORE_ENVIRONMENT' -and $value -eq 'Development') { $violations.Add("${svc}: ASPNETCORE_ENVIRONMENT=Development") }
                foreach ($f in $Forbidden) {
                    if ($value.IndexOf($f, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $violations.Add("${svc}: $($e.Name) contains '$f'") }
                }
            }
        }

        $limit = Get-Prop (Get-Prop (Get-Prop (Get-Prop $v 'deploy') 'resources') 'limits') 'memory'
        if (-not $limit) { $violations.Add("${svc}: no memory limit") }

        $restart = [string](Get-Prop $v 'restart')
        if (-not $restart) { $violations.Add("${svc}: no restart policy") }
        elseif ($restart -eq 'no' -and -not $oneShot.ContainsKey($svc)) { $violations.Add("${svc}: restart 'no' on a long-running container") }

        $ports = Get-Prop $v 'ports'
        if ($ports) {
            foreach ($p in @($ports)) {
                $published = [string](Get-Prop $p 'published'); $hostIp = [string](Get-Prop $p 'host_ip'); $target = [string](Get-Prop $p 'target')
                $desc = "${hostIp}:${published}->${target}/$(Get-Prop $p 'protocol')"
                if ($SingleHost) {
                    $ok = ($svc -eq 'edge' -and $published -eq '443' -and $target -eq '443' -and -not $hostIp) -or
                          ($svc -eq 'seq' -and $hostIp -eq '127.0.0.1')
                    if (-not $ok) { $violations.Add("${svc}: publishes $desc (single host allows only edge 443 and Seq on loopback)") }
                }
                elseif ($svc -ne 'hr-gateway' -and $svc -ne 'keycloak') {
                    $violations.Add("${svc}: publishes $desc (production allows only the gateway and Keycloak)")
                }
            }
        }
    }

    if ($Config.services.PSObject.Properties['minio']) {
        $minio = $Config.services.minio
        if (-not (Get-Prop $minio 'image')) { $violations.Add('minio: MINIO_IMAGE is empty') }
        $menv = Get-Prop $minio 'environment'
        foreach ($k in @('MINIO_ROOT_USER', 'MINIO_ROOT_PASSWORD')) {
            if (-not [string](Get-Prop $menv $k)) { $violations.Add("minio: $k is empty") }
        }
    }

    if ($SingleHost) {
        foreach ($forbiddenService in @('otel-collector', 'prometheus', 'tempo', 'grafana')) {
            if ($Config.services.PSObject.Properties[$forbiddenService]) { $violations.Add("$forbiddenService must not run on the single host") }
        }
        $gwEnv = Get-Prop $Config.services.'hr-gateway' 'environment'
        if ([string](Get-Prop $gwEnv 'ForwardedHeaders__KnownProxies') -ne $EdgeAddress) { $violations.Add("hr-gateway: ForwardedHeaders__KnownProxies is not $EdgeAddress") }
        foreach ($s in $services) {
            $e = Get-Prop $s.Value 'environment'
            if ($e -and (Get-Prop $e 'OpenTelemetry__Enabled') -and [string](Get-Prop $e 'OpenTelemetry__Enabled') -ne 'false') { $violations.Add("$($s.Name): OpenTelemetry__Enabled is not false") }
        }
        if (-not $Config.services.PSObject.Properties['edge']) { $violations.Add('edge service missing') }
    }

    return ,$violations
}

$envFile = Join-Path $OutDir 'lint.env'
New-LintEnvFile -Path $envFile

$production = @('docker-compose.yml', 'docker-compose.services.yml', 'docker-compose.images.yml', 'docker-compose.production.yml')
$chains = @(
    @{ Name = 'production'; Files = $production; SingleHost = $false },
    @{ Name = 'single-host'; Files = $production + @('docker-compose.vps.yml', 'docker-compose.vps-demo.yml'); SingleHost = $true }
)

$failed = $false
foreach ($chain in $chains) {
    $result = Invoke-ComposeConfig -Name $chain.Name -Files $chain.Files -EnvFile $envFile -ChainProfiles $Profiles
    if (-not $result.Ok) {
        Write-Host "[FAIL] $($chain.Name): $($result.Error)"
        $failed = $true
        continue
    }
    $violations = Test-Chain -Name $chain.Name -Config $result.Config -SingleHost $chain.SingleHost
    $count = @($result.Config.services.PSObject.Properties).Count
    if ($violations.Count -eq 0) {
        Write-Host "[PASS] $($chain.Name): $count services"
    }
    else {
        $failed = $true
        Write-Host "[FAIL] $($chain.Name): $($violations.Count) violation(s)"
        foreach ($v in $violations) { Write-Host "       - $v" }
    }
}

if ($failed) { exit 1 }
exit 0
