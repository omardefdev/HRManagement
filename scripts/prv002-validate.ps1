# PRV-002: validate individual rights / DSAR deliverables.
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

Need "docs/PRV_002_INDIVIDUAL_RIGHTS.md" | Out-Null
Need "docs/prv/dsar-workflow.json" | Out-Null
Need "src/Shared/HRManagement.SharedKernel/Privacy/DsarRequest.cs" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/Privacy/IDsarService.cs" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Prv002DsarTests.cs" | Out-Null

$workflowPath = Need "docs/prv/dsar-workflow.json"
if (Test-Path $workflowPath) {
    $wf = Get-Content -Raw $workflowPath | ConvertFrom-Json
    if ($wf.card -ne "PRV-002") { $failures.Add("workflow card must be PRV-002") }

    $types = @($wf.requestTypes | ForEach-Object { $_.type })
    foreach ($need in @("Access", "Correction", "Restriction", "Deletion", "Objection", "Appeal")) {
        if ($types -notcontains $need) { $failures.Add("workflow missing request type $need") }
    }

    if ($wf.noDirectDbEdit -ne $true) { $failures.Add("noDirectDbEdit must be true") }
    if ($wf.appealIndependent -ne $true) { $failures.Add("appealIndependent must be true") }
}

$entityPath = Need "src/Shared/HRManagement.SharedKernel/Privacy/DsarRequest.cs"
if (Test-Path $entityPath) {
    $entity = Get-Content -Raw $entityPath
    foreach ($prop in @("TenantId", "SubjectId", "RequestType", "Status", "DeadlineUtc")) {
        if ($entity -notmatch $prop) { $failures.Add("DsarRequest missing property $prop") }
    }
    if ($entity -notmatch "DsarWorkflowRules") { $failures.Add("DsarWorkflowRules required") }
}

$svcPath = Need "src/Shared/HRManagement.ServiceDefaults/Privacy/IDsarService.cs"
if (Test-Path $svcPath) {
    $svc = Get-Content -Raw $svcPath
    if ($svc -notmatch "interface IDsarService") { $failures.Add("IDsarService interface required") }
    if ($svc -notmatch "class DsarService") { $failures.Add("DsarService stub required") }
}

if ($failures.Count -gt 0) {
    Write-Host "PRV-002 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PRV-002 validation OK."
exit 0
