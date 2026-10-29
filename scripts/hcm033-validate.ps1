# HCM-033 (PLAN-33): validate that the recruitment path is proven honestly.
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts/hcm033-validate.ps1 [-SkipTests]
# Checks:
#   - no silent-pass pattern in the acceptance tests (WriteResult("Skip"…), raw Skip.If) and the architecture guard passes;
#   - every recruitment evidence folder (AC-HCM-21, 22, 26–33, AC-WF-23, AC-HCM-33/prodlike) has the eight items and a result;
#   - the docs no longer list SharedKernel/Recruitment as runtime, the onboarding guide has no «مختصر → تعيين» flow;
#   - the workflow YAML files pass a structural check (no YAML parser is available on the reference machine);
#   - the Hcm001 | Hcm033 | AcceptanceSkipPattern | AcceptanceGate unit tests pass.
# Writes artifacts/hcm033/validate-report.json (git-ignored).

param(
    [string]$RepoRoot = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()
$notes = @()

# 1. Silent passes in the acceptance tests.
$acceptanceDir = Resolve-RepoPath "src/Tests/HRManagement.AcceptanceTests"
foreach ($file in Get-ChildItem -Path $acceptanceDir -Filter "*.cs" -File) {
    $text = Get-Content -Raw -Encoding UTF8 -Path $file.FullName
    if ($text -match 'WriteResult\s*\([^;]*"Skip"') { $failures += "$($file.Name): WriteResult(""Skip"") is forbidden" }
    if ($file.Name -ne "AcceptanceGate.cs" -and $text -match '\bSkip\.If(Not)?\s*\(') { $failures += "$($file.Name): raw Skip.If — use AcceptanceGate.RequireData" }
}

# 2. Evidence folders: the eight items and a result.
$required = @("metadata.json", "steps.md", "before.json", "after.json", "audit.json", "db-checks.json", "timing.json", "result.json")
$folders = @("AC-HCM-21", "AC-HCM-22", "AC-HCM-26", "AC-HCM-27", "AC-HCM-28", "AC-HCM-29", "AC-HCM-30", "AC-HCM-31", "AC-HCM-32",
    "AC-HCM-33", "AC-HCM-33/prodlike", "AC-WF-23")
$evidence = [ordered]@{}
foreach ($folder in $folders) {
    $path = Resolve-RepoPath "acceptance-evidence/$folder"
    if (-not (Test-Path $path)) { $failures += "missing evidence folder acceptance-evidence/$folder"; continue }
    foreach ($item in $required) {
        if (-not (Test-Path (Join-Path $path $item))) { $failures += "acceptance-evidence/$folder missing $item" }
    }
    if (-not (Get-ChildItem -Path (Join-Path $path "http") -Filter "*.json" -ErrorAction SilentlyContinue)) {
        $failures += "acceptance-evidence/$folder has no http/*.json"
    }
    $resultPath = Join-Path $path "result.json"
    if (Test-Path $resultPath) {
        $result = Get-Content -Raw -Encoding UTF8 -Path $resultPath | ConvertFrom-Json
        if ($result.status -notin @("Pass", "Fail", "Blocked")) { $failures += "acceptance-evidence/$folder result status '$($result.status)' is not Pass/Fail/Blocked" }
        $evidence[$folder] = $result.status
    }
}

# 3. Docs.
$phantom = @("SharedKernel/Recruitment/RecruitmentRequisition.cs", "SharedKernel/Recruitment/CandidateApplication.cs",
    "SharedKernel/Recruitment/OfferLetter.cs")
foreach ($doc in @("docs/HCM_001_RECRUITMENT.md", "docs/completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md")) {
    # A line may mention the removed files only to say they were removed.
    $lines = Get-Content -Encoding UTF8 -Path (Resolve-RepoPath $doc)
    foreach ($p in $phantom) {
        $listed = $lines | Where-Object { $_.Contains($p) -and $_ -notmatch '(?i)deleted|removed|phantom|حُذف|حذف|وهمي' }
        if ($listed) { $failures += "$doc still lists $p as an artifact" }
    }
}
if (Test-Path (Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Recruitment")) {
    $failures += "src/Shared/HRManagement.SharedKernel/Recruitment must not exist"
}
$guide = Get-Content -Encoding UTF8 -Path (Resolve-RepoPath "docs/HIRE_ONBOARDING_GUIDE.md")
if ($guide | Where-Object { $_ -match 'مختصر\s*→\s*\**تعيين' -and $_ -notmatch 'حذف|حُذف|removed' }) {
    $failures += "HIRE_ONBOARDING_GUIDE.md still describes the shortlist-to-hire flow"
}

# 4. Workflow YAML: structural check (no tabs, top-level name/on/jobs, every job has runs-on and steps).
foreach ($wf in @(".github/workflows/ci.yml", ".github/workflows/e2e-journeys.yml")) {
    $lines = Get-Content -Encoding UTF8 -Path (Resolve-RepoPath $wf)
    if ($lines | Where-Object { $_ -match "`t" }) { $failures += "$wf contains tabs" }
    foreach ($key in @("name:", "on:", "jobs:")) {
        if (-not ($lines | Where-Object { $_ -eq $key -or $_.StartsWith("$key ") })) { $failures += "$wf has no top-level $key" }
    }
    $inJobs = $false; $job = $null; $jobs = @{}
    foreach ($line in $lines) {
        if ($line -match '^jobs:') { $inJobs = $true; continue }
        if ($inJobs -and $line -match '^\S') { $inJobs = $false }
        if ($inJobs -and $line -match '^  ([A-Za-z0-9_-]+):\s*$') { $job = $Matches[1]; $jobs[$job] = @{ runs = $false; steps = $false } }
        if ($inJobs -and $job -and $line -match '^    runs-on:') { $jobs[$job].runs = $true }
        if ($inJobs -and $job -and $line -match '^    steps:') { $jobs[$job].steps = $true }
    }
    foreach ($j in $jobs.Keys) {
        if (-not ($jobs[$j].runs -and $jobs[$j].steps)) { $failures += "$wf job '$j' lacks runs-on or steps" }
    }
}
$notes += "YAML: structural check only (no YAML parser / actionlint on the reference machine); CI not executed."

# 5. Unit tests.
if (-not $SkipTests) {
    Push-Location $RepoRoot
    try {
        dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm001|FullyQualifiedName~Hcm033|FullyQualifiedName~AcceptanceSkipPattern" -v q --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { $failures += "unit tests Hcm001|Hcm033|AcceptanceSkipPattern failed" }
        dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcceptanceGateTests|FullyQualifiedName~RecruitmentStatesFileTests" -v q --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { $failures += "AcceptanceGateTests|RecruitmentStatesFileTests failed" }
    }
    finally {
        Pop-Location
    }
}

$outDir = Resolve-RepoPath "artifacts/hcm033"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "HCM-033"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
    evidence = $evidence
    notes = $notes
}
($report | ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $outDir "validate-report.json") -Encoding utf8

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: HCM-033 recruitment verification validate"
$evidence.GetEnumerator() | ForEach-Object { Write-Host (" {0}: {1}" -f $_.Key, $_.Value) }
exit 0
