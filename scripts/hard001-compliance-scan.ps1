# HARD-001: validate hardening baselines in compose, k8s, and documentation.
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

function Need([string]$rel) {
    $p = Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $p)) { $script:failures.Add("missing $rel") }
    return $p
}

$requiredDocs = @(
    "docs/hardening/HARD_001_POSTGRESQL.md",
    "docs/hardening/HARD_001_RABBITMQ.md",
    "docs/hardening/HARD_001_KEYCLOAK.md",
    "docs/hardening/HARD_001_MINIO.md",
    "docs/hardening/HARD_001_CONTAINERS.md",
    "docs/hardening/HARD_001_KUBERNETES.md",
    "docs/hardening/HARD_001_GATEWAY.md",
    "docs/hardening/HARD_001_EXCEPTION_REGISTER.md",
    "docs/HARD_001_VERIFICATION.md"
)
foreach ($d in $requiredDocs) { Need $d | Out-Null }

$exceptionRegister = Need "docs/hardening/HARD_001_EXCEPTION_REGISTER.md"
if (Test-Path $exceptionRegister) {
    $regText = Get-Content -Raw $exceptionRegister
    if ($regText -notmatch "Expires \(UTC\)") { $failures.Add("exception register missing expiry column") }
    if ($regText -notmatch "HARD-001-EX-") { $failures.Add("exception register missing entry template ID pattern") }
}

$composePath = Need "docker-compose.production.yml"
if (Test-Path $composePath) {
    $compose = Get-Content -Raw $composePath
    if ($compose -notmatch "x-app-hardening") { $failures.Add("compose missing x-app-hardening anchor") }
    if ($compose -notmatch "no-new-privileges") { $failures.Add("compose missing no-new-privileges") }
    if ($compose -notmatch "cap_drop") { $failures.Add("compose missing cap_drop") }
    if ($compose -notmatch "x-infra-hardening-mild") { $failures.Add("compose missing x-infra-hardening-mild for data plane") }
    # The service's own merge line (directly under the service key) must include *app-hardening; other anchors may be
    # merged too (PLAN-36 added *app-runtime for the restart policy).
    foreach ($svc in @("hr-gateway", "hr-core-web", "payroll-web", "file-storage-web")) {
        if ($compose -notmatch "(?m)^  ${svc}:\s*\r?\n\s+<<: \[[^\]]*\*app-hardening[^\]]*\]") {
            $failures.Add("$svc must merge app-hardening anchor")
        }
    }
}

$psaPath = Need "deploy/k8s/base/pod-security/restricted-psa.yaml"
if (Test-Path $psaPath) {
    $psa = Get-Content -Raw $psaPath
    if ($psa -notmatch "pod-security\.kubernetes\.io/enforce:\s*restricted") {
        $failures.Add("PSA enforce restricted label missing")
    }
}

Need "deploy/k8s/base/pod-security/security-context-defaults.yaml" | Out-Null
Need "deploy/k8s/base/pod-security/kustomization.yaml" | Out-Null

$baseKust = Need "deploy/k8s/base/kustomization.yaml"
if (Test-Path $baseKust) {
    $kust = Get-Content -Raw $baseKust
    if ($kust -notmatch "pod-security") { $failures.Add("base kustomization must reference pod-security") }
    if ($kust -notmatch "restricted-psa\.yaml") { $failures.Add("base kustomization must patch restricted-psa") }
}

Need "scripts/hard001-compliance-scan.ps1" | Out-Null
Need ".github/workflows/hardening-check.yml" | Out-Null

$exceptionsJson = Join-Path $RepoRoot "docs/hardening/hard001-exceptions.json"
if (Test-Path $exceptionsJson) {
    try {
        $ex = Get-Content -Raw $exceptionsJson | ConvertFrom-Json
        if ($ex.card -ne "HARD-001") { $failures.Add("hard001-exceptions.json card must be HARD-001") }
        $now = [DateTime]::UtcNow
        foreach ($row in @($ex.exceptions)) {
            if ($row.status -eq "Active" -and $row.expires) {
                $exp = [DateTime]::Parse($row.expires).ToUniversalTime()
                if ($exp -lt $now) {
                    $failures.Add("expired active exception: $($row.id)")
                }
            }
        }
    }
    catch {
        $failures.Add("hard001-exceptions.json invalid: $_")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "HARD-001 FAILED:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "HARD-001 OK: hardening baselines and enforcement artifacts present."
exit 0
