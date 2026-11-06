#Requires -Version 5.1
# PLAN-66 (S2, D-QA-66-2): may this run reuse an earlier green full run instead of doing the work again? And it records what this
# run is, in the run-identity artifact, so a later run can ask the same question about it.
#
# It reuses only the newest successful run of this workflow, for the same pull request, from this repository, of scope full, every
# job of which succeeded, with the same code tree and the same inputs of the governance checks (docs/qa/** is outside the code
# tree), and whose evidence artifact is still there. A reusing run points at the original full run, so a chain never grows.
#
# Never for: a fork, a push, a manual or scheduled run, a narrowed run, a failed, cancelled or partly skipped run, another tree. Any
# doubt, an API error above all, means the run does everything: this script exits 0 and says reuse=false.
#
# Writes reuse, reused-run and reused-url to GITHUB_OUTPUT, a line to the job summary, and -IdentityFile. ASCII only; Windows
# PowerShell 5.1 and PowerShell 7.
param(
    [string]$RepoRoot = "",
    [string]$Scope = "tests/acceptance/ci-scope.json",
    [string]$Event = $env:GITHUB_EVENT_NAME,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$RunId = $env:GITHUB_RUN_ID,
    [string]$Sha = $env:GITHUB_SHA,
    [string]$PullRequest = $env:PR_NUMBER,
    [string]$HeadRepository = $env:PR_HEAD_REPOSITORY,
    [string]$HeadSha = $env:PR_HEAD_SHA,
    [string]$HeadRef = $env:PR_HEAD_REF,
    [string]$Classes = $env:ACCEPTANCE_CLASSES,
    [string]$SimulateMissing = $env:ACCEPTANCE_SIMULATE_MISSING,
    [string]$WorkflowFile = "ci.yml",
    [string]$IdentityFile = "artifacts/ci/run-identity.json",
    # Tests: fixed values, and the answers of GitHub as files of this folder instead of API calls.
    [string]$CodeTree = "",
    [string]$MetaInputs = "",
    [string]$FixtureDir = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")
. (Join-Path $PSScriptRoot "acceptance-stage-lib.ps1")

$server = if ($env:GITHUB_SERVER_URL) { $env:GITHUB_SERVER_URL } else { "https://github.com" }
$scopePath = if ([System.IO.Path]::IsPathRooted($Scope)) { $Scope } else { Join-Path $RepoRoot $Scope }
$scopeFile = Read-AcceptanceJson -Path $scopePath
if ([string]::IsNullOrWhiteSpace($CodeTree)) { $CodeTree = Get-AcceptanceCodeTree -RepoRoot $RepoRoot -Commit "HEAD" }
if ([string]::IsNullOrWhiteSpace($MetaInputs)) { $MetaInputs = Get-AcceptanceInputsHash -RepoRoot $RepoRoot -ScopeFile $scopeFile }
$narrowed = -not [string]::IsNullOrWhiteSpace($Classes) -or -not [string]::IsNullOrWhiteSpace($SimulateMissing)

function Read-Fixture([string]$Name) {
    return Read-AcceptanceJson -Path (Join-Path $FixtureDir $Name)
}

function Invoke-GhJson([string]$Path) {
    $text = (& gh api $Path) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "gh api $Path failed with exit code $LASTEXITCODE" }
    return $text | ConvertFrom-Json
}

function Get-CandidateRuns {
    if ($FixtureDir) { return @((Read-Fixture "runs.json").workflow_runs) }
    $branch = [System.Uri]::EscapeDataString($HeadRef)
    return @((Invoke-GhJson "repos/$Repository/actions/workflows/$WorkflowFile/runs?event=pull_request&status=success&branch=$branch&per_page=30").workflow_runs)
}

function Get-OneRun([string]$Id) {
    if ($FixtureDir) { return Read-Fixture "run-$Id.json" }
    return Invoke-GhJson "repos/$Repository/actions/runs/$Id"
}

function Get-RunJobs([string]$Id) {
    if ($FixtureDir) { return @((Read-Fixture "jobs-$Id.json").jobs) }
    return @((Invoke-GhJson "repos/$Repository/actions/runs/$Id/jobs?per_page=100").jobs)
}

function Get-RunIdentity([string]$Id) {
    if ($FixtureDir) { return Read-Fixture "identity-$Id.json" }
    $folder = Join-Path ([System.IO.Path]::GetTempPath()) ("run-identity-" + [guid]::NewGuid().ToString("N"))
    try {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & gh run download $Id --repo $Repository --name run-identity --dir $folder 2>$null
        $ErrorActionPreference = $previous
        if ($LASTEXITCODE -ne 0) { return $null }
        return Read-AcceptanceJson -Path (Join-Path $folder "run-identity.json")
    }
    finally {
        if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    }
}

function Test-EvidenceArtifact([string]$Id) {
    $artifacts = if ($FixtureDir) { @((Read-Fixture "artifacts-$Id.json").artifacts) }
    else { @((Invoke-GhJson "repos/$Repository/actions/runs/$Id/artifacts?name=acceptance-evidence").artifacts) }
    return @($artifacts | Where-Object { $null -ne $_ -and [string]$_.name -eq "acceptance-evidence" -and -not [bool]$_.expired }).Count -gt 0
}

# Why this run cannot be reused; nothing when it can. It speaks of one run; the chain is the caller's.
function Get-RefusalOfRun {
    param([Parameter(Mandatory)]$Run, $Identity)
    $id = [string]$Run.id
    if ([string]$Run.status -ne "completed" -or [string]$Run.conclusion -ne "success") { return "run $id is $($Run.status)/$($Run.conclusion)" }
    if ([string]$Run.event -ne "pull_request") { return "run $id is a $($Run.event) run" }
    if ([string]$Run.head_repository.full_name -ne $Repository) { return "run $id comes from $($Run.head_repository.full_name)" }
    $numbers = @(@($Run.pull_requests) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_.number })
    if ($PullRequest -notin $numbers) { return "run $id does not belong to pull request #$PullRequest" }

    $jobs = @(Get-RunJobs $id)
    if ($jobs.Count -eq 0) { return "run $id lists no job" }
    $notGreen = @($jobs | Where-Object { [string]$_.conclusion -ne "success" })
    if ($notGreen.Count -gt 0) { return "run $id has a job that did not succeed: $($notGreen[0].name) is $($notGreen[0].conclusion)" }

    if ($null -eq $Identity) { return "run $id has no run identity" }
    if ([string]$Identity.repository -ne $Repository) { return "the identity of run $id names $($Identity.repository)" }
    if ([string]$Identity.scope -ne "full") { return "run $id has scope $($Identity.scope)" }
    if ([string]$Identity.codeTree -ne $CodeTree) { return "run $id tested another code tree" }
    if ([string]$Identity.metaInputs -ne $MetaInputs) { return "run $id saw other inputs of the governance checks" }
    return ""
}

function Find-ReusableRun {
    $refusals = New-Object System.Collections.Generic.List[string]
    $candidates = @(Get-CandidateRuns | Where-Object { $null -ne $_ -and [string]$_.id -ne $RunId } |
        Sort-Object -Property { ConvertTo-AcceptanceUtc $_.created_at } -Descending | Select-Object -First 10)
    foreach ($run in $candidates) {
        $id = [string]$run.id
        $identity = Get-RunIdentity $id
        $refusal = Get-RefusalOfRun -Run $run -Identity $identity
        if ($refusal) { [void]$refusals.Add($refusal); continue }

        # A reusing run stands for the run it reused: the proof is the original, and that one must still hold.
        $origin = $id
        $source = [string](Get-AcceptanceProperty $identity "reusedFrom")
        if ($source) {
            $origin = $source
            $originIdentity = Get-RunIdentity $origin
            $refusal = Get-RefusalOfRun -Run (Get-OneRun $origin) -Identity $originIdentity
            if (-not $refusal -and [string](Get-AcceptanceProperty $originIdentity "reusedFrom")) { $refusal = "run $origin is not an original full run" }
            if ($refusal) { [void]$refusals.Add($refusal); continue }
        }
        if (-not (Test-EvidenceArtifact $origin)) { [void]$refusals.Add("run $origin has no evidence artifact left"); continue }
        return [pscustomobject]@{ Id = $origin; Why = "run $origin tested this code tree for pull request #$PullRequest" }
    }
    $why = if ($refusals.Count -gt 0) { $refusals[0] } else { "no successful run of pull request #$PullRequest yet" }
    return [pscustomobject]@{ Id = ""; Why = $why }
}

$found = [pscustomobject]@{ Id = ""; Why = "" }
try {
    if ($Event -ne "pull_request") { $found.Why = "event ${Event}: only a pull request run reuses" }
    elseif ($narrowed) { $found.Why = "a narrowed run never reuses" }
    elseif ($PullRequest -notmatch '^[0-9]+$') { $found.Why = "no pull request number" }
    elseif ([string]::IsNullOrWhiteSpace($Repository) -or $HeadRepository -ne $Repository) { $found.Why = "the head of pull request #$PullRequest is in $HeadRepository, not in $Repository" }
    else { $found = Find-ReusableRun }
}
catch {
    $found = [pscustomobject]@{ Id = ""; Why = "the check failed, so everything runs: $($_.Exception.Message)" }
}

$reuse = -not [string]::IsNullOrWhiteSpace($found.Id)
$url = if ($reuse) { "$server/$Repository/actions/runs/$($found.Id)" } else { "" }
$scopeName = if ($narrowed) { "narrowed" } else { "full" }
$identityPath = if ([System.IO.Path]::IsPathRooted($IdentityFile)) { $IdentityFile } else { Join-Path $RepoRoot $IdentityFile }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $identityPath) | Out-Null
Write-AcceptanceJsonFile -Path $identityPath -Value ([ordered]@{
        schema = "run-identity-v1"
        repository = $Repository
        runId = $RunId
        event = $Event
        pullRequest = $PullRequest
        codeTree = $CodeTree
        metaInputs = $MetaInputs
        headSha = $Sha
        pullRequestHeadSha = $HeadSha
        scope = $scopeName
        reusedFrom = $found.Id
    })

$answer = if ($reuse) { "true" } else { "false" }
Write-Host "[reuse] reuse=$answer scope=${scopeName}: $($found.Why)"
if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "reuse=$answer"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "reused-run=$($found.Id)"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "reused-url=$url"
}
if ($env:GITHUB_STEP_SUMMARY) {
    $title = if ($reuse) { "### Reusing run [$($found.Id)]($url)" } else { "### Full run" }
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "$title`n`n$($found.Why)"
}
exit 0
