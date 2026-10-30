# PLAN-35: install the optional local pre-push guard (scripts/scm/pre-push-guard.ps1) as .git/hooks/pre-push.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1                 # scan only
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1 -ProtectMain    # + refuse pushes to main
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1 -Uninstall
#
# -ProtectMain is the D-QA-1 fallback when GitHub cannot protect main (private repository on the Free plan).
# An existing pre-push hook that this script did not write is kept as pre-push.bak.
# Windows PowerShell 5.1 and PowerShell 7. ASCII only.

param(
    [switch]$ProtectMain,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$hooksDir = (& git -C $repo rev-parse --git-path hooks).Trim()
if (-not [IO.Path]::IsPathRooted($hooksDir)) { $hooksDir = Join-Path $repo $hooksDir }
New-Item -ItemType Directory -Force $hooksDir | Out-Null
$hook = Join-Path $hooksDir "pre-push"
$marker = "# PLAN-35 pre-push guard"

if (Test-Path $hook) {
    $existing = Get-Content -Raw $hook
    if ($existing -notmatch [regex]::Escape($marker)) {
        Copy-Item $hook "$hook.bak" -Force
        Write-Host "Kept the existing hook as $hook.bak"
    }
}

if ($Uninstall) {
    Remove-Item $hook -ErrorAction SilentlyContinue
    Write-Host "Removed $hook"
    exit 0
}

$flag = ""
if ($ProtectMain) { $flag = " -ProtectMain" }
# git runs hooks with sh (Git for Windows ships one); stdin carries the pushed refs to the guard.
$content = @"
#!/bin/sh
$marker (scripts/scm/install-hooks.ps1)
ps=powershell
command -v powershell >/dev/null 2>&1 || ps=pwsh
exec "`$ps" -NoProfile -ExecutionPolicy Bypass -File scripts/scm/pre-push-guard.ps1$flag "`$1" "`$2"
"@
[IO.File]::WriteAllText($hook, ($content -replace "`r`n", "`n") + "`n")
Write-Host "Installed $hook (ProtectMain=$([bool]$ProtectMain))"
exit 0
