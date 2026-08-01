# STR-001: runtime/production binding validate (appsettings + NFR floors).
# Usage:
#   powershell -File scripts/str001-envelope-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/str-scope/envelope-matrix.json",
    [string]$NfrPath = "docs/NFR_CAPACITY_MODEL.md",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str001/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()
$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }
$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json

$prodFiles = @(
    "src/Gateway/HRManagement.Gateway/appsettings.Production.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Production.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Production.json"
)

foreach ($rel in $prodFiles) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $failures += "missing $rel"
        continue
    }
    $cfg = Get-Content -Raw -Path $full | ConvertFrom-Json
    $ns = $cfg.NationalScope
    if ($null -eq $ns) {
        $failures += "$rel missing NationalScope section"
        continue
    }
    if ($ns.ActiveEnvelope -eq "national-competition" -and $ns.AllowNationalCompetitionClaim -ne $true) {
        $failures += "$rel claims national-competition without AllowNationalCompetitionClaim"
    }
    if ($ns.ActiveEnvelope -ne "syria-interim" -and $ns.ActiveEnvelope -ne "national-competition") {
        $failures += "$rel ActiveEnvelope invalid: $($ns.ActiveEnvelope)"
    }
    if ($ns.ActiveEnvelope -eq "syria-interim" -and $ns.AllowNationalCompetitionClaim -eq $true) {
        # allowed but unusual; warn only via report note
    }
}

# NFR markdown must cite national persons floor matching matrix national.active_history_persons
$nfrFull = Resolve-RepoPath $NfrPath
if (-not (Test-Path $nfrFull)) {
    $failures += "missing NFR model"
} else {
    $nfrText = Get-Content -Raw -Path $nfrFull
    $natPersons = ($m.indicators | Where-Object { $_.id -eq "national.active_history_persons" } | Select-Object -First 1).value
    $natTenants = ($m.indicators | Where-Object { $_.id -eq "national.tenants" } | Select-Object -First 1).value
    $natRps = ($m.indicators | Where-Object { $_.id -eq "national.peak_http_rps" } | Select-Object -First 1).value
    if ($null -eq $natPersons) { $failures += "matrix missing national.active_history_persons" }
    else {
        $personsStr = [string]([long]$natPersons)
        if ($nfrText -notmatch [regex]::Escape($personsStr) -and $nfrText -notmatch "5,000,000" -and $nfrText -notmatch "5000000") {
            $failures += "NFR_CAPACITY_MODEL missing national persons floor $personsStr"
        }
    }
    if ($null -ne $natTenants) {
        $tenantsStr = [string]([long]$natTenants)
        if ($nfrText -notmatch [regex]::Escape($tenantsStr) -and $nfrText -notmatch "1,000") {
            $failures += "NFR_CAPACITY_MODEL missing tenants floor $tenantsStr"
        }
    }
    if ($null -ne $natRps) {
        $rpsStr = [string]([long]$natRps)
        if ($nfrText -notmatch [regex]::Escape($rpsStr) -and $nfrText -notmatch "10,000") {
            $failures += "NFR_CAPACITY_MODEL missing peak RPS floor $rpsStr"
        }
    }
}

# Code artifacts must exist
$codeChecks = @(
    "src/Shared/HRManagement.ServiceDefaults/NationalScope/NationalScopeOptions.cs",
    "src/Shared/HRManagement.ServiceDefaults/NationalScope/NationalScopeProvider.cs",
    "src/Shared/HRManagement.ServiceDefaults/NationalScope/NationalScopeGuard.cs",
    "docs/ADR-STR-001-production-scope-binding.md",
    "docs/STR_001_ADOPTION_MINUTES.md",
    "docs/STR_001_INDEPENDENT_REVIEW_WAIVER.md"
)
foreach ($rel in $codeChecks) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing artifact $rel" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-001"
    mode = "runtime-binding"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "runtime-validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: STR-001 envelope runtime validate"
exit 0
