# FILE-001: orphan / reconcile scan against a running File.Storage instance.
# Usage:
#   pwsh scripts/reconcile-file-orphans.ps1 -BaseUrl http://localhost:5xxx -Token <jwt>
# Optional: -DryRun  (persist=false)

param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [Parameter(Mandatory = $true)]
    [string] $Token,

    [switch] $DryRun,

    [switch] $ProcessPurge
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }
$persist = if ($DryRun) { "false" } else { "true" }

Write-Host "Reconciling reference counts..."
$ref = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/files/admin/reconcile-ref-counts" -Headers $headers
Write-Host "  repaired=$($ref.repaired)"

Write-Host "Running orphan scan (persist=$persist)..."
$scan = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/files/admin/orphan-scan?persist=$persist" -Headers $headers
Write-Host ("  DbWithoutObject={0} ObjectWithoutDb={1} DeletedWithObject={2} RefCountDriftRepaired={3} Unresolved={4}" -f `
    $scan.dbWithoutObject, $scan.objectWithoutDb, $scan.deletedWithObject, $scan.refCountDriftRepaired, $scan.unresolvedExceptions)

if ($ProcessPurge) {
    Write-Host "Processing purge queue..."
    $purge = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/files/admin/process-purge" -Headers $headers
    Write-Host "  succeeded=$($purge.succeeded)"
}

$metrics = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/files/metrics" -Headers $headers
Write-Host ("Metrics: pendingPurge={0} deadPurgeWorkItems={1}" -f $metrics.pendingPurge, $metrics.deadPurgeWorkItems)

if ($scan.unresolvedExceptions -gt 0 -and -not $DryRun) {
    Write-Warning "Unresolved reconcile exceptions remain. Approve via FileReconcileExceptions or fix storage/DB drift."
    exit 2
}

Write-Host "OK"
