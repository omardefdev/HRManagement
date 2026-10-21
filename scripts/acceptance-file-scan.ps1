# PLAN-11 AC-28: live file scan — clean downloadable, EICAR blocked.
# Usage: pwsh scripts/acceptance-file-scan.ps1

param(
    [string]$BaseUrl = $env:BASE_URL,
    [string]$KeycloakBase = $env:KEYCLOAK_BASE_URL,
    [string]$Username = "acc-central-a",
    [string]$Password = $env:ACCEPTANCE_PERSONA_PASSWORD,
    [string]$ClientSecret = $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET,
    [string]$AccessToken = $env:ACCESS_TOKEN,
    [int]$PollSeconds = 120
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BaseUrl)) { $BaseUrl = "http://localhost:6200" }
if ([string]::IsNullOrWhiteSpace($KeycloakBase)) { $KeycloakBase = "http://localhost:6388" }
if ([string]::IsNullOrWhiteSpace($Password)) { $Password = "Accept-Hr123!" }
if ([string]::IsNullOrWhiteSpace($ClientSecret)) { $ClientSecret = $env:E2E_KEYCLOAK_CLIENT_SECRET }

$evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
    $env:ACCEPTANCE_EVIDENCE_DIR
} else {
    Join-Path $root "artifacts/acceptance/evidence"
}
$evidenceDir = Join-Path $evidenceRoot "AC-28"
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

function Get-Token {
    $form = @{
        grant_type = "password"
        client_id = "hr-gateway-client"
        client_secret = $ClientSecret
        username = $Username
        password = $Password
        scope = "openid profile email"
    }
    $resp = Invoke-RestMethod -Method Post -Uri "$KeycloakBase/realms/hrmanagement/protocol/openid-connect/token" -Body $form
    return $resp.access_token
}

function Invoke-FileApi([string]$Method, [string]$Path, [object]$Body = $null, [hashtable]$ExtraHeaders = @{}) {
    $headers = @{ Authorization = "Bearer $token" } + $ExtraHeaders
    if ($Body -ne $null) {
        return Invoke-WebRequest -Method $Method -Uri "$BaseUrl$Path" -Headers $headers -ContentType "application/json" -Body ($Body | ConvertTo-Json)
    }
    return Invoke-WebRequest -Method $Method -Uri "$BaseUrl$Path" -Headers $headers
}

function Upload-Bytes([byte[]]$Bytes, [string]$Name, [string]$ContentType) {
    $client = [System.Net.Http.HttpClient]::new()
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    $content = [System.Net.Http.ByteArrayContent]::new($Bytes)
    try {
        $client.DefaultRequestHeaders.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $token)
        $content.Headers.ContentType =
            [System.Net.Http.Headers.MediaTypeHeaderValue]::new($ContentType)
        $form.Add($content, "file", $Name)

        $response = $client.PostAsync("$BaseUrl/api/files", $form).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "Upload failed with HTTP $([int]$response.StatusCode): $body"
        }

        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Content = $body
        }
    }
    finally {
        $content.Dispose()
        $form.Dispose()
        $client.Dispose()
    }
}

function Wait-Scan([string]$FileId, [string[]]$Wanted) {
    $deadline = (Get-Date).AddSeconds($PollSeconds)
    while ((Get-Date) -lt $deadline) {
        $meta = Invoke-FileApi GET "/api/files/$FileId" | Select-Object -Expand Content | ConvertFrom-Json
        if ($Wanted -contains $meta.scanStatus) { return $meta }
        Start-Sleep -Seconds 2
    }
    throw "Timeout waiting for scan status [$($Wanted -join ',')] on $FileId"
}

$token = if ([string]::IsNullOrWhiteSpace($AccessToken)) { Get-Token } else { $AccessToken }
$cleanPdf = [System.Text.Encoding]::UTF8.GetBytes("%PDF-1.4`n1 0 obj<<>>endobj`ntrailer<<>>`n%%EOF`n")
$cleanUpload = Upload-Bytes $cleanPdf "clean.pdf" "application/pdf"
$cleanMeta = $cleanUpload.Content | ConvertFrom-Json
$cleanId = $cleanMeta.fileId
$cleanBefore = @{ fileId = $cleanId; scanStatus = $cleanMeta.scanStatus }
$cleanAfter = Wait-Scan $cleanId @("Clean")
$cleanDownload = Invoke-FileApi GET "/api/files/$cleanId/download"
$cleanBefore | ConvertTo-Json | Set-Content (Join-Path $evidenceDir "scan-clean-before-after.json") -Encoding UTF8
(@{ before = $cleanBefore; after = $cleanAfter; downloadStatus = [int]$cleanDownload.StatusCode }) | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $evidenceDir "scan-clean-before-after.json") -Encoding UTF8

$eicarSig = [System.Text.Encoding]::ASCII.GetBytes('X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*')
$eicarBytes = $cleanPdf + $eicarSig
$eicarUpload = Upload-Bytes $eicarBytes "eicar.pdf" "application/pdf"
$eicarMeta = $eicarUpload.Content | ConvertFrom-Json
$eicarId = $eicarMeta.fileId
$eicarBefore = @{ fileId = $eicarId; scanStatus = $eicarMeta.scanStatus }
$eicarAfter = Wait-Scan $eicarId @("Infected", "Quarantined")
try {
    Invoke-FileApi GET "/api/files/$eicarId/download" | Out-Null
    $denyStatus = 200
} catch {
    $denyStatus = [int]$_.Exception.Response.StatusCode.value__
}
(@{ before = $eicarBefore; after = $eicarAfter; downloadStatus = $denyStatus }) | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $evidenceDir "scan-eicar-before-after.json") -Encoding UTF8
@{ statusCode = $denyStatus; ok = ($denyStatus -ge 400) } | ConvertTo-Json | Set-Content (Join-Path $evidenceDir "download-denied.json") -Encoding UTF8

if ($cleanAfter.scanStatus -ne "Clean" -or $cleanDownload.StatusCode -ne 200 -or $denyStatus -lt 400) {
    Write-Error "AC-28 FAIL: clean=$($cleanAfter.scanStatus) download=$($cleanDownload.StatusCode) eicarDeny=$denyStatus"
}
Write-Host "PASS AC-28 file scan"
exit 0
