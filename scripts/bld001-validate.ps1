# BLD-001: validate build provenance docs, NuGet mirror config, and Kyverno admission policy.

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



Need "docs/BLD_001_BUILD_PROVENANCE.md" | Out-Null

Need "NuGet.config" | Out-Null

Need "deploy/k8s/base/admission/require-signed-images.yaml" | Out-Null

Need "deploy/k8s/base/admission/kustomization.yaml" | Out-Null

Need "scripts/bld001-verify-provenance.ps1" | Out-Null

Need "src/Tests/HRManagement.UnitTests/Bld001BuildProvenanceTests.cs" | Out-Null



$nuget = Need "NuGet.config"

if (Test-Path $nuget) {

    $xml = Get-Content -Raw $nuget

    if ($xml -notmatch "sovereign-mirror") { $failures.Add("NuGet.config must define sovereign-mirror source") }

    if ($xml -notmatch "SOVEREIGN_NUGET_MIRROR") { $failures.Add("NuGet.config must document SOVEREIGN_NUGET_MIRROR placeholder") }

}



$admission = Need "deploy/k8s/base/admission/require-signed-images.yaml"

if (Test-Path $admission) {

    $yaml = Get-Content -Raw $admission

    if ($yaml -notmatch "require-signed-images") { $failures.Add("admission policy name missing") }

    if ($yaml -notmatch "verifyImages") { $failures.Add("admission policy must use verifyImages") }

    if ($yaml -notmatch "ghcr.io/OWNER/hrmanagement") { $failures.Add("admission must scope hrmanagement images") }

}



$kustBase = Join-Path $RepoRoot "deploy/k8s/base/kustomization.yaml"

if (Test-Path $kustBase) {

    $base = Get-Content -Raw $kustBase

    if ($base -notmatch "admission") { $failures.Add("base kustomization must include admission") }

}



$deployYml = Join-Path $RepoRoot ".github/workflows/deploy.yml"

if (Test-Path $deployYml) {

    $deploy = Get-Content -Raw $deployYml

    if ($deploy -notmatch "cosign sign") { $failures.Add("deploy.yml must cosign sign images") }

    if ($deploy -notmatch "attest-build-provenance") { $failures.Add("deploy.yml must attest build provenance") }

}



if ($failures.Count -gt 0) {

    Write-Host "BLD-001 validation FAILED:"

    $failures | ForEach-Object { Write-Host "  - $_" }

    exit 1

}



Write-Host "PASS: BLD-001 validation OK."

exit 0

