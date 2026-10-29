# HCM-031 (PLAN-31): validate multi-vacancy competitions — competitions and lines, intake with line preferences, exam
# sessions/seating/admission cards, anonymous double marking (hr-exam-marker), results with approval/publication and the
# objection window, allocation by merit and preference, line reserve recall and bulk offers; the candidate portal,
# notifications (no score/rank/national id), retention and DSAR. Static checks (files, additive migrations, no legal
# defaults outside Development, marker access entries), the Hcm031 + PII guard unit tests and the accessibility scan.
param(
    [string]$RepoRoot = "",
    # Skips `dotnet ef migrations has-pending-model-changes` (needs the EF tool and builds of the Web projects).
    [switch]$SkipModelCheck,
    # Skips the unit test run (static checks and the a11y scan only).
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

$failures = @()

$requiredFiles = @(
    "docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md",
    "docs/acceptance-plans/execution/PLAN-31-EXECUTION.md",
    "reports/plan31-progress.md",
    "reports/plan31-final-report.md",
    "perf/hcm031-competition-scale.ps1",
    # Domain
    "src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentCompetition.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/CompetitionLine.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/CompetitionPreference.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/ExamSession.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/CompetitionResultVersion.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/CompetitionWorkflowRules.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/ExamMarkingRules.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/CompetitionScoringRules.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/CompetitionAllocationRules.cs",
    # Infrastructure
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/CompetitionLifecycleService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/CompetitionIntakeService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/ExamSeatingService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/ExamMarkingService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/ExamAdmissionCardDocumentService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/CompetitionResultsService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/CompetitionObjectionService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/CompetitionAllocationService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/CompetitionReserveService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/CompetitionOfferService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceCompetitionPatch.cs",
    # Web
    "src/Services/HRCore/HR.Core.Web/Controllers/CompetitionsController.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/ExamMarkingController.cs",
    "src/Services/HRCore/HR.Core.Web/Views/PublicCareers/Competition.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/PublicCareers/CompetitionResults.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/ExamMarking/Index.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/ExamMarking/Session.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/ExamMarking/Paper.cshtml",
    # Tests
    "src/Tests/HRManagement.UnitTests/Hcm031TestSupport.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031CompetitionLifecycleTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031IntakeTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031SeatingTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031MarkingTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031MarkerAccessTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031AdmissionCardTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031ResultsTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031ObjectionTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031AllocationTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031ReserveRecallTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031BulkOfferTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031PortalTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031NotificationTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031CandidateDataTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm031AcceptanceSeedTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030NotificationPiiGuardTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm031CompetitionIntegrationTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm031CompetitionScaleTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm31CompetitionTests.cs"
)
foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) { $failures += "missing $file" }
}

# Migrations: HR.Core only, additive (no DropTable/DropColumn/DELETE/UPDATE of existing rows in Up).
$coreMigrations = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
foreach ($migration in @("Competitions", "Exams", "Results")) {
    $file = Get-ChildItem -Path $coreMigrations -Filter "*_HCM031_$migration.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $file) { $failures += "missing migration HCM031_$migration"; continue }
    $up = [regex]::Match((Get-Content -Raw $file.FullName), '(?s)void Up\(.*?void Down\(').Value
    if ($up -match 'DropTable\(|DropColumn\(|(?i)\bDELETE\s+FROM\b|(?i)\bTRUNCATE\b|(?i)\bUPDATE\s+"') {
        $failures += "HCM031_$migration must be additive (no DropTable/DropColumn/DELETE/UPDATE in Up)"
    }
}

# Decision 1: no legal default outside Development (base, Staging, Production carry no Recruitment:Competition:Defaults).
$coreWeb = "src/Services/HRCore/HR.Core.Web"
foreach ($name in @("appsettings.json", "appsettings.Staging.json", "appsettings.Production.json")) {
    $path = "$coreWeb/$name"
    if (-not (Test-Path (Resolve-RepoPath $path))) { $failures += "missing $path"; continue }
    $settings = Read-Json $path
    if ($null -ne $settings.Recruitment -and $null -ne $settings.Recruitment.Competition -and $null -ne $settings.Recruitment.Competition.Defaults) {
        $failures += "$path must not define Recruitment:Competition:Defaults (legal inputs are per competition)"
    }
}
foreach ($compose in @("docker-compose.production.yml", "docker-compose.acceptance.yml")) {
    $text = Get-Content -Raw (Resolve-RepoPath $compose)
    if ($text -match 'Recruitment__Competition__Defaults') { $failures += "$compose must not set Recruitment__Competition__Defaults" }
}
$options = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOptions.cs")
if ($options -match '(?s)class RecruitmentCompetitionDefaults.*?ObjectionWindowDays\s*\{[^}]*\}\s*=\s*\d') {
    $failures += "RecruitmentCompetitionDefaults must have no hard-coded legal values"
}

# Decision 6 / J7: marker access is the role-specific slice (policy, module allow-list entry, gateway path rule).
$roles = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/HrManagementRoles.cs")
if (-not $roles.Contains('HrExamMarker = "hr-exam-marker"')) { $failures += "HrManagementRoles.HrExamMarker missing" }
$marking = Get-Content -Raw (Resolve-RepoPath "$coreWeb/Controllers/ExamMarkingController.cs")
if (-not $marking.Contains('HrManagementPolicies.HrCoreExamMarking')) { $failures += "ExamMarkingController must require HrCoreExamMarking" }
$gatewayRules = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs")
if (-not $gatewayRules.Contains('"/ui/hr-core/ExamMarking"')) {
    $failures += "gateway path rule /ui/hr-core/ExamMarking missing"
}
# Marker views never reference applicant identity.
foreach ($view in Get-ChildItem -Path (Resolve-RepoPath "$coreWeb/Views/ExamMarking") -Filter "*.cshtml") {
    if ((Get-Content -Raw $view.FullName) -match 'ApplicantFullName|NationalId|RecruitmentApplicationId') {
        $failures += "marker view $($view.Name) references applicant identity"
    }
}
$personas = Read-Json "testdata/acceptance/personas.json"
foreach ($id in @("a-exam-marker", "a-exam-marker-2", "a-exam-marker-3")) {
    $persona = $personas.personas | Where-Object { $_.id -eq $id }
    if (-not $persona) { $failures += "acceptance persona $id missing"; continue }
    if ($persona.roles -notcontains "hr-exam-marker") { $failures += "persona $id must hold hr-exam-marker" }
    if (-not $persona.passwordEnv -or $persona.PSObject.Properties.Name -contains "password") { $failures += "persona $id must use passwordEnv only" }
}

# Decision 4: competition messages carry no score, rank or national id.
$templates = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Services/CandidateNotificationTemplates.cs")
foreach ($code in @("ADMISSION_CARD_READY", "RESULTS_PUBLISHED", "OBJECTION_DECIDED", "COMPETITION_ALLOCATED", "COMPETITION_RESERVE",
        "COMPETITION_RESERVE_RECALLED", "COMPETITION_NOT_SUCCESSFUL")) {
    if (-not $templates.Contains("`"$code`"")) { $failures += "template $code missing" }
}

# Workflows seeded with distinct ids.
$workflows = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs")
foreach ($wf in @('"RECRUITMENT-COMPETITION-APPROVAL"', '"RECRUITMENT-RESULTS-APPROVAL"')) {
    if (-not $workflows.Contains($wf)) { $failures += "workflow $wf not seeded" }
}

if (-not $SkipModelCheck) {
    Write-Host "Checking for pending EF model changes (HR.Core)..."
    $ef = dotnet ef migrations has-pending-model-changes -p (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure") -s (Resolve-RepoPath $coreWeb) 2>&1
    $ef | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not ($ef -match 'No changes have been made')) { $failures += "HR.Core has pending EF model changes" }
}

if (-not $SkipTests) {
    $unitTests = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj"
    Write-Host "Running Hcm031 and the candidate notification PII guard unit tests..."
    dotnet test $unitTests --filter "FullyQualifiedName~Hcm031|FullyQualifiedName~Hcm030NotificationPiiGuard" --no-restore 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { $failures += "Hcm031 / PII guard unit tests failed" }
}

Write-Host "Running the public careers / candidate portal a11y scan..."
$a11y = & powershell -NoProfile -ExecutionPolicy Bypass -File (Resolve-RepoPath "scripts/hcm006-a11y-scan.ps1") 2>&1
$a11y | Out-Host
if ($LASTEXITCODE -ne 0 -or -not ($a11y -match '^PASS: ')) { $failures += "hcm006-a11y-scan.ps1 reported findings" }
$scan = Get-Content -Raw (Resolve-RepoPath "scripts/hcm006-a11y-scan.ps1")
foreach ($view in @("PublicCareers/Competition.cshtml", "PublicCareers/CompetitionResults.cshtml")) {
    if (-not $scan.Contains($view)) { $failures += "hcm006-a11y-scan.ps1 must scan $view" }
}

if ($failures.Count -gt 0) {
    Write-Error ("hcm031-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm031-validate: PASS"
