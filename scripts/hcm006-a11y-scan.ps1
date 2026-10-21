# HCM-006: baseline a11y checks for public careers views.
param([string]$RepoRoot = "")

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$views = @(
    "src/Services/HRCore/HR.Core.Web/Views/PublicCareers/Details.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/PublicCareers/Track.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/PublicCareers/TrackResult.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/Shared/_PublicLayout.cshtml"
)

$failures = @()
foreach ($rel in $views) {
    $path = Resolve-RepoPath $rel
    if (-not (Test-Path $path)) {
        $failures += "missing $rel"
        continue
    }
    $content = Get-Content -Raw -Path $path
    if ($content -notmatch 'aria-label|for="') {
        $failures += "$rel missing aria-label or label/for pairing"
    }
}

$layout = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Views/Shared/_PublicLayout.cshtml"
if (Test-Path $layout) {
    $layoutContent = Get-Content -Raw -Path $layout
    if ($layoutContent -notmatch 'hr-skip-link') { $failures += "_PublicLayout missing skip link" }
    if ($layoutContent -notmatch 'id="main-content"') { $failures += "_PublicLayout missing main landmark" }
}

if ($failures.Count -gt 0) {
    Write-Host "FAIL"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: HCM-006 public careers a11y baseline"
exit 0
