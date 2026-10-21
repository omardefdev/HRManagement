<#
.SYNOPSIS
    BLD-002: run HR.Core locally (uses host NuGet cache — fast rebuild).
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Urls = "http://localhost:5010"
)

$ErrorActionPreference = 'Stop'
Push-Location $RepoRoot
try {
    dotnet run --project src/Services/HRCore/HR.Core.Web/HR.Core.Web.csproj --urls $Urls
}
finally {
    Pop-Location
}
