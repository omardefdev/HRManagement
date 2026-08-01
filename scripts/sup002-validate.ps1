# SUP-002: validate service desk docs, SLA/OLA matrix, knowledge base structure.

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

Need "docs/SUP_002_SERVICE_DESK.md" | Out-Null
Need "docs/sup/sla-ola-matrix.json" | Out-Null
Need "docs/sup/knowledge-base-structure.md" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Sup002ServiceDeskTests.cs" | Out-Null

$matrixPath = Need "docs/sup/sla-ola-matrix.json"
if (Test-Path $matrixPath) {
    try {
        $matrix = Get-Content -Raw $matrixPath | ConvertFrom-Json
        if ($matrix.card -ne "SUP-002") { $failures.Add("SLA matrix card must be SUP-002") }
        if (-not $matrix.priorities -or $matrix.priorities.Count -lt 4) { $failures.Add("need at least 4 priority levels") }

        $p1 = $matrix.priorities | Where-Object { $_.id -eq "P1" }
        if (-not $p1) { $failures.Add("P1 priority required") }
        elseif ($p1.sla.responseMinutes -gt 15) { $failures.Add("P1 response must be <= 15 min") }

        if (-not $matrix.supportTiers -or $matrix.supportTiers.Count -lt 3) { $failures.Add("need at least 3 support tiers") }
    } catch {
        $failures.Add("sla-ola-matrix.json invalid JSON: $_")
    }
}

$deskDoc = Need "docs/SUP_002_SERVICE_DESK.md"
if (Test-Path $deskDoc) {
    $text = Get-Content -Raw $deskDoc
    if ($text -notmatch "SLA") { $failures.Add("doc must reference SLA") }
    if ($text -notmatch "knowledge") { $failures.Add("doc must reference knowledge management") }
    if ($text -notmatch "L1") { $failures.Add("doc must define L1 tier") }
}

$kbDoc = Need "docs/sup/knowledge-base-structure.md"
if (Test-Path $kbDoc) {
    $text = Get-Content -Raw $kbDoc
    if ($text -notmatch "top.20") { $failures.Add("KB structure must reference top 20 coverage") }
    if ($text -notmatch "template") { $failures.Add("KB structure must include article template") }
}

if ($failures.Count -gt 0) {
    Write-Host "SUP-002 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PASS: SUP-002 validation OK."
exit 0
