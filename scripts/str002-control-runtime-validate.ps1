# STR-002: runtime/production control binding validate.
# Usage:
#   powershell -File scripts/str002-control-runtime-validate.ps1

param(
    [string]$CatalogPath = "docs/str-legal/control-catalog.json",
    [string]$WatchPath = "docs/str-legal/legislative-watch-stamp.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str002/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()
$catalogFull = Resolve-RepoPath $CatalogPath
if (-not (Test-Path $catalogFull)) { Write-Error "catalog not found: $catalogFull" }
$cat = Get-Content -Raw -Path $catalogFull | ConvertFrom-Json

if ($cat.card -ne "STR-002") { $failures += "card must be STR-002" }

$exceptionMap = @{}
foreach ($ex in @($cat.exceptions)) {
    $exceptionMap[[string]$ex.exceptionId] = $ex
}

foreach ($c in @($cat.controls)) {
    $status = [string]$c.status
    $rid = [string]$c.requirementId

    if ($status -eq "Implemented") {
        $blob = ("{0} {1}" -f $c.evidencePath, $c.testRef)
        if ($blob -match "(?i)LEGAL_TBD") {
            $failures += "$rid Implemented must not reference LEGAL_TBD"
        }

        $ev = [string]$c.evidencePath
        if ($ev) {
            $evRel = ($ev -split '#')[0]
            $evFull = Resolve-RepoPath $evRel
            if (-not (Test-Path $evFull)) {
                $failures += "$rid evidencePath missing: $evRel"
            }
        }

        $tr = [string]$c.testRef
        if ([string]::IsNullOrWhiteSpace($tr)) {
            $failures += "$rid Implemented missing testRef"
        }
        elseif ($tr -match '(?i)^docs[/\\]' -or $tr -match '(?i)^scripts[/\\]' -or $tr -match '(?i)\.(md|py|ps1)(#|$)') {
            $trRel = ($tr -split '#')[0]
            $trFull = Resolve-RepoPath $trRel
            if (-not (Test-Path $trFull)) {
                $failures += "$rid testRef path missing: $trRel"
            }
        }
        # else: test class / wildcard name — structural presence only
    }

    if ($status -eq "Partial") {
        $exId = [string]$c.exceptionId
        if ([string]::IsNullOrWhiteSpace($exId) -or -not $exceptionMap.ContainsKey($exId)) {
            $failures += "$rid Partial requires known exceptionId"
        }
        elseif ([string]$exceptionMap[$exId].status -ne "Active") {
            $failures += "$rid Partial exception $exId is not Active"
        }
    }

    if ([string]$c.confidence -eq "D") {
        $okD = ($status -eq "Blocked-Legal") -or (
            $c.exceptionId -and $exceptionMap.ContainsKey([string]$c.exceptionId) -and
            [string]$exceptionMap[[string]$c.exceptionId].status -eq "Active"
        )
        if (-not $okD) {
            $failures += "$rid confidence D requires Blocked-Legal or Active exception"
        }
    }
}

# appsettings LegalControl binding
$prodFiles = @(
    "src/Gateway/HRManagement.Gateway/appsettings.Production.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Production.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Production.json"
)

foreach ($rel in $prodFiles) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $failures += "missing $rel"
        continue
    }
    $cfg = Get-Content -Raw -Path $full | ConvertFrom-Json
    $lc = $cfg.LegalControl
    if ($null -eq $lc) {
        $failures += "$rel missing LegalControl section"
        continue
    }
    if ($lc.ReleaseProfile -eq "NationalClaim" -and $lc.AllowBlockedLegalNationalClaim -ne $true) {
        $blockedCritical = @($cat.controls | Where-Object {
            $_.status -eq "Blocked-Legal" -and
            @("payroll", "tax", "privacy", "signature_evidence", "sovereignty") -contains $_.domain
        })
        if ($blockedCritical.Count -gt 0) {
            $failures += "$rel NationalClaim without AllowBlockedLegalNationalClaim while critical Blocked-Legal remain"
        }
    }
    if ($lc.ReleaseProfile -ne "Interim" -and $lc.ReleaseProfile -ne "NationalClaim") {
        $failures += "$rel ReleaseProfile invalid: $($lc.ReleaseProfile)"
    }
    if ($lc.EnforceOnStartup -ne $true) {
        $failures += "$rel EnforceOnStartup must be true for Production"
    }
}

$watchFull = Resolve-RepoPath $WatchPath
if (-not (Test-Path $watchFull)) {
    $failures += "missing legislative-watch-stamp.json"
}
else {
    $watch = Get-Content -Raw -Path $watchFull | ConvertFrom-Json
    if (-not $watch.nextDue) {
        $failures += "watch stamp missing nextDue"
    }
    else {
        $nextDue = [datetime]::Parse([string]$watch.nextDue).ToUniversalTime().Date
        if ($nextDue -lt [datetime]::UtcNow.Date) {
            $failures += "legislative watch overdue: nextDue=$($watch.nextDue)"
        }
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-002"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    controlCount = @($cat.controls).Count
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "runtime-validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: STR-002 control runtime validate"
exit 0
