# SUP-004: audit vendor contract registry — licenses, SLA, EOL, escalation.
param(
    [string]$RegistryPath = "docs/sup-contracts/vendor-contract-registry.json",
    [string]$RepoRoot = "",
    [int]$ExpiryWarningDays = 90
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$registryFull = Resolve-RepoPath $RegistryPath
if (-not (Test-Path $registryFull)) { Write-Error "registry not found: $registryFull" }

$r = Get-Content -Raw -Path $registryFull | ConvertFrom-Json
$failures = @()
$warnings = @()
$today = Get-Date

if ($r.card -ne "SUP-004") { $failures += "card must be SUP-004" }

$requiredVendors = @("postgres", "rabbitmq", "keycloak", "minio", "hosting", "support")
$vendorIds = @($r.vendors | ForEach-Object { $_.id })
foreach ($need in $requiredVendors) {
    if ($vendorIds -notcontains $need) { $failures += "missing vendor $need" }
}

foreach ($v in $r.vendors) {
    if (-not $v.sla) { $failures += "$($v.id) missing sla" }
    elseif ($v.sla.p1ResponseMinutes -gt 60) {
        $warnings += "$($v.id) p1ResponseMinutes > 60 for Tier-0 review"
    }

    if (-not $v.escalation.l1 -or -not $v.escalation.l2 -or -not $v.escalation.l3) {
        $failures += "$($v.id) incomplete escalation path"
    }

    if ($v.licenseExpiry) {
        $exp = [datetime]::Parse($v.licenseExpiry)
        $days = ($exp - $today).Days
        if ($days -lt 0) { $failures += "$($v.id) license expired" }
        elseif ($days -lt $ExpiryWarningDays) {
            $warnings += "$($v.id) license expires in $days days (< $ExpiryWarningDays)"
        }
    }

    if ($v.eol.eolDate) {
        $eol = [datetime]::Parse($v.eol.eolDate)
        if ($eol -lt $today) { $failures += "$($v.id) EOL date passed" }
    }
}

if (-not $r.auditRules.requireEscalationDrillQuarterly) {
    $failures += "requireEscalationDrillQuarterly must be true"
}

$docPath = Resolve-RepoPath "docs/SUP_004_ESCALATION_DRILL.md"
if (-not (Test-Path $docPath)) { $failures += "missing SUP_004_ESCALATION_DRILL.md" }

if ($failures.Count -gt 0) {
    Write-Host "FAIL: SUP-004 license audit ($($failures.Count) issue(s)):"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

if ($warnings.Count -gt 0) {
    Write-Host "WARN: $($warnings.Count) warning(s):"
    $warnings | ForEach-Object { Write-Host "  - $_" }
}

Write-Host "PASS: SUP-004 license audit"
exit 0
