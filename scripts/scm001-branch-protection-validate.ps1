# SCM-001: validate repo governance artifacts (branch protection policy, CODEOWNERS, signing docs).
param(
    [string]$RepoRoot = "",
    [switch]$CheckLiveGitHub
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

$bp = Need "docs/scm-governance/branch-protection-main.json"
Need "docs/SCM_001_BRANCH_PROTECTION.md" | Out-Null
Need "docs/SCM_001_SIGNING_POLICY.md" | Out-Null
Need "docs/SCM_001_DEVELOPER_IDENTITY.md" | Out-Null
Need "docs/SCM_001_REPO_DISASTER_RECOVERY.md" | Out-Null
Need "docs/scm-governance/offboarding-checklist.md" | Out-Null
Need "scripts/scm001-repo-backup.ps1" | Out-Null

if (Test-Path $bp) {
    $j = Get-Content -Raw $bp | ConvertFrom-Json
    if ($j.card -ne "SCM-001") { $failures.Add("branch-protection card must be SCM-001") }
    if ($j.allow_force_pushes -ne $false) { $failures.Add("allow_force_pushes must be false") }
    if ($j.allow_deletions -ne $false) { $failures.Add("allow_deletions must be false") }
    if ($j.enforce_admins -ne $true) { $failures.Add("enforce_admins must be true") }
    if ($j.required_signatures -ne $true) { $failures.Add("required_signatures must be true") }
    if ($j.required_pull_request_reviews.required_approving_review_count -lt 2) {
        $failures.Add("required_approving_review_count must be >= 2 for main policy")
    }
    foreach ($c in @("CI", "CodeQL", "Gitleaks")) {
        if (@($j.required_status_checks.contexts) -notcontains $c) {
            $failures.Add("missing required check $c")
        }
    }
}

$owners = Need ".github/CODEOWNERS"
if (Test-Path $owners) {
    $text = Get-Content -Raw $owners
    foreach ($path in @("/.github/", "/deploy/", "/src/Shared/", "/docs/scm-governance/")) {
        if ($text -notmatch [regex]::Escape($path)) {
            $failures.Add("CODEOWNERS missing coverage hint for $path")
        }
    }
    # Prefer more than one owner token when org teams exist; warn if only @omard for critical paths
    $critical = Select-String -Path $owners -Pattern '^/\.github/|^/deploy/' -AllMatches
    if (-not $critical) {
        $failures.Add("CODEOWNERS must cover .github and deploy")
    }
}

if ($CheckLiveGitHub) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $gh) {
        $failures.Add("gh CLI required for -CheckLiveGitHub")
    }
    else {
        $json = gh api repos/:owner/:repo/branches/main/protection 2>&1
        if ($LASTEXITCODE -ne 0) {
            $failures.Add("live branch protection not readable/enabled: $json")
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "SCM-001 FAILED:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "SCM-001 OK: governance artifacts present."
exit 0
