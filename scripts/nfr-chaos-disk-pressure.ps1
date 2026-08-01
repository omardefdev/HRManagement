# NFR-002: simulate disk pressure on audit overflow path (lab).
# Fills a temp file until free space check fails or MaxMb reached — does NOT wipe disks.
param(
    [string]$Path = $(if ($env:HR_AUDIT_OVERFLOW_PATH) { $env:HR_AUDIT_OVERFLOW_PATH } else { Join-Path $env:TEMP 'hrm-chaos-disk' }),
    [int]$MaxMb = 64,
    [string]$ResultsRoot = 'chaos/results'
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $ResultsRoot "$stamp-disk-pressure"
New-Item -ItemType Directory -Force -Path $Path, $runDir | Out-Null

$fill = Join-Path $Path "chaos-fill-$stamp.bin"
$bytes = [Math]::Min($MaxMb, 64) * 1MB
$buffer = New-Object byte[] (1MB)
$rnd = [Random]::new(42)
$written = 0
try {
    $fs = [IO.File]::Open($fill, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        while ($written -lt $bytes) {
            $rnd.NextBytes($buffer)
            $fs.Write($buffer, 0, $buffer.Length)
            $written += $buffer.Length
        }
    }
    finally { $fs.Dispose() }
    $status = 'filled'
}
catch {
    $status = "failed:$($_.Exception.Message)"
}

@{
    atUtc = (Get-Date).ToUniversalTime().ToString('o')
    path = $Path
    fillFile = $fill
    writtenBytes = $written
    status = $status
    expect = 'Audit overflow/DLQ must fail loudly or spill — not silent drop (AUD-001/NFR-002)'
} | ConvertTo-Json | Set-Content (Join-Path $runDir 'summary.json') -Encoding UTF8

Write-Host "Disk pressure artifact $fill ($written bytes). Remove after drill. Results → $runDir"
Write-Host "Cleanup: Remove-Item '$fill' -Force"
