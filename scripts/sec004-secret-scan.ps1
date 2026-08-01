# SEC-004: scan repository for known default secrets and risky patterns.
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

$prodCompose = Join-Path $RepoRoot "docker-compose.production.yml"
if (-not (Test-Path $prodCompose)) {
    $failures.Add("missing docker-compose.production.yml")
} else {
    $prodText = Get-Content -Raw $prodCompose
    foreach ($bad in @("hrminio_secret_change_me", "MINIO_ROOT_USER:-hrminio", "GF_SECURITY_ADMIN_PASSWORD:-admin")) {
        if ($prodText.Contains($bad)) {
            $failures.Add("production compose contains known default pattern: $bad")
        }
    }
    foreach ($need in @(
        "MINIO_ROOT_USER:?MINIO_ROOT_USER required",
        "MINIO_ROOT_PASSWORD:?MINIO_ROOT_PASSWORD required",
        "MINIO_FILESTORAGE_SECRET_KEY:?MINIO_FILESTORAGE_SECRET_KEY required",
        "GRAFANA_ADMIN_PASSWORD:?GRAFANA_ADMIN_PASSWORD required"
    )) {
        if (-not $prodText.Contains($need)) {
            $failures.Add("production compose missing fail-closed marker: $need")
        }
    }
    if ($prodText -match 'FileStorage__AccessKey:.*MINIO_ROOT') {
        $failures.Add("app must not use MINIO_ROOT for FileStorage__AccessKey in production")
    }
}

$dockerignore = Join-Path $RepoRoot ".dockerignore"
if (-not (Test-Path $dockerignore)) {
    $failures.Add("missing .dockerignore")
} else {
    $ignore = Get-Content -Raw $dockerignore
    foreach ($entry in @(".env", ".env.*", "*.secret", "secrets/")) {
        if (-not $ignore.Contains($entry)) {
            $failures.Add(".dockerignore missing $entry")
        }
    }
}

$scanRoots = @(
    (Join-Path $RepoRoot "deploy"),
    (Join-Path $RepoRoot "src\Services"),
    (Join-Path $RepoRoot "src\Gateway"),
    (Join-Path $RepoRoot "src\Shared")
)
foreach ($root in $scanRoots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem -Path $root -Recurse -File -Include *.cs,*.yaml,*.yml,*.json -ErrorAction SilentlyContinue |
        ForEach-Object {
            $content = Get-Content -Raw $_.FullName -ErrorAction SilentlyContinue
            if ($content -and $content -match ':\s*["'']?hrminio_secret_change_me|=\s*["'']?hrminio_secret_change_me') {
                $rel = $_.FullName.Substring($RepoRoot.Length).TrimStart("\", "/")
                $failures.Add("hardcoded MinIO default password assignment in $rel")
            }
        }
}

if ($failures.Count -gt 0) {
    Write-Host "SEC-004 secret scan FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "SEC-004 secret scan OK."
exit 0
