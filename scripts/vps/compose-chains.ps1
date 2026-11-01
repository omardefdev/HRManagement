#Requires -Version 5.1
<#
.SYNOPSIS
  Shared compose chain definitions and helpers, dot-sourced by scripts/vps/lint-compose.ps1 (PLAN-36) and
  scripts/int037-wiring-check.ps1 (PLAN-37), so both render exactly the same chains.
.DESCRIPTION
  The caller defines $RepoRoot and $OutDir before dot-sourcing this file. Rendered output is written under $OutDir
  (git-ignored) and never printed. Lint-only values only; never a real .env.
#>

# Production chains (all with --profile full-stack).
function Get-HrmProductionChains {
    $production = @('docker-compose.yml', 'docker-compose.services.yml', 'docker-compose.images.yml', 'docker-compose.production.yml')
    return @(
        @{ Name = 'production'; Files = $production; SingleHost = $false },
        @{ Name = 'single-host'; Files = $production + @('docker-compose.vps.yml', 'docker-compose.vps-demo.yml'); SingleHost = $true }
    )
}

# PLAN-37 (section 7): the prod-like acceptance stack (scripts/acceptance-up.ps1 -ProdLike).
function Get-HrmProdLikeChain {
    return @{ Name = 'prodlike'; Files = @('docker-compose.yml', 'docker-compose.services.yml', 'docker-compose.dev-profiles.yml',
            'docker-compose.full-stack-gateway.yml', 'docker-compose.acceptance.yml', 'docker-compose.acceptance-prodlike.yml'); SingleHost = $false }
}

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
            'KEYCLOAK_DIRECTORY_SYNC_SECRET', 'HR_CORE_OTP_HMAC_KEY', 'ACCEPTANCE_GATEWAY_CALLER_PROOF_SECRET')) {
        if (-not $values.Contains($k)) { $values[$k] = 'lint-only-' + $k.ToLowerInvariant().Replace('_', '-') }
    }
    foreach ($svc in @('ADMIN_LOGISTICS', 'AUTH_DIRECTORY', 'COMPLIANCE', 'DEV_ANALYTICS', 'HR_CONTRACTS', 'HR_CORE', 'HR_HEALTH', 'HR_MOVEMENTS',
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
function Invoke-Native([string]$FilePath, [string[]]$Arguments, [hashtable]$Environment = @{}, [string[]]$RemoveEnvironment = @()) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ConvertTo-ArgumentString $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($k in $RemoveEnvironment) { if ($psi.EnvironmentVariables.ContainsKey($k)) { $psi.EnvironmentVariables.Remove($k) } }
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
    # compose prefers the shell environment over --env-file: a real exported secret would otherwise be rendered into
    # artifacts/. The child process gets neither the env-file keys nor any secret-looking variable of this shell.
    $clear = @(Get-Content -LiteralPath $EnvFile | ForEach-Object { ($_ -split '=', 2)[0] } | Where-Object { $_ })
    $clear += @(Get-ChildItem Env: | Where-Object { $_.Name -match '(SECRET|PASSWORD|TOKEN|_KEY)' } | ForEach-Object { $_.Name })
    $run = Invoke-Native -FilePath 'docker' -Arguments $cliArgs -RemoveEnvironment $clear
    [System.IO.File]::WriteAllText((Join-Path $OutDir "$Name.json"), $run.StdOut, (New-Object System.Text.UTF8Encoding($false)))
    if ($run.ExitCode -ne 0) {
        $err = ($run.StdErr -split "`n" | Where-Object { $_ -and $_ -notmatch 'level=warning' }) -join ' '
        return @{ Ok = $false; Error = "docker compose config failed: $err" }
    }
    return @{ Ok = $true; Config = ($run.StdOut | ConvertFrom-Json) }
}
