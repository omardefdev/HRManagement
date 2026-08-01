# SRE-003: validate incident command docs, severity model, templates.

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

Need "docs/SRE_003_INCIDENT_COMMAND.md" | Out-Null
Need "docs/sre/incident-severity-model.json" | Out-Null
Need "docs/sre/status-page-templates.md" | Out-Null
Need "docs/sre/postmortem-template.md" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Sre003IncidentTests.cs" | Out-Null

$severityPath = Need "docs/sre/incident-severity-model.json"
if (Test-Path $severityPath) {
    try {
        $model = Get-Content -Raw $severityPath | ConvertFrom-Json
        if ($model.card -ne "SRE-003") { $failures.Add("severity model card must be SRE-003") }
        if (-not $model.severities -or $model.severities.Count -lt 4) { $failures.Add("need at least 4 severity levels") }
        if (-not $model.roles -or $model.roles.Count -lt 3) { $failures.Add("need at least 3 incident roles") }

        $sev1 = $model.severities | Where-Object { $_.level -eq "Sev-1" }
        if (-not $sev1) { $failures.Add("Sev-1 level required") }
        elseif ($sev1.sla.firstCommMinutes -gt 15) { $failures.Add("Sev-1 first comm must be <= 15 min") }
    } catch {
        $failures.Add("incident-severity-model.json invalid JSON: $_")
    }
}

$incDoc = Need "docs/SRE_003_INCIDENT_COMMAND.md"
if (Test-Path $incDoc) {
    $text = Get-Content -Raw $incDoc
    if ($text -notmatch "Incident Commander") { $failures.Add("doc must define Incident Commander role") }
    if ($text -notmatch "postmortem") { $failures.Add("doc must reference postmortem process") }
    if ($text -notmatch "blameless") { $failures.Add("doc must mandate blameless postmortems") }
}

$pmDoc = Need "docs/sre/postmortem-template.md"
if (Test-Path $pmDoc) {
    $text = Get-Content -Raw $pmDoc
    if ($text -notmatch "Action items") { $failures.Add("postmortem template must have action items section") }
    if ($text -notmatch "Root cause") { $failures.Add("postmortem template must have root cause section") }
}

$spDoc = Need "docs/sre/status-page-templates.md"
if (Test-Path $spDoc) {
    $text = Get-Content -Raw $spDoc
    if ($text -notmatch "Investigating") { $failures.Add("status page must have Investigating template") }
    if ($text -notmatch "Resolved") { $failures.Add("status page must have Resolved template") }
}

if ($failures.Count -gt 0) {
    Write-Host "SRE-003 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PASS: SRE-003 validation OK."
exit 0
