param(
    [Parameter(Mandatory = $true)][string]$OrganizationName,
    [Parameter(Mandatory = $true)][string]$LegalRegime,
    [string]$LicenseKey = "",
    [string]$LicenseExpiresUtc = "",
    [string]$ComposeFile = "docker/docker-compose.yml"
)

$ErrorActionPreference = "Stop"
$env:HR_ORG_NAME = $OrganizationName
$env:HR_LEGAL_REGIME = $LegalRegime
$env:HR_LICENSE_KEY = $LicenseKey
$env:HR_LICENSE_EXPIRES_UTC = $LicenseExpiresUtc

Write-Host "Deploying HRManagement for: $OrganizationName (regime: $LegalRegime)"
docker compose -f $ComposeFile up -d --build
if (Test-Path "docker-compose.observability.yml") {
    docker compose -f $ComposeFile -f docker-compose.observability.yml up -d
    Write-Host "Observability stack (Prometheus/Grafana/Alertmanager) started."
}
Write-Host "Deployment started. Activate legal regime '$LegalRegime' in Payroll > الأطر القانونية if needed."
Write-Host "Set Organization:Name and Organization:PrimaryColor in service appsettings for branding."
