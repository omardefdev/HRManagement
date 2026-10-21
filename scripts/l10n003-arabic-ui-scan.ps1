# L10N-003: scan Razor views and controllers for untranslated English UI patterns.
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

function Scan-Files {
    param([string]$Pattern, [string[]]$Paths, [string]$Message)
    foreach ($path in $Paths) {
        $full = Join-Path $RepoRoot ($path -replace '/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path $full)) { continue }
        Get-ChildItem -Path $full -Recurse -Filter "*.cshtml" -ErrorAction SilentlyContinue | ForEach-Object {
            $lines = Get-Content $_.FullName
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match $Pattern) {
                    $rel = $_.FullName.Substring($RepoRoot.Length + 1)
                    $failures.Add("$Message at ${rel}:$($i + 1)")
                }
            }
        }
    }
}

$viewRoots = @(
    "src/Services",
    "src/Gateway/HRManagement.Gateway/Views",
    "src/Shared/HRManagement.Web.Shared/Views"
)

# Raw enum display in views (excluding helper usage on same line)
Scan-Files -Pattern '>@(Model|x|item|h|r|job)\.(Status|State|Severity)\s*<' `
    -Paths $viewRoots `
    -Message "raw enum display"

Scan-Files -Pattern '@(h\.(OldStatus|NewStatus)|Model\.(OldStatus|NewStatus))' `
    -Paths $viewRoots `
    -Message "stored status without HrStoredEnumDisplay"

$englishPatterns = @(
    @{ Pattern = 'Welcome'; Message = "scaffold Welcome text" },
    @{ Pattern = 'Home Page'; Message = "scaffold Home Page title" },
    @{ Pattern = 'Request ID:'; Message = "English Request ID label" },
    @{ Pattern = '>Legal Hold<'; Message = "English Legal Hold label" }
)

foreach ($ep in $englishPatterns) {
    Scan-Files -Pattern $ep.Pattern -Paths $viewRoots -Message $ep.Message
}

# Export controllers: enum ToString in user-facing output
$exportControllers = Get-ChildItem -Path (Join-Path $RepoRoot "src") -Recurse -Filter "*Export*Controller*.cs" -ErrorAction SilentlyContinue
$exportControllers += Get-ChildItem -Path (Join-Path $RepoRoot "src/Services/HRCore/HR.Core.Web/Controllers/ReportsController.cs") -ErrorAction SilentlyContinue
$exportControllers += Get-ChildItem -Path (Join-Path $RepoRoot "src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs") -ErrorAction SilentlyContinue

foreach ($file in $exportControllers | Select-Object -Unique) {
    $text = Get-Content -Raw $file.FullName
    if ($text -match '\.Status\.ToString\(\)|CurrentStatus\.ToString\(\)|LeaveType\.ToString\(\)') {
        $rel = $file.FullName.Substring($RepoRoot.Length + 1)
        $failures.Add("enum ToString in export at $rel")
    }
}

# Required infrastructure files
$required = @(
    "src/Shared/HRManagement.SharedKernel/Localization/EnumDisplayExtensions.cs",
    "src/Shared/HRManagement.Web.Shared/TagHelpers/HrEnumDisplayTagHelper.cs",
    "src/Shared/HRManagement.Web.Shared/TagHelpers/HrStoredEnumTagHelper.cs",
    "src/Shared/HRManagement.Web.Shared/wwwroot/js/jquery-validate-ar.js",
    "src/Tests/HRManagement.UnitTests/L10n003ArabicUiTests.cs"
)
foreach ($rel in $required) {
    $p = Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $p)) { $failures.Add("missing $rel") }
}

# Localization wired in all module web apps
foreach ($program in Get-ChildItem -Path (Join-Path $RepoRoot "src/Services") -Recurse -Filter "Program.cs" |
    Where-Object { $_.FullName -match '\\\.Web\\Program\.cs$' -and $_.FullName -notmatch 'File\.Storage' }) {
    $text = Get-Content -Raw $program.FullName
    $rel = $program.FullName.Substring($RepoRoot.Length + 1)
    if ($text -notmatch 'AddHrLocalization') { $failures.Add("$rel missing AddHrLocalization") }
    if ($text -notmatch 'UseHrLocalization') { $failures.Add("$rel missing UseHrLocalization") }
}

if ($failures.Count -gt 0) {
    Write-Host "L10N-003 scan FAILED ($($failures.Count) issue(s)):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "L10N-003 scan passed." -ForegroundColor Green
exit 0
