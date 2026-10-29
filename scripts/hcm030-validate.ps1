# HCM-030 / FS-030 / COM-030 (PLAN-30): validate the candidate experience across all three sessions — unified intake,
# application events and the public status projection, the backfill, profile/eligibility (session 1); candidate
# documents through the File.Storage service upload, candidate notifications and pair-aware retention (session 2);
# the /careers/my OTP portal, the gateway careers_session allow-list, HR on-behalf acceptance, privacy and the
# accessibility scan (session 3). Runs the HCM-030 unit tests, the a11y scan and the ROPA validation.
param(
    [string]$RepoRoot = "",
    # Skips `dotnet ef migrations has-pending-model-changes` (needs the EF tool and builds of the Web projects).
    [switch]$SkipModelCheck,
    # Skips the unit test run (static checks, a11y scan and ROPA validation only).
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
    "docs/acceptance-plans/PLAN-30-hcm-candidate-experience.md",
    "docs/acceptance-plans/execution/PLAN-30-EXECUTION.md",
    "docs/PRV_001_DPIA_RECRUITMENT.md",
    "reports/plan30-session1-handoff.md",
    "reports/plan30-session2-handoff.md",
    "scripts/hcm030-validate.ps1",
    "scripts/hcm030-backfill-unified-intake.ps1",
    "scripts/sql/hcm030-backfill-unified-intake.sql",
    "scripts/hcm006-a11y-scan.ps1",
    "scripts/prv001-ropa-validate.ps1",
    # Session 1 — unified intake, events, projection, eligibility
    "src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentApplicationEvent.cs",
    "src/Services/HRCore/HR.Core.Domain/Enums/RecruitmentIntakeChannel.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/CandidatePublicStatusProjection.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/RecruitmentEligibilityRules.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/UnifiedIntakeStatusMapping.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Data/RecruitmentApplicationEventRecorder.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/UnifiedIntakeService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidatePublicStatusService.cs",
    # Session 2 — documents (File.Storage service upload), notifications, retention
    "src/Services/FileStorage/File.Storage.Web/Controllers/FilesServiceUploadController.cs",
    "src/Services/FileStorage/File.Storage.Infrastructure/Services/FileServiceUploadService.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/CandidateDocument.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/CandidateNotification.cs",
    "src/Services/HRCore/HR.Core.Domain/Entities/CandidateRetentionRun.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/CandidateDocumentTypes.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/CandidateContactRules.cs",
    "src/Services/HRCore/HR.Core.Domain/Services/CandidateNotificationTemplates.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateNotificationDispatcher.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateNotificationJob.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateRetentionService.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateRetentionJob.cs",
    "src/Services/HRCore/HR.Core.Web/Services/CandidateDocumentService.cs",
    "src/Services/HRCore/HR.Core.Web/Services/CandidateDocumentUploadClient.cs",
    "src/Services/HRCore/HR.Core.Web/Views/CandidateLifecycle/Index.cshtml",
    "src/Shared/HRManagement.ServiceDefaults/Notifications/INotificationChannelSender.cs",
    "src/Shared/HRManagement.ServiceDefaults/Notifications/FileDropNotificationSender.cs",
    "src/Shared/HRManagement.ServiceDefaults/Notifications/SmtpEmailNotificationSender.cs",
    "src/Shared/HRManagement.ServiceDefaults/Notifications/HttpSmsNotificationSender.cs",
    # Session 3 — OTP portal, gateway, on-behalf acceptance
    "src/Services/HRCore/HR.Core.Domain/Entities/CandidateOtpChallenge.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Middleware/CareersClientIpResolver.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateOtpKeyRing.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateOtpService.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/CandidatePortalController.cs",
    "src/Services/HRCore/HR.Core.Web/Services/CandidatePortalService.cs",
    "src/Services/HRCore/HR.Core.Web/Services/CandidatePortalSession.cs",
    "src/Services/HRCore/HR.Core.Web/Services/OfferOnBehalfService.cs",
    "src/Services/HRCore/HR.Core.Web/Views/CandidatePortal/Login.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/CandidatePortal/Code.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/CandidatePortal/Index.cshtml",
    "src/Services/HRCore/HR.Core.Web/wwwroot/js/careers-portal.js",
    # Tests
    "src/Tests/HRManagement.UnitTests/Hcm030UnifiedIntakeTests.cs",
    "src/Tests/HRManagement.UnitTests/CandidatePublicStatusProjectionTests.cs",
    "src/Tests/HRManagement.UnitTests/RecruitmentEligibilityRulesTests.cs",
    "src/Tests/HRManagement.UnitTests/Fs030ServiceUploadPolicyTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030CandidateDocumentTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030NotificationPiiGuardTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030RetentionJobTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030CandidateOtpTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030CareersSessionTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030CandidatePortalTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030OnBehalfAcceptanceTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030DsarCandidateExportTests.cs",
    "src/Tests/HRManagement.UnitTests/Hcm030PrivacyA11yScanTests.cs",
    "src/Tests/HRManagement.UnitTests/GatewayUpstreamCookieForwardingTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm030BackfillTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Fs030ServiceUploadHttpTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm030CandidateNotificationConcurrencyTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm030CandidateRetentionIntegrationTests.cs",
    "src/Tests/HRManagement.IntegrationTests/Hcm030CandidateOtpConcurrencyTests.cs",
    "src/Tests/HRManagement.AcceptanceTests/AcHcm30CandidateExperienceTests.cs"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $file))) {
        $failures += "missing $file"
    }
}

# Migrations: HR.Core only, all additive (the UnifiedIntake data step writes only the new IntakeChannel column).
$coreMigrations = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations"
foreach ($migration in @("UnifiedIntake", "ApplicationProfile", "CandidateDocuments", "CandidateNotifications",
        "CandidateRetention", "CandidatePortal", "OfferAcceptance", "OfferDeclineEvidence")) {
    $file = Get-ChildItem -Path $coreMigrations -Filter "*_HCM030_$migration.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $file) {
        $failures += "missing migration HCM030_$migration"
        continue
    }
    $up = [regex]::Match((Get-Content -Raw $file.FullName), '(?s)void Up\(.*?void Down\(').Value
    if ($up -match 'DropTable\(|DropColumn\(|(?i)\bDELETE\s+FROM\b|(?i)\bTRUNCATE\b') {
        $failures += "HCM030_$migration must be additive (no DropTable/DropColumn/DELETE in Up)"
    }
}
$unified = Get-ChildItem -Path $coreMigrations -Filter "*_HCM030_UnifiedIntake.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($unified) {
    $sql = Get-Content -Raw $unified.FullName
    if ($sql -notmatch 'Npgsql\.EntityFrameworkCore\.PostgreSQL' -or $sql -notmatch 'HCM030: duplicate') {
        $failures += "HCM030_UnifiedIntake data step must be Postgres-guarded and raise on duplicate links"
    }
}
# File.Storage (FS-030) changed no entity: no new migration.
$fileStorageMigrations = Resolve-RepoPath "src/Services/FileStorage/File.Storage.Infrastructure/Migrations"
# Match the PLAN-30 name after the 14-digit EF timestamp only, so ordinary timestamps containing "030" do not trip it.
if (Get-ChildItem -Path $fileStorageMigrations -Filter "*.cs" -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d{14}_(FS|HCM)030' }) {
    $failures += "File.Storage must have no PLAN-30 migration (FS-030 changed no entity)"
}

# Backfill SQL only inserts recruitment applications and sets the channel links.
$backfill = Get-Content -Raw (Resolve-RepoPath "scripts/sql/hcm030-backfill-unified-intake.sql")
if ($backfill -match '(?i)\bDELETE\s+FROM\b|(?i)\bTRUNCATE\b|(?i)\bDROP\s+TABLE\b') { $failures += "backfill SQL must not delete data" }
$backfillScript = Get-Content -Raw (Resolve-RepoPath "scripts/hcm030-backfill-unified-intake.ps1")
foreach ($switch in @('\$Apply', '\$DockerContainer')) {
    if ($backfillScript -notmatch $switch) { $failures += "backfill script must support -$($switch.TrimStart('\$'))" }
}

# Contracts that other services and the gateway rely on.
$identity = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/ServiceIdentity.cs")
if (-not $identity.Contains('FileStorageServiceUpload = "file-storage.service-upload"')) { $failures += "ServiceScopes.FileStorageServiceUpload missing" }
$cookies = Get-Content -Raw (Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/GatewayUpstreamCookieForwarding.cs")
if (-not $cookies.Contains('CareersSessionCookieName = "careers_session"')) { $failures += "gateway careers_session allow-list constant missing" }
$session = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Services/CandidatePortalSession.cs")
if (-not $session.Contains('GatewayUpstreamCookieForwarding.CareersSessionCookieName') -or -not $session.Contains('"HR.Core.CareersSession.v1"')) {
    $failures += "CandidatePortalSession must use the gateway cookie name and the versioned Data Protection purpose"
}
$limiter = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Middleware/PublicCareersRateLimitExtensions.cs")
if (-not $limiter.Contains('OtpPolicyName = "careers-otp"') -or -not $limiter.Contains('CreateChained')) {
    $failures += "careers-otp must be a chained limiter (shared public window + per-client window)"
}
$otpEntity = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Entities/CandidateOtpChallenge.cs")
if ($otpEntity -match '\bpublic\s+string\??\s+Code\s*\{') { $failures += "CandidateOtpChallenge must not store the plain code" }
$keyRing = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateOtpKeyRing.cs")
if (-not $keyRing.Contains('CryptographicOperations.FixedTimeEquals') -or -not $keyRing.Contains('"Recruitment:Portal:OtpHmacKey"')) {
    $failures += "OTP codes must be HMAC'd with Recruitment:Portal:OtpHmacKey and compared in constant time"
}
$offerLetters = Get-Content -Raw (Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Controllers/OfferLettersController.cs")
if ($offerLetters -notmatch '(?s)Policy = HrManagementPolicies\.HrCoreWrite\)\]\s*public async Task<IActionResult> AcceptOffer\(\s*Guid id,\s*AcceptOfferOnBehalfForm form') {
    $failures += "AcceptOffer must stay HrCoreWrite (HrCoreApprove is wider) and take the on-behalf form (reason + evidence)"
}

# Configuration defaults per environment.
$coreWeb = "src/Services/HRCore/HR.Core.Web"
$base = Read-Json "$coreWeb/appsettings.json"
if ($base.Notifications.Candidate.Provider -ne "None") { $failures += "Notifications:Candidate:Provider must default to None" }
if ($base.ServiceIdentity.Scope -notmatch '(^|\s)file-storage\.service-upload(\s|$)') { $failures += "HR.Core ServiceIdentity:Scope must include file-storage.service-upload" }
foreach ($env in @("Production", "Staging")) {
    $path = "$coreWeb/appsettings.$env.json"
    if (-not (Test-Path (Resolve-RepoPath $path))) { $failures += "missing $path"; continue }
    $settings = Read-Json $path
    if ($settings.Notifications.Candidate.Provider -ne "None") { $failures += "Notifications:Candidate:Provider must be None in $env (D-8 pending)" }
    if ($settings.Recruitment.Retention.Apply -ne $false) { $failures += "Recruitment:Retention:Apply must be false in $env (dry run until DPO approval)" }
    if (-not [string]::IsNullOrWhiteSpace($settings.Recruitment.PublicPortalBaseUrl)) { $failures += "Recruitment:PublicPortalBaseUrl must stay empty in $env" }
}
$development = Read-Json "$coreWeb/appsettings.Development.json"
if ($development.Notifications.Candidate.Provider -ne "FileDrop") { $failures += "Development must use the FileDrop lab adapter" }

$acceptanceCompose = Get-Content -Raw (Resolve-RepoPath "docker-compose.acceptance.yml")
foreach ($line in @('Notifications__Candidate__Provider: FileDrop', 'Recruitment__Retention__Apply: "true"',
        'Integration__FileStorageBaseUrl: http://file-storage-web:8080', './artifacts/acceptance/candidate-notifications:/var/hrm/candidate-notifications',
        'file-storage.service-upload')) {
    if (-not $acceptanceCompose.Contains($line)) { $failures += "docker-compose.acceptance.yml: missing '$line'" }
}
$productionCompose = Get-Content -Raw (Resolve-RepoPath "docker-compose.production.yml")
if (-not $productionCompose.Contains('Recruitment__Portal__OtpHmacKey: ${HR_CORE_OTP_HMAC_KEY:?')) { $failures += "docker-compose.production.yml must require HR_CORE_OTP_HMAC_KEY" }
$keycloakSync = Get-Content -Raw (Resolve-RepoPath "scripts/sync-keycloak-dev-realm.ps1")
if (-not $keycloakSync.Contains('file-storage.service-upload')) { $failures += "sync-keycloak-dev-realm.ps1 must provision file-storage.service-upload" }

# Session markers are resolved (PLAN-30 S2 / S3 comments must not remain in code or scripts).
$markerHits = Get-ChildItem -Path (Resolve-RepoPath "src"), (Resolve-RepoPath "scripts") -Recurse -File -Include *.cs, *.cshtml, *.js, *.ps1, *.sql |
    Where-Object { $_.Name -ne "hcm030-validate.ps1" -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Select-String -Pattern 'PLAN-30 S[23]:' -SimpleMatch:$false
foreach ($hit in $markerHits) { $failures += "unresolved marker $($hit.Path):$($hit.LineNumber)" }

if (-not $SkipModelCheck) {
    foreach ($svc in @(
            @("HR.Core", "src/Services/HRCore/HR.Core.Infrastructure", "src/Services/HRCore/HR.Core.Web"),
            @("File.Storage", "src/Services/FileStorage/File.Storage.Infrastructure", "src/Services/FileStorage/File.Storage.Web"))) {
        Write-Host "Checking for pending EF model changes ($($svc[0]))..."
        $ef = dotnet ef migrations has-pending-model-changes -p (Resolve-RepoPath $svc[1]) -s (Resolve-RepoPath $svc[2]) 2>&1
        $ef | Out-Host
        if ($LASTEXITCODE -ne 0 -or -not ($ef -match 'No changes have been made')) { $failures += "$($svc[0]) has pending EF model changes" }
    }
}

if (-not $SkipTests) {
    $unitTests = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj"
    Write-Host "Running Hcm030|CandidatePublicStatusProjection|RecruitmentEligibility|Fs030|GatewayUpstreamCookieForwarding unit tests..."
    dotnet test $unitTests --filter "FullyQualifiedName~Hcm030|FullyQualifiedName~CandidatePublicStatusProjection|FullyQualifiedName~RecruitmentEligibility|FullyQualifiedName~Fs030|FullyQualifiedName~GatewayUpstreamCookieForwarding" --no-restore 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { $failures += "Hcm030/CandidatePublicStatusProjection/RecruitmentEligibility/Fs030 unit tests failed" }
}

Write-Host "Running the public careers / candidate portal a11y scan..."
$a11y = & powershell -NoProfile -ExecutionPolicy Bypass -File (Resolve-RepoPath "scripts/hcm006-a11y-scan.ps1") 2>&1
$a11y | Out-Host
if ($LASTEXITCODE -ne 0 -or -not ($a11y -match '^PASS: ')) { $failures += "hcm006-a11y-scan.ps1 reported findings" }

Write-Host "Running the ROPA validation..."
$ropa = & powershell -NoProfile -ExecutionPolicy Bypass -File (Resolve-RepoPath "scripts/prv001-ropa-validate.ps1") -RepoRoot $RepoRoot 2>&1
$ropa | Out-Host
if ($LASTEXITCODE -ne 0 -or -not ($ropa -match '^PRV-001 OK:')) { $failures += "prv001-ropa-validate.ps1 failed" }
$activities = (Read-Json "docs/prv-ropa/processing-activities.json").activities | ForEach-Object { $_.id }
foreach ($activity in @("pa-hcm-public-recruitment", "pa-hcm-candidate-otp", "pa-hcm-candidate-documents", "pa-hcm-candidate-notifications")) {
    if ($activities -notcontains $activity) { $failures += "ROPA activity $activity missing" }
}

if ($failures.Count -gt 0) {
    Write-Error ("hcm030-validate failed:`n" + ($failures -join "`n"))
}

Write-Host "hcm030-validate: PASS"
