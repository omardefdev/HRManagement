#Requires -Version 5.1
# PLAN-66 (S3, D-QA-66-1): do the governance checks of the CI run in this run? They read the CI itself, so a pull request needs them
# only when it changes one of their inputs. Everything else runs them: a schedule, a manual run, a push that the dedupe guard did not
# skip. A list of changed files that cannot be read runs them too. Exit 0 either way; writes meta=run or meta=skip to GITHUB_OUTPUT.
# ASCII only; Windows PowerShell 5.1 and PowerShell 7.
param(
    [string]$RepoRoot = "",
    [string]$Scope = "tests/acceptance/ci-scope.json",
    [string]$Event = $env:GITHUB_EVENT_NAME,
    # Tests: the changed files, one per line, instead of the first parent of the merge commit.
    [string]$ChangedFile = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")
. (Join-Path $PSScriptRoot "acceptance-stage-lib.ps1")

$scopePath = if ([System.IO.Path]::IsPathRooted($Scope)) { $Scope } else { Join-Path $RepoRoot $Scope }
$scopeFile = Read-AcceptanceJson -Path $scopePath

function Get-ChangedPaths {
    if ($ChangedFile) {
        if (-not (Test-Path -LiteralPath $ChangedFile)) { return $null }
        return @(Get-Content -LiteralPath $ChangedFile | Where-Object { $_ })
    }
    # A pull request run checks out the test merge commit: its first parent is the base, so this is what the pull request changes.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $lines = @(& git -C $RepoRoot -c core.quotepath=false diff --name-only 'HEAD^1' HEAD 2>$null)
        if ($LASTEXITCODE -ne 0) { return $null }
        return $lines
    }
    finally { $ErrorActionPreference = $previous }
}

$decision = "run"
$why = ""
if ($Event -ne "pull_request") {
    $why = "event ${Event}: the governance checks always run"
}
else {
    $changed = Get-ChangedPaths
    if ($null -eq $changed) {
        $why = "the changed files could not be read, so they run"
    }
    else {
        $hit = @($changed | Where-Object { Test-AcceptanceTriggerPath -ScopeFile $scopeFile -Path $_ })
        if ($hit.Count -gt 0) { $why = "the pull request changes $($hit[0])$(if ($hit.Count -gt 1) { " and $($hit.Count - 1) more" }), which they read" }
        else {
            $decision = "skip"
            $why = "the pull request changes nothing they read ($(@($changed).Count) files)"
        }
    }
}

Write-Host "[meta] meta=${decision}: $why"
if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "meta=$decision" }
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "`nGovernance checks: **$decision**. $why." }
exit 0
