# PLAN-35 (SCM-001, D-QA-1/2/16): apply docs/scm-governance/branch-protection-main.json to main on GitHub, then compare
# the live settings with the file (scm001-branch-protection-validate.ps1 -CheckLiveGitHub). Run by the owner (admin).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1 -DryRun   # print the API body only
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1 -Remove   # delete main's protection
#
# -Remove exists for the first push only (the protection that predates PLAN-35 requires signed commits, two reviews and
# linear history, which the unsigned history with merge commits cannot meet); re-apply right after the push.
# Windows PowerShell 5.1 and PowerShell 7. ASCII only.

param(
    [string]$Repo = "omardefdev/HRManagement",
    [string]$Branch = "main",
    [string]$Protection = "docs/scm-governance/branch-protection-main.json",
    [switch]$DryRun,
    [switch]$Remove
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$file = Join-Path $root ($Protection -replace '/', [IO.Path]::DirectorySeparatorChar)
$endpoint = "repos/$Repo/branches/$Branch/protection"

function Invoke-Gh([string[]]$GhArgs) {
    $ErrorActionPreference = "Continue"
    $out = (& gh @GhArgs 2>&1 | ForEach-Object { "$_" }) -join "`n"
    $code = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($code -ne 0) { throw "gh $($GhArgs -join ' ') failed (exit $code): $out" }
    return $out
}

if (-not $DryRun) {
    $perm = Invoke-Gh @("repo", "view", $Repo, "--json", "viewerPermission", "--jq", ".viewerPermission")
    if ($perm.Trim() -ne "ADMIN") { throw "the gh account has '$($perm.Trim())' on $Repo; ADMIN is needed (gh auth switch / gh auth login)" }
}

if ($Remove) {
    if ($DryRun) { Write-Host "DryRun: would DELETE $endpoint"; exit 0 }
    Invoke-Gh @("api", "-X", "DELETE", $endpoint) | Out-Null
    Write-Host "Removed the protection of $Branch on $Repo. Re-apply it after the push: powershell -File scripts/scm/apply-branch-protection.ps1"
    exit 0
}

$j = Get-Content -Raw $file | ConvertFrom-Json
$rev = $j.required_pull_request_reviews
$body = [ordered]@{
    required_status_checks           = [ordered]@{ strict = [bool]$j.required_status_checks.strict; contexts = @($j.required_status_checks.contexts) }
    enforce_admins                   = [bool]$j.enforce_admins
    required_pull_request_reviews    = [ordered]@{
        dismiss_stale_reviews           = [bool]$rev.dismiss_stale_reviews
        require_code_owner_reviews      = [bool]$rev.require_code_owner_reviews
        required_approving_review_count = [int]$rev.required_approving_review_count
        require_last_push_approval      = [bool]$rev.require_last_push_approval
    }
    restrictions                     = $null
    required_linear_history          = [bool]$j.required_linear_history
    allow_force_pushes               = [bool]$j.allow_force_pushes
    allow_deletions                  = [bool]$j.allow_deletions
    block_creations                  = [bool]$j.block_creations
    required_conversation_resolution = [bool]$j.required_conversation_resolution
    lock_branch                      = [bool]$j.lock_branch
    allow_fork_syncing               = [bool]$j.allow_fork_syncing
}
$json = $body | ConvertTo-Json -Depth 5
if ($DryRun) {
    Write-Host "DryRun: PUT $endpoint"
    Write-Host $json
    Write-Host "required_signatures: $([bool]$j.required_signatures) (separate endpoint)"
    exit 0
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("hrm-protection-{0}.json" -f [guid]::NewGuid().ToString("N"))
[IO.File]::WriteAllText($tmp, $json, (New-Object System.Text.UTF8Encoding $false))
try {
    Invoke-Gh @("api", "-X", "PUT", $endpoint, "--input", $tmp) | Out-Null
}
finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

# Signed commits have their own endpoint.
if ([bool]$j.required_signatures) {
    Invoke-Gh @("api", "-X", "POST", "$endpoint/required_signatures", "-H", "Accept: application/vnd.github+json") | Out-Null
}
else {
    $ErrorActionPreference = "Continue"
    & gh api -X DELETE "$endpoint/required_signatures" *> $null
    $ErrorActionPreference = "Stop"
}
Write-Host "Applied $Protection to $Branch on $Repo."

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts/scm001-branch-protection-validate.ps1") -CheckLiveGitHub -Repo $Repo
exit $LASTEXITCODE
