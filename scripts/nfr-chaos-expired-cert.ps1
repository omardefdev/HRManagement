# NFR-002: emit expired cert stub for TLS failure drills (does not install into production trust).
param(
    [string]$OutDir = 'chaos/certs',
    [string]$ResultsRoot = 'chaos/results'
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $ResultsRoot "$stamp-expired-cert"
New-Item -ItemType Directory -Force -Path $OutDir, $runDir | Out-Null

$certPath = Join-Path $OutDir 'nfr002-expired.pem'
# Placeholder PEM marker — real openssl generation when openssl available
$openssl = Get-Command openssl -ErrorAction SilentlyContinue
if ($openssl) {
    $key = Join-Path $OutDir 'nfr002-expired.key'
    & openssl req -x509 -newkey rsa:2048 -keyout $key -out $certPath -days -1 -nodes -subj '/CN=nfr002-expired' 2>$null
    if (-not (Test-Path $certPath)) {
        # some openssl reject negative days — create 1-day then note as "treat as expired in drill"
        & openssl req -x509 -newkey rsa:2048 -keyout $key -out $certPath -days 1 -nodes -subj '/CN=nfr002-lab' 2>$null
        $note = 'Generated 1-day cert; for drill set system clock forward or replace with expired artifact.'
    }
    else { $note = 'Generated expired/near-expired cert via openssl.' }
}
else {
    @"
-----BEGIN CERTIFICATE-----
NFR002-EXPIRED-CERT-STUB-NOT-FOR-TRUST
-----END CERTIFICATE-----
"@ | Set-Content $certPath -Encoding ASCII
    $note = 'openssl missing — stub file only. Use a real expired cert in staging TLS terminator for the drill.'
}

@{
    status = 'ready'
    certPath = $certPath
    expect = 'TLS handshake fails visibly; no silent fallback to cleartext for confidential routes'
    note = $note
} | ConvertTo-Json | Set-Content (Join-Path $runDir 'summary.json') -Encoding UTF8
$note | Set-Content (Join-Path $runDir 'notes.md') -Encoding UTF8
Write-Host "Expired-cert drill artifact → $certPath ($runDir)"
