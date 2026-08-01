# SUP-004: validate vendor contract registry, escalation drill, and unit tests.

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



Need "docs/sup-contracts/vendor-contract-registry.json" | Out-Null

Need "docs/SUP_004_ESCALATION_DRILL.md" | Out-Null

Need "scripts/sup004-license-audit.ps1" | Out-Null

Need "src/Tests/HRManagement.UnitTests/Sup004ContractTests.cs" | Out-Null



$registryPath = Need "docs/sup-contracts/vendor-contract-registry.json"

if (Test-Path $registryPath) {

    try {

        $json = Get-Content -Raw $registryPath | ConvertFrom-Json

        if ($json.card -ne "SUP-004") { $failures.Add("registry card must be SUP-004") }

        if (-not $json.vendors -or $json.vendors.Count -lt 5) { $failures.Add("registry needs at least 5 vendors") }

    } catch {

        $failures.Add("vendor-contract-registry.json invalid JSON: $_")

    }

}



$drill = Need "docs/SUP_004_ESCALATION_DRILL.md"

if (Test-Path $drill) {

    $text = Get-Content -Raw $drill

    if ($text -notmatch "Quarterly") { $failures.Add("escalation drill must define quarterly frequency") }

    if ($text -notmatch "Tier-0") { $failures.Add("escalation drill must reference Tier-0") }

}



& (Join-Path $RepoRoot "scripts/sup004-license-audit.ps1") -RepoRoot $RepoRoot

if ($LASTEXITCODE -ne 0) {

    $failures.Add("sup004-license-audit.ps1 failed")

}



if ($failures.Count -gt 0) {

    Write-Host "SUP-004 validation FAILED:"

    $failures | ForEach-Object { Write-Host "  - $_" }

    exit 1

}



Write-Host "PASS: SUP-004 validation OK."

exit 0

