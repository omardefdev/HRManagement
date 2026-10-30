# PLAN-35 (D-QA-1 fallback, G-QA-01): local pre-push guard. Installed as .git/hooks/pre-push by
# scripts/scm/install-hooks.ps1; git passes "<remote name> <remote url>" as arguments and one line per pushed ref on
# stdin: "<local ref> <local sha> <remote ref> <remote sha>".
#
# For every pushed ref it runs the fast checks of scripts/plan035-prepush.ps1 (gitleaks and forbidden files) on the
# commits that are new to the remote, and it refuses a direct push to main when -ProtectMain is set (the local
# replacement for branch protection when GitHub cannot protect the branch). Exit 1 = git aborts the push.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/pre-push-guard.ps1 origin <url> < refs.txt
#   powershell -File scripts/scm/pre-push-guard.ps1 -SelfTestRefs "refs/heads/x <sha> refs/heads/x 0000..."   # no stdin
# Windows PowerShell 5.1 and PowerShell 7. ASCII only.

param(
    [Parameter(Position = 0)][string]$RemoteName = "origin",
    [Parameter(Position = 1)][string]$RemoteUrl = "",
    [switch]$ProtectMain,
    [string]$DefaultBase = "origin/main",
    [string[]]$SelfTestRefs = @()
)

$ErrorActionPreference = "Continue"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$zero = "0000000000000000000000000000000000000000"

$lines = @($SelfTestRefs)
if ($lines.Count -eq 0) {
    $lines = @()
    while ($null -ne ($l = [Console]::In.ReadLine())) { if ($l.Trim()) { $lines += $l } }
}

$failed = $false
foreach ($line in $lines) {
    $parts = $line.Trim() -split '\s+'
    if ($parts.Count -lt 4) { continue }
    $localRef = $parts[0]; $localSha = $parts[1]; $remoteRef = $parts[2]; $remoteSha = $parts[3]
    if ($localSha -eq $zero) { Write-Host "pre-push guard: deleting $remoteRef - nothing to scan"; continue }

    if ($ProtectMain -and $remoteRef -eq "refs/heads/main") {
        Write-Host "pre-push guard: REFUSED direct push to main (D-QA-1 local guard). Open a PR from a branch instead."
        $failed = $true
        continue
    }

    $base = $remoteSha
    if ($base -eq $zero) { $base = $DefaultBase }
    else {
        & git -C $repo cat-file -e "$base^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) { $base = $DefaultBase }
    }

    Write-Host "pre-push guard: $localRef -> $RemoteName $remoteRef ($base..$localSha)"
    $out = Join-Path $repo "artifacts/plan35/pre-push-guard"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "scripts/plan035-prepush.ps1") `
        -Base $base -Head $localSha -Only "gitleaks,forbidden" -Out $out -Branch ($remoteRef -replace '^refs/heads/', '')
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}

if ($failed) {
    Write-Host "pre-push guard: push aborted (report: artifacts/plan35/pre-push-guard/report.md)"
    exit 1
}
exit 0
