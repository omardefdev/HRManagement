# Migrates legacy HR.Core employee photos from wwwroot/uploads/employees to MinIO.
# Requires: MinIO running (docker compose up minio), mc CLI configured, HR.Core Web path.

param(
    [string]$UploadsPath = "src/Services/HRCore/HR.Core.Web/wwwroot/uploads/employees",
    [string]$MinioAlias = "hr-local",
    [string]$Bucket = "hr-core",
    [string]$MinioEndpoint = "http://localhost:9000",
    [string]$AccessKey = "hrminio",
    [string]$SecretKey = "hrminio_secret_change_me"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command mc -ErrorAction SilentlyContinue)) {
    Write-Error "MinIO Client (mc) is required. Install from https://min.io/docs/minio/linux/reference/minio-mc.html"
}

mc alias set $MinioAlias $MinioEndpoint $AccessKey $SecretKey | Out-Null
mc mb --ignore-existing "$MinioAlias/$Bucket" | Out-Null

if (-not (Test-Path $UploadsPath)) {
    Write-Host "No local uploads folder at $UploadsPath — nothing to migrate."
    exit 0
}

$files = Get-ChildItem -Path $UploadsPath -File
if ($files.Count -eq 0) {
    Write-Host "No files to migrate."
    exit 0
}

foreach ($file in $files) {
    $objectKey = "employees/photos/migrated/$($file.Name)"
    mc cp $file.FullName "$MinioAlias/$Bucket/$objectKey"
    Write-Host "Migrated $($file.Name) -> $Bucket/$objectKey"
}

Write-Host "Done. Update Employee.PhotoUrl to 'hr-core/<objectKey>' and set PhotoFileId as needed via SQL or admin UI."
