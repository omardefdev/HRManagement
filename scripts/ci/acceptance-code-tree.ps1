#Requires -Version 5.1
# PLAN-41 (P-2): prints the code tree of a commit. EvidenceRecorder runs this when the job did not pass ACCEPTANCE_CODE_TREE.
param(
    [string]$RepoRoot = "",
    [string]$Commit = "HEAD"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")
Write-Output (Get-AcceptanceCodeTree -RepoRoot $RepoRoot -Commit $Commit)
