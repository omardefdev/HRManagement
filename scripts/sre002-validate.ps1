# SRE-002: validate ORR checklist, runbooks, and docs.

param(
    [string]$RepoRoot = ""
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

Need "docs/SRE_002_ORR.md" | Out-Null
Need "docs/sre/orr-checklist-template.json" | Out-Null
Need "docs/runbooks/db-connection-failure.md" | Out-Null
Need "docs/runbooks/queue-backup.md" | Out-Null
Need "docs/runbooks/oom-kill.md" | Out-Null
Need "docs/runbooks/cert-expiry.md" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Sre002OrrTests.cs" | Out-Null

$checklistPath = Need "docs/sre/orr-checklist-template.json"
if (Test-Path $checklistPath) {
    try {
        $checklist = Get-Content -Raw $checklistPath | ConvertFrom-Json
        if ($checklist.card -ne "SRE-002") { $failures.Add("orr-checklist card must be SRE-002") }
        if (-not $checklist.sections -or $checklist.sections.Count -lt 5) { $failures.Add("checklist needs at least 5 sections") }
        $allItems = $checklist.sections | ForEach-Object { $_.items } | Measure-Object
        if ($allItems.Count -lt 10) { $failures.Add("checklist needs at least 10 items total") }
    } catch {
        $failures.Add("orr-checklist-template.json invalid JSON: $_")
    }
}

$orrDoc = Need "docs/SRE_002_ORR.md"
if (Test-Path $orrDoc) {
    $text = Get-Content -Raw $orrDoc
    if ($text -notmatch "Tier-0") { $failures.Add("ORR doc must reference Tier-0") }
    if ($text -notmatch "runbook") { $failures.Add("ORR doc must reference runbooks") }
}

foreach ($rb in @("db-connection-failure", "queue-backup", "oom-kill", "cert-expiry")) {
    $rbPath = Join-Path $RepoRoot "docs/runbooks/$rb.md"
    if (Test-Path $rbPath) {
        $rbText = Get-Content -Raw $rbPath
        if ($rbText -notmatch "SRE-002") { $failures.Add("$rb runbook must reference SRE-002") }
        if ($rbText -notmatch "Severity") { $failures.Add("$rb runbook must define severity") }
        if ($rbText -notmatch "Escalation") { $failures.Add("$rb runbook must have escalation section") }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "SRE-002 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PASS: SRE-002 validation OK."
exit 0
