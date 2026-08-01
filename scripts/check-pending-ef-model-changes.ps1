# DB-001-B: Fail if any of the 15 DbContexts has pending model changes vs migrations.
# Requires: dotnet SDK + local tool dotnet-ef (dotnet tool restore).

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

dotnet tool restore | Out-Null

$Migrations = @(
  @{ Project = "src/Gateway/HRManagement.Gateway"; Startup = "src/Gateway/HRManagement.Gateway" }
  @{ Project = "src/Services/HRCore/HR.Core.Infrastructure"; Startup = "src/Services/HRCore/HR.Core.Web" }
  @{ Project = "src/Services/HRContracts/HR.Contracts.Infrastructure"; Startup = "src/Services/HRContracts/HR.Contracts.Web" }
  @{ Project = "src/Services/Payroll/Payroll.Infrastructure"; Startup = "src/Services/Payroll/Payroll.Web" }
  @{ Project = "src/Services/HRHealth/HR.Health.Infrastructure"; Startup = "src/Services/HRHealth/HR.Health.Web" }
  @{ Project = "src/Services/HRMovements/HR.Movements.Infrastructure"; Startup = "src/Services/HRMovements/HR.Movements.Web" }
  @{ Project = "src/Services/HRTraining/HR.Training.Infrastructure"; Startup = "src/Services/HRTraining/HR.Training.Web" }
  @{ Project = "src/Services/IDCard/IDCard.Infrastructure"; Startup = "src/Services/IDCard/IDCard.Web" }
  @{ Project = "src/Services/RecordsArchive/Records.Archive.Infrastructure"; Startup = "src/Services/RecordsArchive/Records.Archive.Web" }
  @{ Project = "src/Services/AdminLogistics/Admin.Logistics.Infrastructure"; Startup = "src/Services/AdminLogistics/Admin.Logistics.Web" }
  @{ Project = "src/Services/ITInfrastructure/IT.Infra.Infrastructure"; Startup = "src/Services/ITInfrastructure/IT.Infra.Web" }
  @{ Project = "src/Services/DevAnalytics/Dev.Analytics.Infrastructure"; Startup = "src/Services/DevAnalytics/Dev.Analytics.Web" }
  @{ Project = "src/Services/Compliance/Compliance.Infrastructure"; Startup = "src/Services/Compliance/Compliance.Web" }
  @{ Project = "src/Services/AuthDirectory/Auth.Directory.Infrastructure"; Startup = "src/Services/AuthDirectory/Auth.Directory.Web" }
  @{ Project = "src/Services/FileStorage/File.Storage.Infrastructure"; Startup = "src/Services/FileStorage/File.Storage.Web" }
)

$failed = $false
foreach ($m in $Migrations) {
  Write-Host "Checking pending model changes: $($m.Project) ..."
  & dotnet ef migrations has-pending-model-changes `
    --project $m.Project `
    --startup-project $m.Startup `
    --configuration Release
  if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Pending EF model changes for $($m.Project). Add a migration before merging."
    $failed = $true
  }
}

if ($failed) {
  Write-Host "Pending model change gate FAILED."
  exit 1
}

Write-Host "Pending model change gate passed ($($Migrations.Count) contexts)."
