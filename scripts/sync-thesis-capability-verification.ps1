# Maps AC evidence Pass results to thesis capability verification status.
# Usage:
#   pwsh scripts/sync-thesis-capability-verification.ps1
#   pwsh scripts/sync-thesis-capability-verification.ps1 -Apply

param(
    [string]$EvidenceDir = $(if ($env:ACCEPTANCE_EVIDENCE_DIR) { $env:ACCEPTANCE_EVIDENCE_DIR } else { "acceptance-evidence" }),
    [switch]$Apply,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$evidencePath = if ([System.IO.Path]::IsPathRooted($EvidenceDir)) { $EvidenceDir } else { Join-Path $root $EvidenceDir }
$registerPath = Join-Path $root "docs/THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md"

# AC -> capability IDs with Pass evidence sufficient for "منفذ ومتحقق"
$acToCapabilities = @{
    "AC-01" = @("CAP-TEN-01"); "AC-02" = @("CAP-TEN-01"); "AC-03" = @("CAP-TEN-01")
    "AC-04" = @("CAP-TEN-01", "CAP-TEN-05"); "AC-05" = @("CAP-TEN-04"); "AC-06" = @("CAP-TEN-01")
    "AC-07" = @("CAP-ONB-01"); "AC-08" = @("CAP-TEN-03")
    "AC-09" = @("CAP-POL-02"); "AC-10" = @("CAP-POL-01", "CAP-POL-02")
    "AC-11" = @("CAP-WF-01", "CAP-WF-02"); "AC-12" = @("CAP-WF-01")
    "AC-13" = @("CAP-COD-01"); "AC-14" = @("CAP-ORG-02")
    "AC-15" = @("CAP-INT-01"); "AC-16" = @("CAP-INT-01"); "AC-17" = @("CAP-INT-01"); "AC-18" = @("CAP-INT-01")
    "AC-19" = @("CAP-TEN-02"); "AC-20" = @("CAP-TEN-05", "CAP-DB-01")
    "AC-21" = @("CAP-PAY-01"); "AC-22" = @("CAP-PAY-02")
    "AC-23" = @("CAP-PAY-01"); "AC-24" = @("CAP-PAY-04", "CAP-PAY-05"); "AC-25" = @("CAP-PAY-06")
    "AC-26" = @("CAP-PRV-01"); "AC-31" = @("CAP-TEN-01")
    "AC-27" = @("CAP-PRV-01"); "AC-28" = @("CAP-FILE-01"); "AC-29" = @("CAP-FILE-02"); "AC-30" = @("CAP-INT-01")
    "AC-32" = @("CAP-LOG-02"); "AC-33" = @("CAP-LOG-02")
    "AC-34" = @("CAP-OPS-01"); "AC-35" = @("CAP-OPS-01"); "AC-36" = @("CAP-PDF-01"); "AC-37" = @("CAP-INT-01")
}

$passedAcs = @{}
Get-ChildItem -Path $evidencePath -Directory -Filter "AC-*" -ErrorAction SilentlyContinue | ForEach-Object {
    $resultPath = Join-Path $_.FullName "result.json"
    if (-not (Test-Path $resultPath)) { return }
    $result = Get-Content $resultPath -Raw | ConvertFrom-Json
    if ($result.status -eq "Pass") {
        $passedAcs[$_.Name] = $true
    }
}

$capabilityReady = @{}
foreach ($ac in $passedAcs.Keys) {
    if (-not $acToCapabilities.ContainsKey($ac)) { continue }
    foreach ($cap in $acToCapabilities[$ac]) {
        if (-not $capabilityReady.ContainsKey($cap)) {
            $capabilityReady[$cap] = @()
        }
        $capabilityReady[$cap] += $ac
    }
}

Write-Host "Capabilities with Pass acceptance evidence:"
foreach ($cap in ($capabilityReady.Keys | Sort-Object)) {
    Write-Host "  $cap <= $($capabilityReady[$cap] -join ', ')"
}

if (-not (Test-Path $registerPath)) {
    Write-Warning "Register not found: $registerPath"
    exit 0
}

if (-not $Apply -and -not $WhatIf) {
    Write-Host "Dry run only. Use -Apply to update register, or -WhatIf to preview."
    exit 0
}

$content = Get-Content $registerPath -Raw -Encoding UTF8
$updated = $content
$changes = 0
foreach ($cap in $capabilityReady.Keys) {
    $pattern = "(\| $cap \|[^|]+\| )منفذ ولم يكتمل التحقق( \|)"
    if ($updated -match $pattern) {
        if ($WhatIf -or -not $Apply) {
            Write-Host "[Would update] $cap -> منفذ ومتحقق (evidence: $($capabilityReady[$cap] -join ', '))"
        } else {
            $updated = [regex]::Replace($updated, $pattern, "`$1منفذ ومتحقق`$2", 1)
            Write-Host "[Updated] $cap"
            $changes++
        }
    }
}

if ($Apply -and $changes -gt 0) {
    Set-Content -Path $registerPath -Value $updated -Encoding UTF8 -NoNewline
    Write-Host "Updated $changes capability row(s) in THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md"
} elseif ($Apply) {
    Write-Host "No register rows updated (already verified or pattern mismatch)."
}
