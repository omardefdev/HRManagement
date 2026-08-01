# SEC-004: validate secret rotation artifacts and production fail-closed compose.
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

Need "docs/SEC_004_SECRET_ROTATION.md" | Out-Null
Need "scripts/sec004-secret-scan.ps1" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Sec004SecretRotationTests.cs" | Out-Null

$prodPath = Need "docker-compose.production.yml"
if (Test-Path $prodPath) {
    $prod = Get-Content -Raw $prodPath
    $needles = @(
        "MINIO_ROOT_PASSWORD:?MINIO_ROOT_PASSWORD required",
        "GRAFANA_ADMIN_PASSWORD:?GRAFANA_ADMIN_PASSWORD required",
        "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET:?KEYCLOAK_HR_GATEWAY_CLIENT_SECRET required",
        "MINIO_FILESTORAGE_SECRET_KEY:?MINIO_FILESTORAGE_SECRET_KEY required"
    )
    foreach ($n in $needles) {
        if (-not $prod.Contains($n)) {
            $failures.Add("production compose missing fail-closed marker: $n")
        }
    }
    if ($prod -notmatch 'RABBITMQ_DEFAULT_PASS') {
        $failures.Add("production compose missing RABBITMQ_DEFAULT_PASS reference")
    }
}

$ignorePath = Need ".dockerignore"
if (Test-Path $ignorePath) {
    $ignore = Get-Content -Raw $ignorePath
    if (-not $ignore.Contains(".env")) {
        $failures.Add(".dockerignore must exclude .env")
    }
}

& (Join-Path $RepoRoot "scripts/sec004-secret-scan.ps1") -RepoRoot $RepoRoot
if ($LASTEXITCODE -ne 0) {
    $failures.Add("sec004-secret-scan.ps1 failed")
}

if ($failures.Count -gt 0) {
    Write-Host "SEC-004 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "SEC-004 validation OK."
exit 0
