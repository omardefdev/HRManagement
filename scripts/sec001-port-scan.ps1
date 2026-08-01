# SEC-001: validate production compose publishes only approved host ports.
# Usage:
#   powershell -File scripts/sec001-port-scan.ps1
# Optional:
#   powershell -File scripts/sec001-port-scan.ps1 -SkipDockerCompose

param(
    [string]$RepoRoot = "",
    [switch]$SkipDockerCompose
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

$mustHaveNoHostPorts = @(
    "hr-core-web", "hr-contracts-web", "hr-health-web", "hr-movements-web",
    "hr-training-web", "payroll-web", "idcard-web", "records-archive-web",
    "admin-logistics-web", "it-infra-web", "dev-analytics-web", "compliance-web",
    "auth-directory-web", "file-storage-web",
    "postgresql", "rabbitmq", "minio",
    "seq", "tempo", "otel-collector", "prometheus", "alertmanager",
    "alert-sink", "blackbox-exporter", "rabbitmq-exporter", "postgres-exporter"
)

$allowedPublishedServices = @{
    "hr-gateway" = $true
    "keycloak"   = $true
    "grafana"    = $true  # must be 127.0.0.1 only
}

# Static guard: production override must clear ports for internal services.
$prodFile = Join-Path $RepoRoot "docker-compose.production.yml"
if (-not (Test-Path $prodFile)) {
    $failures.Add("missing docker-compose.production.yml")
}
else {
    $prodText = Get-Content -Raw -Path $prodFile
    foreach ($svc in $mustHaveNoHostPorts) {
        # Each service block should contain ports: !override []
        $pattern = "(?ms)^\s{2}$([regex]::Escape($svc)):\s*.*?ports:\s*!override\s*\[\s*\]"
        if ($prodText -notmatch $pattern) {
            $failures.Add("production override missing ports: !override [] for $svc")
        }
    }

    if ($prodText -notmatch '(?ms)grafana:.*?127\.0\.0\.1:\$\{GRAFANA_HTTP_PORT') {
        $failures.Add("grafana must bind to 127.0.0.1 in production")
    }

    foreach ($net in @("application:", "data:", "observability:")) {
        $idx = $prodText.IndexOf("`n  $net")
        if ($idx -lt 0) { $idx = $prodText.IndexOf("  $net") }
        if ($idx -lt 0) {
            $failures.Add("network $net missing from production file")
            continue
        }
        $slice = $prodText.Substring($idx, [Math]::Min(200, $prodText.Length - $idx))
        if ($slice -notmatch "internal:\s*true") {
            $failures.Add("network $net must be internal: true")
        }
    }
}

# Chaos profile must not be active by default.
$chaosFile = Join-Path $RepoRoot "docker-compose.chaos.yml"
if (Test-Path $chaosFile) {
    $chaosText = Get-Content -Raw -Path $chaosFile
    if ($chaosText -notmatch 'profiles:\s*\[\s*"chaos"\s*\]') {
        $failures.Add("docker-compose.chaos.yml must use profiles: [`"chaos`"]")
    }
}

# Gateway: security-events ingest must not be AllowAnonymous.
$auditCtrl = Join-Path $RepoRoot "src\Gateway\HRManagement.Gateway\Controllers\AuditSecurityController.cs"
if (Test-Path $auditCtrl) {
    $auditText = Get-Content -Raw -Path $auditCtrl
    if ($auditText -match '\[AllowAnonymous\].*?security-events|security-events[\s\S]{0,120}\[AllowAnonymous\]') {
        $failures.Add("AuditSecurityController security-events must not be AllowAnonymous")
    }
    if ($auditText -match '\[HttpPost\("security-events"\)\]\s*\r?\n\s*\[AllowAnonymous\]') {
        $failures.Add("security-events endpoint still has AllowAnonymous")
    }
}
else {
    $failures.Add("missing AuditSecurityController.cs")
}

# NetworkPolicies present.
$npDir = Join-Path $RepoRoot "deploy\k8s\base\network-policies"
foreach ($need in @(
    "deny-all-default.yaml",
    "allow-dns.yaml",
    "allow-ingress-to-gateway.yaml",
    "allow-gateway-to-apps.yaml",
    "allow-apps-to-data.yaml",
    "allow-observability-scrape.yaml"
)) {
    if (-not (Test-Path (Join-Path $npDir $need))) {
        $failures.Add("missing NetworkPolicy $need")
    }
}

if (-not $SkipDockerCompose) {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) {
        Write-Host "WARN: docker not available; skipping live compose config check"
    }
    else {
        Push-Location $RepoRoot
        try {
            $args = @(
                "compose",
                "-f", "docker-compose.yml",
                "-f", "docker-compose.services.yml",
                "-f", "docker-compose.observability.yml",
                "-f", "docker-compose.production.yml",
                "config", "--format", "json"
            )
            $jsonText = & docker @args 2>&1
            if ($LASTEXITCODE -ne 0) {
                $failures.Add("docker compose config failed: $jsonText")
            }
            else {
                $cfg = $jsonText | ConvertFrom-Json
                foreach ($svcName in $mustHaveNoHostPorts) {
                    $svc = $cfg.services.$svcName
                    if ($null -eq $svc) { continue }
                    $ports = @($svc.ports)
                    if ($ports.Count -gt 0) {
                        $failures.Add("service $svcName still publishes ports: $($ports | ConvertTo-Json -Compress)")
                    }
                }

                foreach ($svcName in @("hr-gateway", "keycloak", "grafana")) {
                    $svc = $cfg.services.$svcName
                    if ($null -eq $svc) {
                        $failures.Add("expected published service missing: $svcName")
                        continue
                    }
                    $ports = @($svc.ports)
                    if ($ports.Count -eq 0 -and $svcName -ne "grafana") {
                        # compose may normalize differently; warn only if completely absent
                        Write-Host "WARN: $svcName has no ports in config (may be env-expanded elsewhere)"
                    }
                }

                $grafana = $cfg.services.grafana
                if ($null -ne $grafana) {
                    foreach ($p in @($grafana.ports)) {
                        $published = "$($p.published)$($p.host_ip)$($p)"
                        $hostIp = $p.host_ip
                        if ($hostIp -and $hostIp -ne "127.0.0.1") {
                            $failures.Add("grafana host_ip must be 127.0.0.1, got $hostIp")
                        }
                    }
                }
            }
        }
        finally {
            Pop-Location
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "SEC-001 FAILED:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "SEC-001 OK: production surface closed (static checks passed)."
exit 0
