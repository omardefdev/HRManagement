# SAN-001: validate sanctions and supply chain classification artifacts.
# Usage:
#   powershell -File scripts/san001-validate.ps1 [-GlobalJsonSource Committed|WorkingTree]
# QA-034 (D-QA-12): the SDK pin that ships is the committed global.json, so by default the rollForward rule is checked
# on `git show HEAD:global.json`; a local, never-committed edit of the working-tree file does not fail the check.
# Without git (e.g. a container without .git) it falls back to the file. -GlobalJsonSource WorkingTree checks the file.

param(
    [string]$RepoRoot = "",
    [ValidateSet("Committed", "WorkingTree")]
    [string]$GlobalJsonSource = "Committed"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()

$requiredFiles = @(
    "docs/SAN_001_SANCTIONS.md",
    "docs/san/dependency-classification.json",
    "scripts/san001-validate.ps1",
    "scripts/san001-airgapped-build.ps1",
    "NuGet.offline.config",
    "src/Tests/HRManagement.UnitTests/San001SanctionsTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$validStates = @("available", "restricted", "unpayable", "geo-blocked")

$classFile = Resolve-RepoPath "docs/san/dependency-classification.json"
if (Test-Path $classFile) {
    $reg = Get-Content -Raw -Path $classFile | ConvertFrom-Json
    if ($reg.card -ne "SAN-001") { $failures += "classification card must be SAN-001" }
    if (-not $reg.dependencies -or $reg.dependencies.Count -lt 5) {
        $failures += "must classify at least 5 dependencies"
    }
    foreach ($dep in $reg.dependencies) {
        if (-not $dep.dependencyId) { $failures += "dependency missing dependencyId" }
        if (-not $dep.legalStatus) { $failures += "dependency $($dep.dependencyId) missing legalStatus" }
        elseif ($validStates -notcontains $dep.legalStatus) {
            $failures += "dependency $($dep.dependencyId) invalid legalStatus"
        }
        if (-not $dep.owner) { $failures += "dependency $($dep.dependencyId) missing owner" }
        if (-not $dep.criticality) { $failures += "dependency $($dep.dependencyId) missing criticality" }
    }
    if (-not $reg.mirrorPolicy) { $failures += "mirrorPolicy required" }
    if (-not $reg.pinningPolicy) { $failures += "pinningPolicy required" }
    elseif ($reg.pinningPolicy.rollForward -ne "disable") {
        $failures += "pinningPolicy.rollForward must be disable"
    }
}

$globalJsonPath = Resolve-RepoPath "global.json"
$globalJsonText = $null
$globalJsonFrom = $null
if ($GlobalJsonSource -eq "Committed" -and (Get-Command git -ErrorAction SilentlyContinue)) {
    $committed = & git -C $RepoRoot show HEAD:global.json 2>$null
    if ($LASTEXITCODE -eq 0 -and $committed) {
        $globalJsonText = ($committed -join "`n")
        $globalJsonFrom = "git show HEAD:global.json"
    }
}
if ($null -eq $globalJsonText -and (Test-Path $globalJsonPath)) {
    $globalJsonText = Get-Content -Raw $globalJsonPath
    $globalJsonFrom = "working tree global.json"
}
if ($null -ne $globalJsonText) {
    Write-Host "SAN-001: SDK pin read from $globalJsonFrom"
    $gj = $globalJsonText | ConvertFrom-Json
    if ($gj.sdk.rollForward -ne "disable") {
        $failures += "global.json rollForward must be disable (SAN-001; checked $globalJsonFrom)"
    }
}

$docFile = Resolve-RepoPath "docs/SAN_001_SANCTIONS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SAN-001") { $failures += "doc missing SAN-001 reference" }
    if ($doc -notmatch "sanction|export|supply") { $failures += "doc must describe sanctions/supply chain" }
    if ($doc -notmatch "mirror|sovereign") { $failures += "doc must mention sovereign mirror" }
}

$outDir = Resolve-RepoPath "testdata/san001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SAN-001"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outDir "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: SAN-001 sanctions supply chain validate"
exit 0
