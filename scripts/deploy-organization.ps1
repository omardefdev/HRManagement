<#
.SYNOPSIS
  Deploys the system for one organization (PLAN-36: a wrapper around scripts/vps/deploy.ps1).

.DESCRIPTION
  Sets HRM_ORGANIZATION_NAME (the name in the gateway and module headers) and deploys the release with
  scripts/vps/deploy.ps1. On the server the name is written to the HRM_ORGANIZATION_NAME line of
  /opt/hrm/shared/.env (that one key only; it is not a secret); with -Local it is passed in the process environment,
  which takes precedence over the env file.
  The legal regime is not a deployment setting: after the deployment, import the payroll legal framework pack with
  scripts/pay054-import-legal-pack.ps1 (PLAN-54), then approve, attest and publish it in Payroll > Legal frameworks. The
  former HR_LEGAL_REGIME / HR_LICENSE_* variables were read by nothing and are gone; licensing is
  Deployment__RequireLicenseKey (HRM_REQUIRE_LICENSE in the server's .env, off by default, D-OPS-9).
  PowerShell 5.1:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy-organization.ps1 -OrganizationName "<name>" [-Tag <sha12>]
#>
param(
    [Parameter(Mandatory = $true)][string]$OrganizationName,
    [string]$Tag = "",
    [string]$SshHost = "hrm-vps",
    [switch]$Local,
    [string]$EnvFile = "",
    [string]$RemoteHome = "/opt/hrm"
)

$ErrorActionPreference = "Stop"
$deploy = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "vps\deploy.ps1"
if ($OrganizationName -match "[\r\n]") { throw "OrganizationName must be one line." }

if ($Local) {
    $env:HRM_ORGANIZATION_NAME = $OrganizationName
    & powershell -NoProfile -ExecutionPolicy Bypass -File $deploy -Local -EnvFile $EnvFile -Tag $Tag
}
else {
    # Base64 keeps Arabic text and quotes intact through ssh and the remote shell.
    $b64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($OrganizationName))
    $set = "f=$RemoteHome/shared/.env; v=`$(printf %s $b64 | base64 -d); " +
        "awk -v v=`"`$v`" 'BEGIN{d=0} /^HRM_ORGANIZATION_NAME=/{print `"HRM_ORGANIZATION_NAME=`" v; d=1; next} {print} END{if(!d) print `"HRM_ORGANIZATION_NAME=`" v}' `"`$f`" > `"`$f.tmp`" && " +
        "chmod 600 `"`$f.tmp`" && mv `"`$f.tmp`" `"`$f`""
    & ssh -o BatchMode=yes $SshHost $set
    if ($LASTEXITCODE -ne 0) { throw "could not set HRM_ORGANIZATION_NAME on $SshHost" }
    $deployArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $deploy, "-SshHost", $SshHost)
    if ($Tag) { $deployArgs += @("-Tag", $Tag) }
    & powershell @deployArgs
}
if ($LASTEXITCODE -ne 0) { throw "deployment failed" }
Write-Host "Deployed for: $OrganizationName"
# PLAN-54: no payroll run calculates before a legal framework is imported, approved, attested and published.
Write-Host "Next: import the payroll legal framework pack (a production deployment seeds none), as a holder of Payroll.MasterData:"
Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pay054-import-legal-pack.ps1 -PackPath <pack.json> -EffectiveFrom <yyyy-MM-dd> -GatewayBaseUrl <https://gateway> -KeycloakBaseUrl <https://sso> -Username <user>"
Write-Host "Then, in Payroll > Legal frameworks, a second person approves the draft, a third attests it, and one of the two publishes it."
