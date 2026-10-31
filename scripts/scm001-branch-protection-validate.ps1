# SCM-001: validate repo governance artifacts (branch protection policy, CODEOWNERS, signing docs).
# PLAN-35: the protection file may be weaker than the SCM-001 policy floors only through exceptionRefs that name an
# Open entry of docs/qa/governance-exceptions.json whose reviewBy is not past; every required context must be a job
# name of .github/workflows/ci.yml; CODEOWNERS names only the repository owner. -CheckLiveGitHub compares the live
# protection of main (gh api) with the file; -LiveProtectionFile does the same from a saved API response (tests).
#
#   powershell -File scripts/scm001-branch-protection-validate.ps1
#   powershell -File scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub
#   powershell -File scripts/scm001-branch-protection-validate.ps1 -Protection testdata/qa/branch-protection/x.json -AsOfDate 2026-11-01
# Windows PowerShell 5.1 and PowerShell 7. ASCII only.
param(
    [string]$RepoRoot = "",
    [switch]$CheckLiveGitHub,
    [string]$Protection = "docs/scm-governance/branch-protection-main.json",
    [string]$Exceptions = "docs/qa/governance-exceptions.json",
    [string]$Workflow = ".github/workflows/ci.yml",
    [string]$AsOfDate = "",
    [string]$LiveProtectionFile = "",
    [string]$HookFile = "",
    [string]$Repo = "omardefdev/HRManagement",
    [string]$Owner = "omardefdev"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
$jsonArgs = @{}
if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $jsonArgs['DateKind'] = 'String' }
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

$failures = [System.Collections.Generic.List[string]]::new()

function Resolve-RepoPath([string]$rel) {
    if ([IO.Path]::IsPathRooted($rel)) { return $rel }
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

function Need([string]$rel) {
    $p = Resolve-RepoPath $rel
    if (-not (Test-Path $p)) { $script:failures.Add("missing $rel") }
    return $p
}

function ConvertTo-Day([string]$text) {
    $d = [datetime]::MinValue
    if ([datetime]::TryParseExact($text, "yyyy-MM-dd", $invariant, [System.Globalization.DateTimeStyles]::None, [ref]$d)) { return $d }
    return $null
}

if ($AsOfDate) { $asOf = ConvertTo-Day $AsOfDate; if ($null -eq $asOf) { throw "AsOfDate must be yyyy-MM-dd" } }
else { $asOf = [datetime]::UtcNow.Date }

$bp = Need $Protection
Need "docs/SCM_001_BRANCH_PROTECTION.md" | Out-Null
Need "docs/SCM_001_SIGNING_POLICY.md" | Out-Null
Need "docs/SCM_001_DEVELOPER_IDENTITY.md" | Out-Null
Need "docs/SCM_001_REPO_DISASTER_RECOVERY.md" | Out-Null
Need "docs/scm-governance/offboarding-checklist.md" | Out-Null
Need "scripts/scm001-repo-backup.ps1" | Out-Null

# Open, unexpired governance exceptions by id.
$open = @{}
$exPath = Resolve-RepoPath $Exceptions
if (Test-Path $exPath) {
    $ex = Get-Content -Raw $exPath | ConvertFrom-Json @jsonArgs
    foreach ($e in @($ex.exceptions | Where-Object { $_ })) {
        $due = ConvertTo-Day "$($e.reviewBy)"
        if ("$($e.status)" -ne "Open") { continue }
        if ($null -eq $due) { $failures.Add("governance exception $($e.id): reviewBy '$($e.reviewBy)' is not yyyy-MM-dd"); continue }
        if ($due -lt $asOf) { continue }
        $open["$($e.id)"] = $e
    }
}

# Job display names of the workflow that defines the required checks.
$jobNames = @()
$wfPath = Resolve-RepoPath $Workflow
if (Test-Path $wfPath) {
    $jobNames = @(Get-Content $wfPath | Where-Object { $_ -match '^    name:\s*(.+?)\s*$' } | ForEach-Object { $Matches[1].Trim('"', "'") })
}
else { $failures.Add("missing $Workflow") }

$j = $null
if (Test-Path $bp) {
    $j = Get-Content -Raw $bp | ConvertFrom-Json @jsonArgs
    if ($j.card -ne "SCM-001") { $failures.Add("branch-protection card must be SCM-001") }

    # Hard rules: no exception can relax them.
    if ($j.allow_force_pushes -ne $false) { $failures.Add("allow_force_pushes must be false") }
    if ($j.allow_deletions -ne $false) { $failures.Add("allow_deletions must be false") }
    if ($j.enforce_admins -ne $true) { $failures.Add("enforce_admins must be true") }
    if ($null -eq $j.required_pull_request_reviews) { $failures.Add("required_pull_request_reviews must be set (pull request required)") }
    if ($j.required_status_checks.strict -ne $true) { $failures.Add("required_status_checks.strict must be true") }

    # Policy floors: a weaker value needs exceptionRefs -> an Open, unexpired governance exception.
    $rev = $j.required_pull_request_reviews
    $floors = @(
        @{ key = "required_pull_request_reviews.required_approving_review_count"; ok = ($null -ne $rev -and [int]$rev.required_approving_review_count -ge 2); want = ">= 2" },
        @{ key = "required_pull_request_reviews.require_code_owner_reviews"; ok = ($null -ne $rev -and $rev.require_code_owner_reviews -eq $true); want = "true" },
        @{ key = "required_pull_request_reviews.require_last_push_approval"; ok = ($null -ne $rev -and $rev.require_last_push_approval -eq $true); want = "true" },
        @{ key = "required_pull_request_reviews.dismiss_stale_reviews"; ok = ($null -ne $rev -and $rev.dismiss_stale_reviews -eq $true); want = "true" },
        @{ key = "required_signatures"; ok = ($j.required_signatures -eq $true); want = "true" },
        @{ key = "required_linear_history"; ok = ($j.required_linear_history -eq $true); want = "true" },
        @{ key = "required_conversation_resolution"; ok = ($j.required_conversation_resolution -eq $true); want = "true" }
    )
    foreach ($f in $floors) {
        if ($f.ok) { continue }
        $ref = $null
        if ($j.exceptionRefs) { $ref = $j.exceptionRefs.($f.key) }
        if (-not $ref) { $failures.Add("$($f.key) is below policy ($($f.want)) without exceptionRefs"); continue }
        if (-not $open.ContainsKey("$ref")) {
            $failures.Add("$($f.key) is below policy ($($f.want)); exceptionRefs '$ref' is not an Open, unexpired entry of $Exceptions (as of $($asOf.ToString('yyyy-MM-dd')))")
        }
    }

    $contexts = @($j.required_status_checks.contexts)
    # D-QA-2: E2E is required since PLAN-35 P7 (two consecutive green runs on main).
    foreach ($c in @("CI", "CodeQL", "Gitleaks", "E2E")) {
        if ($contexts -notcontains $c) { $failures.Add("missing required check $c") }
    }
    $named = @($contexts) + @($j.pendingRequiredChecks | Where-Object { $_ } | ForEach-Object { $_.context })
    foreach ($c in $named) {
        if ($jobNames -notcontains $c) { $failures.Add("required check '$c' is not a job name in $Workflow (jobs: $($jobNames -join ', '))") }
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
    $critical = Select-String -Path $owners -Pattern '^/\.github/|^/deploy/' -AllMatches
    if (-not $critical) {
        $failures.Add("CODEOWNERS must cover .github and deploy")
    }
    # PLAN-35: @omard (CODEOWNERS before PLAN-35) is another GitHub user; every owner token is the repository owner.
    $tokens = @(Get-Content $owners | Where-Object { $_ -notmatch '^\s*#' } | ForEach-Object { [regex]::Matches($_, '@[A-Za-z0-9_./-]+') | ForEach-Object { $_.Value } } | Sort-Object -Unique)
    foreach ($t in $tokens) {
        if ($t -ne "@$Owner") { $failures.Add("CODEOWNERS names $t; expected only @$Owner (repository owner)") }
    }
}

if (($CheckLiveGitHub -or $LiveProtectionFile) -and $null -ne $j) {
    $live = $null
    # GitHub answers 403 "Upgrade to GitHub Pro or make this repository public" for a private repository on the free plan.
    $unavailablePattern = 'Upgrade to GitHub Pro'
    $unavailable = $false
    if ($LiveProtectionFile) {
        $live = Get-Content -Raw (Resolve-RepoPath $LiveProtectionFile) | ConvertFrom-Json @jsonArgs
        if ("$($live.message)" -match $unavailablePattern) { $unavailable = $true; $live = $null }
    }
    elseif ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
        $failures.Add("gh CLI required for -CheckLiveGitHub")
    }
    else {
        $ErrorActionPreference = "Continue"
        $raw = (& gh api "repos/$Repo/branches/main/protection" 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $code = $LASTEXITCODE
        $ErrorActionPreference = "Stop"
        if ($code -ne 0 -and $raw -match $unavailablePattern) { $unavailable = $true }
        elseif ($code -ne 0) { $failures.Add("live branch protection not readable/enabled: $raw") }
        else { $live = $raw | ConvertFrom-Json @jsonArgs }
    }

    # D-QA-1 fallback: no GitHub protection is possible, so the file must name an open exception and the local
    # pre-push guard (scripts/scm/install-hooks.ps1 -ProtectMain) must be installed in this clone.
    if ($unavailable) {
        $fb = $j.unavailableFallback
        if ($null -eq $fb) { $failures.Add("live branch protection unavailable on this plan and $Protection declares no unavailableFallback") }
        elseif (-not $open.ContainsKey("$($fb.exceptionRef)")) {
            $failures.Add("live branch protection unavailable; unavailableFallback.exceptionRef '$($fb.exceptionRef)' is not an Open, unexpired entry of $Exceptions")
        }
        else {
            $hook = $HookFile
            if (-not $hook) {
                $hooksDir = (& git -C $RepoRoot rev-parse --git-path hooks 2>$null)
                if ($hooksDir -and -not [IO.Path]::IsPathRooted($hooksDir)) { $hooksDir = Join-Path $RepoRoot $hooksDir }
                $hook = Join-Path "$hooksDir" "pre-push"
            }
            else { $hook = Resolve-RepoPath $hook }
            $hookText = ""
            if (Test-Path $hook) { $hookText = Get-Content -Raw $hook }
            if ($hookText -notmatch 'PLAN-35 pre-push guard' -or $hookText -notmatch '-ProtectMain') {
                $failures.Add("live branch protection unavailable and the local guard is not installed at $hook (run: powershell -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1 -ProtectMain)")
            }
            else {
                Write-Host "SCM-001 live protection unavailable (private repository on the free plan); local guard installed: $hook (exception $($fb.exceptionRef))"
            }
        }
    }

    if ($null -ne $live) {
        function Compare-Live([string]$name, $want, $have) {
            if ("$want" -ne "$have") { $script:failures.Add("live $name is '$have', file says '$want'") }
        }
        $wantContexts = (@($j.required_status_checks.contexts) | Sort-Object) -join ","
        $haveContexts = (@($live.required_status_checks.contexts) | Sort-Object) -join ","
        Compare-Live "required_status_checks.contexts" $wantContexts $haveContexts
        Compare-Live "required_status_checks.strict" $j.required_status_checks.strict $live.required_status_checks.strict
        Compare-Live "enforce_admins" $j.enforce_admins $live.enforce_admins.enabled
        Compare-Live "required_signatures" $j.required_signatures $live.required_signatures.enabled
        Compare-Live "required_linear_history" $j.required_linear_history $live.required_linear_history.enabled
        Compare-Live "allow_force_pushes" $j.allow_force_pushes $live.allow_force_pushes.enabled
        Compare-Live "allow_deletions" $j.allow_deletions $live.allow_deletions.enabled
        Compare-Live "required_conversation_resolution" $j.required_conversation_resolution $live.required_conversation_resolution.enabled
        $lr = $live.required_pull_request_reviews
        if ($null -eq $lr) { $failures.Add("live protection does not require pull requests") }
        else {
            foreach ($k in @("required_approving_review_count", "require_code_owner_reviews", "require_last_push_approval", "dismiss_stale_reviews")) {
                Compare-Live "required_pull_request_reviews.$k" $j.required_pull_request_reviews.$k $lr.$k
            }
        }
        if ($failures.Count -eq 0) { Write-Host "SCM-001 live protection of main matches $Protection" }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "SCM-001 FAILED:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "SCM-001 OK: governance artifacts present; protection file within policy or under open dated exceptions."
exit 0
