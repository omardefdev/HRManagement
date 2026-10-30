# PLAN-34 — Baseline (P0)

**Date:** 2026-10-30
**Branch:** `feature/plan-34-qa-pushable-baseline`, created from `main` @ `f9eab1a` (PLAN-25 merged). The program
documents (gap studies, verification, roadmap, owner register, PLAN-34..65, this plan's agent prompt) were committed
first as `02f890b` (prompt decision P-1).

## Machine

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26200, machine culture **ar-SA** (Um Al-Qura calendar for .NET test processes) |
| .NET SDK | **10.0.403** installed — the committed `global.json` pins `10.0.400` with `rollForward: disable`, so the owner keeps a local, never-committed `rollForward: latestPatch` edit (D-QA-12) |
| PowerShell | Windows PowerShell **5.1.26100** only; **no `pwsh`** on the host (relevant to `scripts/ci-local.ps1`, see P2) |
| Python | not installed (Store alias only) — the Python CI steps can only run in the Linux container |
| Docker | Docker Desktop, engine 29.9.2 |

## 0.2 — committed `global.json`

`git show HEAD:global.json`:

```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "disable"
  }
}
```

Working tree differs only in `"rollForward": "latestPatch"` (the owner's local edit). It is never staged.

## 0.3 — Build (Windows)

| Command | Result |
|---------|--------|
| `dotnet build HRManagement.slnx` | **0 errors**, 14 warnings: 4 × `NU1903` (SSH.NET 2025.1.0, GHSA-mggc-4xg6-vcxf / GHSA-q939-rpr3-3284, via Testcontainers 4.13.0 in `HRManagement.IntegrationTests`) and 10 compiler/analyzer warnings (each reported twice): `CS0108` (`FileReferenceRecord.CreatedAt`), `CS8321` (unused local function `T`), `CS8604` ×2 (`PayrollPartitionInputLoader.ReadEntityProperty`), `CS9113` (`regimeValidator` unread), `CS9124` (`tenantId` captured), `MVC1004` ×2 (`AssetEditFormViewModel`, `AssetLegacyCreateFormViewModel`) |
| `dotnet build HRManagement.slnx -warnaserror` | **fails at restore**: `NU1903` ×2 as errors (`HRManagement.IntegrationTests.csproj`). The compiler warnings above are not reached. Note: `reports/plan25-baseline.md` said "no compiler warning once restore succeeds"; that is no longer true — they must be resolved for `-warnaserror` in P6 |

## 0.4 — Unit tests (Windows / ar-SA)

`dotnet test src/Tests/HRManagement.UnitTests` → **Total 4141, Passed 4125, Failed 16, Skipped 0** (1 m 7 s).

| # | Failing test | Plan §3 row | Root cause (confirmed) |
|---|--------------|-------------|------------------------|
| 1 | `Architecture.BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard` | BOLA | 12 `Index(employeeId)` actions without a scope guard (§4) |
| 2 | `Ten002ArchitecturalTests.IgnoreQueryFilters_only_in_sanctioned_bypass` | Ten002 | `IgnoreQueryFilters()` in `BenefitPlanSeeder.cs:26`, `AcceptanceCandidateExperiencePatch.cs:42` |
| 3 | `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider` | Syr001 | container has no `HRCoreDbContext` |
| 4 | `Str005ProductionBindingTests.GoLiveDeclared_passes_with_multi_seat_evidence` | Str005 | real matrix holds the expired `EX-STR005-SIM-INCIDENT-WINDOW` (G-QA-02) |
| 5 | `Str005ProductionBindingTests.Runtime_validate_script_passes` | Str005 | same, via `str005-governance-runtime-validate.ps1` |
| 6 | `Str005ProductionBindingTests.Startup_fails_on_expired_declared_exception` | Str005 (row 6) | `DateTime.TryParse("2020-01-01")` under ar-SA reads a **Hijri** year 2020 (≈ Gregorian 2582), so the "expired" exception looks valid and nothing throws |
| 7 | `Str005GovernancePackTests.Exceptions_have_expiry_and_are_not_past_asOf` | Str005 pack | `DateTime.Parse` with the thread culture |
| 8 | `LearningCompletionRulesTests.Certificate_Number_Is_Deterministic_And_Formatted` (`MissingModulesRulesTests.cs`) | MissingModules | ar-SA formats `{completedOn:yyyyMMdd}` with the Hijri calendar (product defect → TRN, wave 2) |
| 9 | `San001SanctionsTests.Validate_script_passes` | San001 | `san001-validate.ps1` reads the working-tree `global.json` (local `latestPatch`) |
| 10–16 | `ArabicPdfGenerationTests.Pdf_arabic_validate_script_passes`, `Doc001DocumentsTests.Doc001_validate_script_passes`, `Iam002OrgScopeTests.Iam002_validate_script_passes`, `Iam005LifecycleTests.Iam005_validate_script_passes`, `Prv003BreachTests.Prv003_validate_script_passes`, `Psirt001VulnerabilityTests.Psirt001_validate_script_passes`, `Rel006ReleaseTrainTests.Rel006_validate_script_passes` | the seven | `powershell -File` without `-ExecutionPolicy Bypass` |

`Hcm029CandidateAcceptedV4Tests.External_hire_publishes_v4_with_the_offer_terms` (flaky per the plan) **passed** in this run.
**No extra unit failure** beyond the plan's §3 table (row 6 of the gap study, "probably green on Linux", is red here as listed).

## 0.4 — Integration tests, `Category!=Testcontainers` (Windows / ar-SA)

→ **Total 258, Passed 230, Failed 24, Skipped 4** (1 m 51 s).

| Group | Tests | Plan §5 | Root cause |
|-------|-------|---------|-----------|
| `IHrCoreActiveRegimeClient` not registered (`PublishSeparationHandler`) | `ContractLifecycleIntegrationTests` ×4 (`PublishSeparation_AlreadyPublished_ReturnsError`, `…_Complete_PublishesOnce_IdempotentOnRetry`, `…_IncompleteChecklist_…`, `UpdateAndDeleteSeparation_AfterPublish_AreBlocked`), `Fun002SeparationRecordIntegrationTests` ×2, `Int003ProcessSagaTests.DOM001_…`, `EndToEndReleaseWorkflowSmokeIntegrationTests` | 8 ✓ | as planned |
| same root cause — **extra** | `ContractSeparationChainIntegrationTests.ActivateContract_PublishesEvent_CreatesPayProfile`, `InboundEventConsumerTests.TransactionRegisteredInDiwan_Updates_ServiceTransaction` | +2 | `MassTransitTestScope` builds with `ValidateOnBuild`, so *any* host that registers the HR.Contracts MediatR handlers fails, even if the test never publishes a separation |
| `IHrCoreServiceRegimeClient` not registered (payroll, SQLite) | `PayrollRunLifecycleIntegrationTests.PayrollRun_Draft_Calculate_Post_Lifecycle`, `Perf001PayrollBatchTests` ×4, `Pay003ResumablePayrollRunTests` ×4 | 10 planned, **9** here | "تعذر التحقق من أنظمة الخدمة — عميل HR.Core غير مهيأ" (`PayrollRegimeGovernance`). The plan's 10th was `ContractSeparationChain…`, which fails on the contracts client instead |
| `IFundedPositionVersionService` not registered | `EstablishmentHireIntegrationTests.HirePartyCompensate_…`, `…HirePartyVerify_FaultsWhenPositionNotOccupiedByEmployee` | 2 ✓ | as planned |
| `Mock.Of<IHrCoreActiveRegimeClient>()` returns a null list | `SoDForgedApproverTests.TransferApprove_IgnoresForgedFormField_UsesTokenOnly` | 2 planned, **1** here | `SoDPolicyIntegrationTests` (`:115`) **passed** — its path does not reach `regimes.FirstOrDefault()`; it is still switched to the fake in P5 |
| **extra** — event count off by one | `InternalHireClosureIntegrationTests.InternalHireClosure_VacatesPreviousPosition_AssignsTarget_PublishesPlacementEvent` (`InternalChannelIntegrationTests.cs:325`, expected 1, actual 2), `RecruitmentHireClosureIntegrationTests.HireClosure_AssignsPosition_ClosesRequisition_ConvertsOffer_PublishesV3Events` (`:59`, expected 2, actual 3) | not in plan | hypothesis: the hire-closure flow now publishes one more message than these tests count (an event added by a later plan, e.g. the funded-position/temporal version or a v4 contract), i.e. a stale test count; root-caused in P5 |

`Hcm028AppealGovernanceMigrationTests` is `Category=Testcontainers`, so it appears in the next table, not here.

## Testcontainers (`RUN_TESTCONTAINERS=true`, `Category=Testcontainers`, Windows host)

→ **Total 82, Passed 72, Failed 10** (1 m 49 s).

| Group | Tests | Plan | Root cause |
|-------|-------|------|-----------|
| payroll on PostgreSQL, no `IHrCoreServiceRegimeClient` | `Pay021DeterminismPostgreSqlTests.DualRun_…`, `Pay022ResumePostgreSqlTests.DoubleStart_…`, `Pay022ResumePostgreSqlTests.Pause_Resume_…`, `Pay023FrozenRunIsolationTests.Frozen_run_unchanged_…`, `Perf001PayrollBatchPostgreSqlTests.ParallelPartitions_…` | 4 planned, **5** here | same as SQLite payroll (`Pay022` ×2 fail downstream: "التشغيل غير موجود" / "Sequence contains no elements") |
| ar-SA date in SQL | `Hcm028AppealGovernanceMigrationTests.Legacy_appeal_notes_become_…` | ✓ | thread-culture date format writes a Hijri year |
| MinIO image not pullable | `Qa003MinioFileScanTests` ×3 | ✓ (D-QA-7) | `pull access denied for minio/minio` |
| MinIO image not pullable — **extra** | `FileStorageInboxIdempotencyTests.FileStorage_Inbox_Duplicate_Message_Single_Effect` | not in plan | shares the PostgreSQL+MinIO fixture; goes to `Category=ObjectStorage` with `Qa003` in P7 |

## 0.5 — Linux baseline

Recorded after P2 (needs `scripts/ci-local.ps1`); see the section appended below.

## 0.6 — Dated-obligations inventory

`git grep -E '"(expiresOn|nextDue|reviewBy|renewBy|expiryDate|validUntil|validTo)"' -- '*.json'` (plus `appsettings*.json`,
`*.yml`, `supply-chain-exceptions.json`, which hold none):

| File | Key(s) | Value(s) | Compared with the clock by |
|------|--------|----------|----------------------------|
| `docs/str-governance/governance-matrix.json` | `exceptions[].expiresOn` | 2026-10-30, **2026-09-15 (expired)** | `str005-governance-runtime-validate.ps1` (CI step 17, before Restore); `ProgramGovernanceStartupValidator` (Gateway/HR.Core/Payroll, Production+Staging, `EnforceOnStartup=true`) |
| `docs/str-legal/legislative-watch-stamp.json` | `nextDue` | 2027-01-31 | `str002-control-runtime-validate.ps1` (CI); `LegalControlStartupValidator` (Production) |
| `docs/str-legal/control-catalog.json` | `exceptions[].expiresOn` ×6 | 2027-01-31 | **nothing** (`LegalControlCatalogProvider.IsExceptionActive` ignores the date) — registered anyway so the date cannot silently lapse |
| `docs/mig-inventory/source-inventory.json` | `exceptions[].expiresOn` | 2027-06-30 | presence only (`mig001-*-validate.ps1`) |
| `testdata/mig001/exceptions/signed-unsupported.json` | `expiresOn` | 2027-06-30 | presence only (fixture) |
| `testdata/mig002/signoff/legal-business-signoff.json` | `expiresOn` | 2027-07-30 | presence only (fixture) |
| `docs/syr/currency-era.json` | `oldCurrency.validUntil` | 2026-07-30 | historical fact (end of the old lira era), not an obligation |

**Found beyond the plan's seven keys** (a broader sweep for date-valued keys):

| File | Key | Value | Compared with the clock by |
|------|-----|-------|----------------------------|
| `docs/sup-contracts/vendor-contract-registry.json` | `vendors[].licenseExpiry` | 2027-07-31, 2028-06-30, 2027-07-31 (3 × null) | `scripts/sup004-license-audit.ps1:43-49` (`[datetime]::Parse`, today) — **run by the unit test `Sup004ContractTests`**, i.e. a unit-test time bomb |
| same | `vendors[].eol.eolDate` | 2027-06-30 (rabbitmq), 2027-07-31, 2027-12-31 ×2, 2028-11-09 (1 × null) | same script `:52-54` (fails once an EOL date passes) |
| `testdata/str005/scenarios/incident.json` | `postReviewBy` | 2026-09-15 | nothing (scenario narrative) |

The full, verified list from an independent read-only sweep (scripts, tests, product, data) is folded into
`docs/qa/dated-obligations.json` in P1.

## 0.5 — Linux baseline (`scripts/ci-local.ps1`, after P2)

Run: `powershell -File scripts/ci-local.ps1 -ContinueOnError -Source WorkingTree -Out artifacts/ci-local/baseline`
(working tree = `396e73f` + the uncommitted P2 runner files and the P3–P5 edits in progress at that moment; the
committed `global.json` is restored inside the container, so the SDK is 10.0.400). The final copy-back of
`summary.json` was lost because `ci-local-entry.sh` was edited while bash was still reading it (fixed: the entry script
now re-executes from a private copy); the step table and trx counts below come from the run's console log
(`artifacts/plan34/ci-local-baseline.log`, git-ignored).

**87 steps: 78 passed, 9 failed, total 965 s.** Testcontainers ran inside the container against sibling containers
(`TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`), so `-SkipTestcontainers` was not needed.

| Failed step | Cause | Container artifact? |
|-------------|-------|--------------------|
| Icon button accessible names (UX-002-E) | `HR.Core.Web/Views/OfferLetters/Index.cshtml:39` icon-only control without an accessible name — the Python check never ran on the owner's machine | no — real finding |
| Check formatting | 657 WHITESPACE + 29 FINALNEWLINE findings in 143 files (part of them CRLF copies of files edited on Windows in the working-tree overlay) | partly |
| Check pending EF model changes | 4 contexts (HRCore, Training, IDCard, Archive) could not be created at design time: the web host throws "HrGateway:InternalBaseUrl must use Docker service DNS … when running in a container" because the SDK image sets `DOTNET_RUNNING_IN_CONTAINER=true`. The other 11 contexts: "No changes have been made to the model since the last migration" | yes — fixed in `ci-local-entry.sh` (unset; GitHub runners are VMs) |
| Check EF migration discoverability | `File.Storage.Infrastructure/Migrations/20260824180000_INT002_IntegrationMessageReceipts.cs` has no `[DbContext]`/`[Migration]` attribute, so EF never applies it (also why `Qa003MinioFileScanTests.Clean_…` hit "relation IntegrationMessageReceipts does not exist") | no — real product defect |
| Unit tests | 4170 total, **2 failed**: `BolaArchitectureTests` (§4) and **`Hcm030CandidateDocumentTests.Long_file_names_are_sanitized_and_shortened_keeping_the_extension`** — `FileUploadHelper.SanitizeFileName` uses `Path.GetFileName`, which on Linux keeps `C:\fakepath\cv.pdf` whole | no — real product defect, Linux only |
| Integration tests (MassTransit InMemory) | 258 total, **52 failed**: the 24 Windows ones (minus `Hcm028`, Testcontainers) + 28 WebApplicationFactory tests of HR.Core (`Hcm025OrgScope` ×6, `Hcm025PostingPolicies` ×3, `Hcm033LabScaleBehaviour` ×7, `Est004…` ×4, `HRCoreJwtBearer…` ×2, `QuarantineBlocksOperation` ×2, `TenantQuarantineResolveApi` ×2, `CrossTenantApiPenetration`, `FileUploadLifecycle`) failing on the same container DNS guard | the 28 extra: yes |
| Integration tests (Testcontainers) | 82 total, **18 failed**: payroll PG ×5, MinIO ×4 (`Qa003MinioFileScan` ×3, `FileStorageInboxIdempotency`), `Hcm025ProductionApproval…` ×5 (container DNS guard), `Qa003KeycloakTokenMatrixTests` ×4 ("bind source path does not exist: /tmp/qa003-realm-….json" — the test writes the realm to the container's `/tmp`, which the Docker daemon cannot see) | Hcm025 + Keycloak: yes — fixed (unset; same-path temp mount `TMPDIR=/tmp/hrm-ci-local`) |
| Coverage gate | not reached meaningfully (test steps failed) | — |
| Security scan (known vulnerabilities) | NU1903 SSH.NET (P6) | no |

`Hcm028AppealGovernanceMigrationTests` **passes on Linux** (invariant culture) and fails on Windows/ar-SA — the culture
asymmetry the plan describes. The unit-test differences are the same story in reverse: 14 of the 16 Windows failures
are environment-only (policy, culture, local `global.json`), while Linux has one failure Windows hides (`Hcm030`).
