#Requires -Version 5.1
# PLAN-66 (S2): a reusing run keeps the evidence of the run it reused: the acceptance-evidence artifact of that run becomes this
# run's, with a reuse.json pointer, so the artifact of the pull request run still exists for the evidence of record. The evidence
# must be for this code tree and from that run, or the check is red. ASCII only; Windows PowerShell 5.1 and PowerShell 7.
param(
    [string]$RepoRoot = "",
    [Parameter(Mandatory)][string]$Run,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Target = "artifacts/acceptance/run",
    # Tests: the folder that stands for the downloaded artifact, and a fixed code tree.
    [string]$FixtureDir = "",
    [string]$CodeTree = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")
. (Join-Path $PSScriptRoot "acceptance-stage-lib.ps1")

if ($Run -notmatch '^[0-9]+$') { throw "Not a run id: $Run" }
if ([string]::IsNullOrWhiteSpace($CodeTree)) { $CodeTree = Get-AcceptanceCodeTree -RepoRoot $RepoRoot -Commit "HEAD" }
$targetPath = if ([System.IO.Path]::IsPathRooted($Target)) { $Target } else { Join-Path $RepoRoot $Target }
if (Test-Path -LiteralPath $targetPath) { Remove-Item -LiteralPath $targetPath -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $targetPath) | Out-Null

if ($FixtureDir) {
    Copy-Item -LiteralPath $FixtureDir -Destination $targetPath -Recurse
}
else {
    gh run download $Run --repo $Repository --name acceptance-evidence --dir $targetPath
    if ($LASTEXITCODE -ne 0) { throw "Could not download the evidence of run $Run." }
}

$summary = Read-AcceptanceJson -Path (Join-Path (Join-Path $targetPath "acceptance-evidence") "summary.json")
if ($null -eq $summary) { throw "The evidence of run $Run has no summary.json." }
if ([string]$summary.codeTree -ne $CodeTree) { throw "The evidence of run $Run is for code tree $($summary.codeTree), this commit has $CodeTree." }
if ([string]$summary.ciRunId -ne $Run) { throw "The evidence downloaded from run $Run says it comes from run $($summary.ciRunId)." }

$server = if ($env:GITHUB_SERVER_URL) { $env:GITHUB_SERVER_URL } else { "https://github.com" }
$url = "$server/$Repository/actions/runs/$Run"
Write-AcceptanceJsonFile -Path (Join-Path $targetPath "reuse.json") -Value ([ordered]@{
        reusedFrom = $Run
        url = $url
        codeTree = $CodeTree
        run = $env:GITHUB_RUN_ID
        sha = $env:GITHUB_SHA
        reason = "D-QA-66-2: this commit leaves the code tree unchanged, so this run reuses the evidence of the green full run."
    })
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "### Evidence reused from run [$Run]($url)" }
Write-Host "acceptance-reuse-evidence: evidence of run $Run copied"
exit 0
