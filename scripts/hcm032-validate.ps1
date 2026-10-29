# HCM-032 (PLAN-32): validate recruitment reports and KPIs — definitions (STR-007 matrix + doc), the read-only scoped
# metrics service, «تقارير التوظيف» and its exports (no national id, names only for HrCoreWrite), the decline category, the
# analytics indexes, the Dev.Analytics publication and the semantic-layer registration. Static checks (files, additive
# migrations, options, no national id in report/export code), the Hcm032 unit tests, the STR-007 / ANA-001 / PROD-006 /
# REL-004 validators and the candidate-portal accessibility scan (the portal decline form changed).
param(
    [string]$RepoRoot = "",
    # Skips `dotnet ef migrations has-pending-model-changes` (needs the EF tool and builds of the Web projects).
    [switch]$SkipModelCheck,
    # Skips the unit test run (static checks and validators only).
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

function Read-Json([string]$rel) {
    return (Get-Content -Raw -Encoding UTF8 (Resolve-RepoPath $rel) | ConvertFrom-Json)
}

function Invoke-Validator([string]$script, [string]$passPattern) {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Resolve-RepoPath "scripts/$script") 2>&1
    $out | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not ($out -match $passPattern)) { $script:failures += "$script failed" }
}

$failures = @()
$core = "src/Services/HRCore"
$analytics = "src/Services/DevAnalytics"

$requiredFiles = @(
    "docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md",
    "docs/acceptance-plans/execution/PLAN-32-EXECUTION.md",
    "docs/STR_007_KPI_DEFINITIONS.md",
    "reports/plan32-progress.md",
    "reports/plan32-final-report.md",
    "perf/hcm032-recruitment-analytics-scale.ps1",
    # Domain
    "$core/HR.Core.Domain/Enums/OfferDeclineReasonCategory.cs",
    "$core/HR.Core.Domain/Services/RecruitmentKpi/KpiStatistics.cs",
    "$core/HR.Core.Domain/Services/RecruitmentKpi/FunnelReachRules.cs",
    "$core/HR.Core.Domain/Services/RecruitmentKpi/TimeToFillRules.cs",
    "$core/HR.Core.Domain/Services/RecruitmentKpi/ApprovalSlaRules.cs",
    "$core/HR.Core.Domain/Services/RecruitmentKpi/SmallCellSuppression.cs",
    # Infrastructure
    "$core/HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentMetricsService.cs",
    "$core/HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentMetricsModels.cs",
    "$core/HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentApplicationsCsv.cs",
    "$core/HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentReportAccess.cs",
    "$core/HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentRequisitionClosedEvent.cs",
    "$core/HR.Core.Infrastructure/Seeding/AcceptanceRecruitmentReportsPatch.cs",
    # Web
    "$core/HR.Core.Web/Controllers/RecruitmentReportsController.cs",
    "$core/HR.Core.Web/ViewModels/RecruitmentReportsViewModels.cs",
    "$core/HR.Core.Web/Services/RecruitmentReportDocuments.cs",
    "$core/HR.Core.Web/Views/RecruitmentReports/Index.cshtml",
    "$core/HR.Core.Web/Views/RecruitmentReports/Requisition.cshtml",
    "$core/HR.Core.Web/wwwroot/js/recruitment-reports.js",
    # Contract + Dev.Analytics
    "src/Shared/HRManagement.MessageContracts/Events/IRecruitmentRequisitionClosed.cs",
    "$analytics/Dev.Analytics.Domain/Entities/RecruitmentRequisitionFact.cs",
    "$analytics/Dev.Analytics.Domain/Services/RecruitmentFactRules.cs",
    "$analytics/Dev.Analytics.Infrastructure/Consumers/RecruitmentRequisitionClosedConsumer.cs",
    # Tests
    "src/Tests/HRManagement.UnitTests/Hcm032KpiStatisticsTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032FunnelReachRulesTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032ApprovalSlaRulesTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032DeclineCategoryTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032MetricsFixture.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032RecruitmentMetricsTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032RecruitmentReportsControllerTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032ExportTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032AnalyticsPublicationTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm032AcceptanceSeedTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm032RecruitmentAnalyticsScaleTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm32RecruitmentReportsTests.cs"
)
foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) { $failures += "missing $file" }
}

# Migrations: additive only (new nullable column, indexes, a new Dev.Analytics table).
$migrations = @(
    @{ Dir = "$core/HR.Core.Infrastructure/Migrations"; Name = "HCM032_OfferDeclineCategory" },
    @{ Dir = "$core/HR.Core.Infrastructure/Migrations"; Name = "HCM032_AnalyticsIndexes" },
    @{ Dir = "$analytics/Dev.Analytics.Infrastructure/Migrations"; Name = "ANA032_RecruitmentRequisitionFacts" }
)
foreach ($m in $migrations) {
    $file = Get-ChildItem -Path (Resolve-RepoPath $m.Dir) -Filter "*_$($m.Name).cs" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "*.Designer.cs" } | Select-Object -First 1
    if (-not $file) { $failures += "missing migration $($m.Name)"; continue }
    $up = [regex]::Match((Get-Content -Raw $file.FullName), '(?s)void Up\(.*?void Down\(').Value
    if ($up -match 'DropTable\(|DropColumn\(|AlterColumn\(|(?i)\bDELETE\s+FROM\b|(?i)\bTRUNCATE\b|(?i)\bUPDATE\s+"') {
        $failures += "$($m.Name) must be additive (no drop/alter/delete/update in Up)"
    }
}
$declineMigration = Get-ChildItem -Path (Resolve-RepoPath "$core/HR.Core.Infrastructure/Migrations") -Filter "*_HCM032_OfferDeclineCategory.cs" |
    Where-Object { $_.Name -notlike "*.Designer.cs" } | Select-Object -First 1
if ($declineMigration -and -not ((Get-Content -Raw $declineMigration.FullName) -match 'nullable:\s*true')) {
    $failures += "HCM032_OfferDeclineCategory must add a nullable column"
}

# Options (Recruitment:Analytics:*) with the documented defaults.
$options = Get-Content -Raw (Resolve-RepoPath "$core/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOptions.cs")
foreach ($need in @("class RecruitmentAnalyticsOptions", "StalePublishedDays { get; set; } = 60", "DefaultApprovalSlaHours", "ApprovalSlaHours",
        "MinCellSize", "DefaultPeriodMonths { get; set; } = 12", "RecruitmentAnalyticsOptions Analytics")) {
    if (-not $options.Contains($need)) { $failures += "RecruitmentOptions: missing '$need'" }
}

# Decision 7: no report or export code reads a national id; names only through the HrCoreWrite check.
$reportCode = @(
    Get-ChildItem -Path (Resolve-RepoPath "$core/HR.Core.Infrastructure/Services/Recruitment/Analytics") -Filter "*.cs"
    Get-ChildItem -Path (Resolve-RepoPath "$core/HR.Core.Web/Views/RecruitmentReports") -Filter "*.cshtml"
    Get-Item (Resolve-RepoPath "$core/HR.Core.Web/Controllers/RecruitmentReportsController.cs")
    Get-Item (Resolve-RepoPath "$core/HR.Core.Web/Services/RecruitmentReportDocuments.cs")
    Get-Item (Resolve-RepoPath "$core/HR.Core.Web/ViewModels/RecruitmentReportsViewModels.cs")
    Get-Item (Resolve-RepoPath "$analytics/Dev.Analytics.Infrastructure/Consumers/RecruitmentRequisitionClosedConsumer.cs")
    Get-Item (Resolve-RepoPath "src/Shared/HRManagement.MessageContracts/Events/IRecruitmentRequisitionClosed.cs")
)
foreach ($file in $reportCode) {
    if ((Get-Content -Raw $file.FullName) -match 'NationalId') { $failures += "$($file.Name) must not reference NationalId" }
}
$controller = Get-Content -Raw (Resolve-RepoPath "$core/HR.Core.Web/Controllers/RecruitmentReportsController.cs")
if (-not $controller.Contains('[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]')) { $failures += "RecruitmentReportsController must require HrCoreReadOnly" }
if (-not $controller.Contains('HrManagementPolicies.HrCoreWrite')) { $failures += "RecruitmentReportsController must gate names on HrCoreWrite" }
foreach ($action in @("ExportExcel", "ExportPdf", "ExportApplicationsCsv")) {
    if (-not $controller.Contains("SecurityAudit.EventTypes.BulkExport, `"RecruitmentReports/$action`"")) { $failures += "$action must be audited as BulkExport" }
}
if (([regex]::Matches($controller, 'ExportQueryLimits\.EnsureWithinLimitAsync')).Count -lt 2) { $failures += "exports must be bounded by ExportQueryLimits" }

# KPI catalog: the twelve recruitment KPIs, frozen hire_cycle_time, owner product-hr / auditor audit.
$matrix = Read-Json "docs/str-value/kpi-matrix.json"
$ids = @("recruitment_funnel", "recruitment_time_to_fill", "recruitment_time_to_hire", "recruitment_stage_time", "recruitment_offer_acceptance_rate",
    "recruitment_offer_decline_reasons", "recruitment_channel_mix", "recruitment_appeal_rate", "recruitment_merit_overrides",
    "recruitment_open_vacancies", "recruitment_approval_sla", "recruitment_competitions")
foreach ($id in $ids) {
    $kpi = $matrix.kpis | Where-Object { $_.id -eq $id }
    if (-not $kpi) { $failures += "kpi-matrix: $id missing"; continue }
    if ($kpi.ownerRole -ne "product-hr" -or $kpi.auditorRole -ne "audit") { $failures += "kpi-matrix: $id owner/auditor" }
    if (@($kpi.antiGamingRules) -notcontains "freeze-definition-before-quarter") { $failures += "kpi-matrix: $id needs freeze-definition-before-quarter" }
    if (@($kpi.moduleIds) -notcontains "hr-core") { $failures += "kpi-matrix: $id must belong to hr-core" }
}
$hire = $matrix.kpis | Where-Object { $_.id -eq "hire_cycle_time" }
if (-not $hire -or $hire.definition -ne "Median calendar days from approved vacancy to employee start date" -or $hire.target.value -ne 45) {
    $failures += "hire_cycle_time definition is frozen and must not change"
}
$definitionsDoc = Get-Content -Raw (Resolve-RepoPath "docs/STR_007_KPI_DEFINITIONS.md")
foreach ($id in $ids) { if (-not $definitionsDoc.Contains("``$id``")) { $failures += "STR_007_KPI_DEFINITIONS.md: $id missing" } }

# Event contract and its registrations.
$inventory = Read-Json "docs/rel-compat/event-schema-inventory.json"
if (-not ($inventory.contracts | Where-Object { $_.id -eq "IRecruitmentRequisitionClosed" -and $_.outbox -eq $true })) {
    $failures += "event-schema-inventory.json: IRecruitmentRequisitionClosed (outbox) missing"
}
if (-not (Get-Content -Raw (Resolve-RepoPath "docs/MESSAGE_EVENT_MATRIX.md")).Contains("IRecruitmentRequisitionClosed")) {
    $failures += "MESSAGE_EVENT_MATRIX.md: IRecruitmentRequisitionClosed missing"
}
$semantic = Get-Content -Raw (Resolve-RepoPath "docs/ana/semantic-layer.json")
foreach ($measure in @("recruitment_funnel", "recruitment_time_to_fill", "recruitment_time_to_hire", "recruitment_offer_acceptance",
        "recruitment_appeal_rate", "recruitment_open_vacancies")) {
    if (-not $semantic.Contains("`"$measure`"")) { $failures += "semantic-layer.json: $measure missing" }
}

if (-not $SkipModelCheck) {
    foreach ($pair in @(@("$core/HR.Core.Infrastructure", "$core/HR.Core.Web"), @("$analytics/Dev.Analytics.Infrastructure", "$analytics/Dev.Analytics.Web"))) {
        Write-Host "Checking for pending EF model changes ($($pair[0]))..."
        $ef = dotnet ef migrations has-pending-model-changes -p (Resolve-RepoPath $pair[0]) -s (Resolve-RepoPath $pair[1]) 2>&1
        $ef | Out-Host
        if ($LASTEXITCODE -ne 0 -or -not ($ef -match 'No changes have been made')) { $failures += "$($pair[0]) has pending EF model changes" }
    }
}

if (-not $SkipTests) {
    $unitTests = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj"
    Write-Host "Running the Hcm032 unit tests..."
    dotnet test $unitTests --filter "FullyQualifiedName~Hcm032" --no-restore 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { $failures += "Hcm032 unit tests failed" }
}

Invoke-Validator "str007-value-kpi-validate.ps1" '^PASS: STR-007'
Invoke-Validator "ana001-validate.ps1" '^PASS: ANA-001'
Invoke-Validator "prod006-semantic-layer-validate.ps1" '^PASS: PROD-006'
Invoke-Validator "rel004-compat-validate.ps1" '^PASS: REL-004'
Invoke-Validator "hcm006-a11y-scan.ps1" '^PASS: '

if ($failures.Count -gt 0) {
    Write-Error ("hcm032-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm032-validate: PASS"
