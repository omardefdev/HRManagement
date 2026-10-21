# L10N-001: validate localization infrastructure.
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

function Need([string]$rel) {
    $p = Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $p)) { $script:failures.Add("missing $rel") }
    return $p
}

Need "docs/L10N_001_LOCALIZATION.md" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/Localization/HrLocalizationExtensions.cs" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/Localization/ICalendarService.cs" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/Localization/DamascusCalendarService.cs" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/Localization/CultureController.cs" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/Localization/Resources/SharedResources.cs" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/Localization/Resources/SharedResources.ar.resx" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/Localization/Resources/SharedResources.en.resx" | Out-Null
Need "src/Shared/HRManagement.Web.Shared/Resources/SharedResources.cs" | Out-Null
Need "src/Shared/HRManagement.Web.Shared/Resources/SharedResources.resx" | Out-Null
Need "src/Shared/HRManagement.Web.Shared/Resources/SharedResources.en.resx" | Out-Null
Need "src/Tests/HRManagement.UnitTests/L10n001LocalizationTests.cs" | Out-Null

foreach ($program in @(
    "src/Gateway/HRManagement.Gateway/Program.cs",
    "src/Services/HRCore/HR.Core.Web/Program.cs",
    "src/Services/Payroll/Payroll.Web/Program.cs",
    "src/Services/Compliance/Compliance.Web/Program.cs",
    "src/Services/HRContracts/HR.Contracts.Web/Program.cs",
    "src/Services/HRTraining/HR.Training.Web/Program.cs",
    "src/Services/HRMovements/HR.Movements.Web/Program.cs",
    "src/Services/HRHealth/HR.Health.Web/Program.cs",
    "src/Services/IDCard/IDCard.Web/Program.cs",
    "src/Services/ITInfrastructure/IT.Infra.Web/Program.cs",
    "src/Services/DevAnalytics/Dev.Analytics.Web/Program.cs",
    "src/Services/AuthDirectory/Auth.Directory.Web/Program.cs",
    "src/Services/AdminLogistics/Admin.Logistics.Web/Program.cs",
    "src/Services/RecordsArchive/Records.Archive.Web/Program.cs"
)) {
    $path = Need $program
    if (Test-Path $path) {
        $text = Get-Content -Raw $path
        if ($text -notmatch "AddHrLocalization") { $failures.Add("$program missing AddHrLocalization") }
        if ($text -notmatch "UseHrLocalization") { $failures.Add("$program missing UseHrLocalization") }
    }
}

$extPath = Need "src/Shared/HRManagement.ServiceDefaults/Localization/HrLocalizationExtensions.cs"
if (Test-Path $extPath) {
    $ext = Get-Content -Raw $extPath
    if ($ext -notmatch 'ar-SY') { $failures.Add("default culture must be ar-SY") }
    if ($ext -notmatch "CookieRequestCultureProvider") { $failures.Add("cookie provider required") }
    if ($ext -notmatch "QueryStringRequestCultureProvider") { $failures.Add("query provider required") }
}

$calPath = Need "src/Shared/HRManagement.ServiceDefaults/Localization/DamascusCalendarService.cs"
if (Test-Path $calPath) {
    $cal = Get-Content -Raw $calPath
    if ($cal -notmatch "HijriCalendar") { $failures.Add("Hijri calendar support required") }
    if ($cal -notmatch "Asia/Damascus|Syria Standard Time") { $failures.Add("Damascus timezone required") }
}

$arResxPath = Need "src/Shared/HRManagement.ServiceDefaults/Localization/Resources/SharedResources.ar.resx"
if (Test-Path $arResxPath) {
    $arResx = Get-Content -Raw $arResxPath
    foreach ($key in @("Save", "Cancel", "Search", "Login", "Logout", "Home", "Employees", "Payroll")) {
        if ($arResx -notmatch "name=`"$key`"") { $failures.Add("ar.resx missing key: $key") }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "L10N-001 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "L10N-001 validation OK."
