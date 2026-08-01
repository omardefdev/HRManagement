# OPS-006: validate cyber recovery docs, scenarios, and immutable artifact pipeline.

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



Need "docs/OPS_006_CYBER_RECOVERY.md" | Out-Null

Need "docs/OPS_006_CREDENTIAL_RESET.md" | Out-Null

Need "docs/OPS_006_IMMUTABLE_ARTIFACTS.md" | Out-Null

Need "docs/OPS_006_CLEANROOM_RUNBOOK.md" | Out-Null

Need "docs/ops006/immutable-artifact-pipeline.json" | Out-Null

Need "docs/ops006/assumed-breach-scenarios.json" | Out-Null

Need "src/Tests/HRManagement.UnitTests/Ops006CyberRecoveryTests.cs" | Out-Null



$pipelinePath = Need "docs/ops006/immutable-artifact-pipeline.json"

if (Test-Path $pipelinePath) {

    try {

        $pipeline = Get-Content -Raw $pipelinePath | ConvertFrom-Json

        if ($pipeline.card -ne "OPS-006") { $failures.Add("pipeline card must be OPS-006") }

        if (-not $pipeline.sources -or $pipeline.sources.Count -lt 4) { $failures.Add("pipeline needs at least 4 sources") }

        if (-not $pipeline.cleanRoomRules.rebuildInPlaceForbidden) { $failures.Add("rebuildInPlaceForbidden must be true") }

    } catch {

        $failures.Add("immutable-artifact-pipeline.json invalid JSON: $_")

    }

}



$scenariosPath = Need "docs/ops006/assumed-breach-scenarios.json"

if (Test-Path $scenariosPath) {

    try {

        $scenarios = Get-Content -Raw $scenariosPath | ConvertFrom-Json

        if ($scenarios.card -ne "OPS-006") { $failures.Add("scenarios card must be OPS-006") }

        if (-not $scenarios.scenarios -or $scenarios.scenarios.Count -lt 4) { $failures.Add("need at least 4 breach scenarios") }

    } catch {

        $failures.Add("assumed-breach-scenarios.json invalid JSON: $_")

    }

}



$credReset = Need "docs/OPS_006_CREDENTIAL_RESET.md"

if (Test-Path $credReset) {

    $text = Get-Content -Raw $credReset

    if ($text -notmatch "Never restore") { $failures.Add("credential reset must forbid restoring old secrets") }

    if ($text -notmatch "Keycloak") { $failures.Add("credential reset must cover Keycloak") }

}



$admission = Need "deploy/k8s/base/admission/require-signed-images.yaml"

if (Test-Path $admission) {

    $yaml = Get-Content -Raw $admission

    if ($yaml -notmatch "verifyImages|require-signed-images") { $failures.Add("admission policy must verify image signatures") }

}



if ($failures.Count -gt 0) {

    Write-Host "OPS-006 validation FAILED:"

    $failures | ForEach-Object { Write-Host "  - $_" }

    exit 1

}



Write-Host "PASS: OPS-006 validation OK."

exit 0

