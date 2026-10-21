# PLAN-26 — Session 1 handoff (A, B, C, F1)

**Date:** 2026-10-21
**Branch:** `feature/plan-26-assessment-interview-requisition` (local only — not pushed)
**Base:** `main` @ `9cb3705`

## 1. Commits

| SHA | Message |
|-----|---------|
| `544ba7a` | chore: snapshot working tree before PLAN-26 |
| `4c62b4e` | feat(hcm026): assessment criteria domain + rules + tests |
| `65de9aa` | feat(hcm026): criteria persistence, service, publish gate, UI |
| `75c2f33` | feat(hcm026): structured multi-assessor scoring |
| `d89d6fa` | feat(hcm026): interview lifecycle |
| `dd48fb5` | chore(hcm026): seeders for criteria and scores |
| *(this commit)* | docs(hcm026): session 1 handoff |

Run `git log --oneline 9cb3705..HEAD` for the final SHA of this handoff commit.

## 2. Migrations (HR.Core.Infrastructure/Migrations)

| Migration | Schema | Data step |
|-----------|--------|-----------|
| `20261021104312_HCM026_CriteriaGovernance` | `RecruitmentAssessmentCriteria` + `IsActive` (bool, default false), `LockedAt` (timestamptz?), `ChangeReason` (varchar 500), `ApprovedBy` (uuid?); filtered unique index `IX_RecruitmentAssessmentCriteria_Tenant_Requisition_Active` on `(TenantId, RequisitionId)` where `"IsActive" = true`. Existing `(TenantId, RequisitionId, VersionNumber)` unique index kept. | Npgsql-guarded: (1) `IsActive = true` on the highest `VersionNumber` per `(TenantId, RequisitionId)`; (2) `LockedAt = now()` on versions referenced by an application that has a legacy `AssessmentScore` (scores were already recorded on them). No existing values are rewritten — only the new columns are filled. |
| `20261021104948_HCM026_AssessmentScores` | New table `RecruitmentAssessmentScores` (see §3). FK application → **cascade**; FK criteria → **restrict**. Unique `(TenantId, RecruitmentApplicationId, AssessorUserId, ItemKey)`. `Score numeric(7,2)`. | none |
| `20261021105622_HCM026_InterviewLifecycle` | `RecruitmentInterviewSlots` + `Mode` (int, default 0), `CompletedAt`, `Outcome` (int?), `OutcomeNotes` (varchar 2000), `CancelledReason` (varchar 500), `RescheduledFromSlotId` (self-FK, **NO ACTION**). New table `RecruitmentInterviewPanelMembers` (FK slot → cascade, FK employee → restrict, unique `(SlotId, EmployeeId)`). | none |

`InterviewSlotStatus` / `InterviewMode` / `InterviewOutcome` are stored as **int** (like the existing slot status). `RecruitmentRequisition.Status` is still stored as **string** — relevant to session 2's partial index (`"Status" IN ('Draft','PendingApproval','Approved','Published')`).

RLS: new tenant tables follow the existing HRCore convention (no `EnableRls` in the migration); `scripts/run-migrations.sh` applies `scripts/sql/ten002c-harden-tenant-rls.sql` afterwards, which covers every table with `TenantId`.

`dotnet ef migrations has-pending-model-changes` → **No changes**.

## 3. New and changed types

### Domain (`src/Services/HRCore/HR.Core.Domain`)
| Type | Path | Notes |
|------|------|-------|
| `AssessmentStage` (new enum) | `Enums/AssessmentStage.cs` | Screening=0, Exam=1, Interview=2, Experience=3, Other=4 (Arabic `[Display]`) |
| `AssessmentCriteriaItem` (record) | `Recruitment/AssessmentCriteriaItem.cs` | `(Key, NameAr, Weight, MaxScore, Stage, IsMandatory = true, Description = null)` |
| `AssessmentCriteriaSerializer` | `Recruitment/AssessmentCriteriaSerializer.cs` | camelCase JSON, stage as string; reads legacy `[{"name","weight"}]` → `item-{n}`, Max 100, Stage Other, mandatory. Malformed JSON → empty list |
| `AssessmentCriteriaRules` | `Services/AssessmentCriteriaRules.cs` | `Validate(items) → string?`; `RequiredBeforePublishMessage` |
| `AssessmentScoringRules` | `Services/AssessmentScoringRules.cs` | `ComputeAssessor`, `ComputeFinal`, `FormulaDescription` |
| `SelectionWorkflowRules` (changed) | `Services/SelectionWorkflowRules.cs` | + `AssessedIsAutomaticMessage`, `SystemOnlyTargets = { Assessed }`; `GetAllowedTargets` excludes system-only targets. `CanTransition` unchanged (the scoring service uses it) |
| `RecruitmentAssessmentCriteria` (changed) | `Entities/RecruitmentAssessmentCriteria.cs` | + `IsActive`, `LockedAt`, `ChangeReason`, `ApprovedBy` |
| `RecruitmentAssessmentScore` (new) | `Entities/RecruitmentAssessmentScore.cs` | `RecruitmentApplicationId`, `CriteriaVersionId`, `ItemKey`(64), `AssessorUserId`, `AssessorEmployeeId?`, `Score`, `Notes`(1000), `NoConflictDeclared`, `SubmittedAt` |
| `RecruitmentInterviewSlot` (changed) | `Entities/RecruitmentInterviewSlot.cs` | + `Mode`, `CompletedAt`, `Outcome`, `OutcomeNotes`, `CancelledReason`, `RescheduledFromSlotId` (+ nav), `PanelMembers` |
| `RecruitmentInterviewPanelMember` (new) | `Entities/RecruitmentInterviewPanelMember.cs` | `SlotId`, `EmployeeId`, `Role` ("Chair"/"Member", 20), `CoiDeclared` |
| `InterviewSlotStatus` (changed) | `Enums/InterviewSlotStatus.cs` | appended `Rescheduled = 3` |
| `InterviewMode`, `InterviewOutcome` (new) | `Enums/InterviewMode.cs`, `Enums/InterviewOutcome.cs` | InPerson/Remote; Attended/NoShow |

### Infrastructure (`src/Services/HRCore/HR.Core.Infrastructure`)
| Type | Path |
|------|------|
| `RecruitmentOptions`, `RecruitmentJobsOptions` (new) | `Services/Recruitment/RecruitmentOptions.cs` |
| `RecruitmentAssessmentCriteriaService` (new) | `Services/Recruitment/RecruitmentAssessmentCriteriaService.cs` |
| `RecruitmentScoringService`, `AssessmentSummary`, `AssessorSummary` (new) | `Services/Recruitment/RecruitmentScoringService.cs` |
| `RecruitmentInterviewService` (rewritten) | `Services/Recruitment/RecruitmentInterviewService.cs` |
| `RecruitmentRequisitionLifecycleService` (changed) | `Services/Recruitment/RecruitmentRequisitionLifecycleService.cs` — publish gate, new ctor param |
| `RecruitmentSelectionService` (changed) | `Services/Recruitment/RecruitmentSelectionService.cs` — rejects manual `Assessed`; `RecordAssessmentAsync` now `internal`, documented "legacy — not exposed; PLAN-26" |
| `RecruitmentSeedCriteria` (new, internal) | `Seeding/RecruitmentSeedCriteria.cs` |
| `HRCoreDbContext` (changed) | `Data/HRCoreDbContext.cs` — `RecruitmentAssessmentScores`, `RecruitmentInterviewPanelMembers` DbSets and mappings |
| `AcceptanceDataSeeder`, `DemoDataSeeder` (changed) | `Seeding/AcceptanceDataSeeder.cs`, `Data/DemoDataSeeder.cs` |

### Web (`src/Services/HRCore/HR.Core.Web`)
| Item | Path |
|------|------|
| `RecruitmentAssessmentCriteriaController` (new) | `Controllers/RecruitmentAssessmentCriteriaController.cs` — class `[Authorize(HrCoreReadOnly)]`; `GET Index(requisitionId)` → redirect to requisition details; `POST Create` (`HrCoreWrite` + runtime `HrCoreApprove` when the active version is locked) |
| `RecruitmentRequisitionsController` (changed) | builds `CriteriaPanel` in `Details`; new ctor dep `RecruitmentAssessmentCriteriaService` |
| `RecruitmentApplicationsController` (changed) | removed `RecordAssessment`; added `SubmitScores`, `ScheduleInterview(ScheduleInterviewForm)`, `RescheduleInterview`, `CancelInterview`, `CompleteInterview`; new ctor deps `RecruitmentScoringService`, `IAuthorizationService` |
| View models | `ViewModels/RecruitmentAssessmentViewModels.cs` (new: `AssessmentCriteriaPanelViewModel`, `AssessmentCriteriaVersionRow`, `AssessmentCriteriaCreateForm`, `AssessmentCriteriaItemInput`, `AssessmentScoringPanelViewModel`, `SubmitScoresForm`, `ScheduleInterviewForm`, `InvariantDecimal`); `RecruitmentSelectionViewModels.cs` (`CriteriaOptions` and `RecordAssessmentForm` removed, `ScoringPanel` added); `RecruitmentRequisitionViewModels.cs` (`CriteriaPanel` added) |
| Partials | `Views/RecruitmentRequisitions/_AssessmentCriteriaPanel.cshtml`, `Views/RecruitmentApplications/_AssessmentScoringPanel.cshtml`, `Views/RecruitmentApplications/_InterviewsPanel.cshtml` |
| Views changed | `RecruitmentRequisitions/Details.cshtml` (panel, missing-criteria warning on Approved, script), `RecruitmentApplications/Details.cshtml` (free-form score form and old schedule card removed, new partials), `PublicCareers/TrackResult.cshtml` (Scheduled slots only) |
| JS | `wwwroot/js/recruitment-criteria.js` (dynamic rows + live weight total; no inline handlers) |
| `Program.cs` | `Configure<RecruitmentOptions>(GetSection("Recruitment"))`, `AddScoped<RecruitmentAssessmentCriteriaService>()`, `AddScoped<RecruitmentScoringService>()` |

## 4. `RecruitmentOptions` (extend this in session 2)

```csharp
// HR.Core.Infrastructure/Services/Recruitment/RecruitmentOptions.cs
public sealed class RecruitmentOptions
{
    public const string SectionName = "Recruitment";
    public bool LegacyHireWithoutOffer { get; set; }                  // Recruitment:LegacyHireWithoutOffer (false)
    public bool RejectSiblingApplicationsOnHire { get; set; } = true; // Recruitment:RejectSiblingApplicationsOnHire (true)
    public bool RequireCriteriaBeforePublish { get; set; } = true;    // Recruitment:RequireCriteriaBeforePublish
    public int MinAssessors { get; set; } = 1;                        // Recruitment:MinAssessors
    public int MaxInterviewReschedules { get; set; } = 1;             // Recruitment:MaxInterviewReschedules
    public RecruitmentJobsOptions Jobs { get; set; } = new();         // Recruitment:Jobs:* — empty, for session 2
}
public sealed class RecruitmentJobsOptions { }                        // add intake-close job settings here
```

Registered in `HR.Core.Web/Program.cs` right after `RecruitmentInterviewService`. No `appsettings*.json` keys were added (defaults apply).
The two legacy keys are still read through `IConfiguration` in `RecruitmentHireClosureService` and `RecruitmentApplicationsController.ResolveLegacyHireWithoutOffer` — unchanged.
Suggested additions for session 2: `MinPostingDays` (7) on the root, and e.g. `Jobs.IntakeClose { Enabled, RunAtUtc/Interval }`.

## 5. Public signatures session 2 will reuse or touch

```csharp
// RecruitmentRequisitionLifecycleService — ctor gained a parameter (update any new `new(...)` call sites)
RecruitmentRequisitionLifecycleService(HRCoreDbContext db, IPolicyOrchestrator policyOrchestrator,
    RecruitmentLegalSourceValidator legalSourceValidator, IWorkflowStarter workflowStarter,
    IConfiguration configuration, IOptions<RecruitmentOptions> recruitmentOptions)
Task<(bool Success, string? Error)> SubmitForApprovalAsync(RecruitmentRequisition, Guid? actorUserId, string? actorUserName, CancellationToken = default)
Task<(bool Success, string? Error)> PublishAsync(RecruitmentRequisition, Guid? actorUserId, string? actorUserName, CancellationToken = default)          // + criteria gate (line ~121)
Task<(bool Success, string? Error)> PublishInternalAsync(RecruitmentRequisition, Guid? actorUserId, string? actorUserName, CancellationToken = default)  // + criteria gate (line ~192)
Task<(bool Success, string? Error)> CloseAsync(Guid requisitionId, CancellationToken = default)    // unchanged — session 2 (G-11 cascade)
Task<(bool Success, string? Error)> CancelAsync(Guid requisitionId, CancellationToken = default)   // unchanged — session 2 (reason + HrCoreApprove + cascade)
// private ValidateCriteriaBeforePublishAsync(Guid, CancellationToken) — put closing-date validation next to it

// RecruitmentAssessmentCriteriaService(HRCoreDbContext db)
Task<(RecruitmentAssessmentCriteria? Criteria, string? Error)> CreateVersionAsync(Guid requisitionId, string? title,
    IReadOnlyList<AssessmentCriteriaItem> items, string? changeReason, Guid? actorUserId, bool actorCanApprove, CancellationToken = default)
Task<RecruitmentAssessmentCriteria?> GetActiveAsync(Guid requisitionId, CancellationToken = default)
Task<IReadOnlyList<RecruitmentAssessmentCriteria>> GetVersionsAsync(Guid requisitionId, CancellationToken = default)
Task<bool> HasActiveAsync(Guid requisitionId, CancellationToken = default)
Task<(bool Success, string? Error)> LockAsync(Guid criteriaId, CancellationToken = default)

// RecruitmentInterviewService(HRCoreDbContext db, IOptions<RecruitmentOptions> options)   — ctor changed
Task<(RecruitmentInterviewSlot? Slot, string? Error)> ScheduleAsync(Guid recruitmentApplicationId, DateTime scheduledAt (UTC),
    InterviewMode mode, string? location, IReadOnlyList<Guid> panelEmployeeIds, string? panelNotes,
    Guid? citizenApplicationId = null, CancellationToken = default)
Task<(RecruitmentInterviewSlot? Slot, string? Error)> RescheduleAsync(Guid slotId, DateTime newTime, string? reason, CancellationToken = default)
Task<(bool Success, string? Error)> CancelAsync(Guid slotId, string? reason, CancellationToken = default)
Task<(bool Success, string? Error)> CompleteAsync(Guid slotId, InterviewOutcome outcome, string? notes, CancellationToken = default)
Task<bool> HasAttendedInterviewAsync(Guid applicationId, CancellationToken = default)
Task<IReadOnlyList<RecruitmentInterviewSlot>> GetForApplicationAsync(Guid recruitmentApplicationId, CancellationToken = default) // includes PanelMembers.Employee

// RecruitmentScoringService(HRCoreDbContext db, RecruitmentInterviewService interviewService, IOptions<RecruitmentOptions> options)
Task<(bool Success, string? Error)> SubmitScoresAsync(Guid applicationId, Guid assessorUserId, Guid? assessorEmployeeId,
    IReadOnlyDictionary<string, decimal> scores, string? notes, bool noConflictDeclared, CancellationToken = default)
Task<AssessmentSummary> ComputeAsync(Guid applicationId, CancellationToken = default)      // persists score + auto-Assessed
Task<AssessmentSummary> GetSummaryAsync(Guid applicationId, CancellationToken = default)   // read-only
Task<bool> IsInterviewGateSatisfiedAsync(Guid applicationId, IReadOnlyList<AssessmentCriteriaItem> items, CancellationToken = default)
const string InterviewGateMessage
```

When session 2 cancels/closes a requisition and rejects open applications, note that `Scheduled` interview slots of those applications are **not** cancelled automatically today — call `RecruitmentInterviewService.CancelAsync(slotId, "إلغاء الشاغر")` for them.

## 6. Test results

| Run | Baseline (`544ba7a`) | Now |
|-----|----------------------|-----|
| Prompt filter `Recruitment\|Hcm\|SelectionWorkflow\|RequisitionWorkflow\|OfferWorkflow\|Est0\|Wf02` | 232 passed / 0 failed | **280 passed / 0 failed** |
| Wide regression (+ `PublicCareers\|Fun001\|Syr001`) | 270 passed / 2 failed | **318 passed / 2 failed** (same 2) |
| `Hcm026*` | — | **47 passed** |
| Full unit suite (now) | not run at baseline | 2767 passed / 8 failed — see below |

Pre-existing failures (unchanged): `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50`, `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider` (confirmed on the snapshot).
Other full-suite failures, all in files this session did not touch (not re-run on the snapshot): `BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard` (offenders in HRContracts/Payroll controllers), `Ten002ArchitecturalTests.IgnoreQueryFilters_only_in_sanctioned_bypass` (`BenefitPlanSeeder.cs`), `San001SanctionsTests.Validate_script_passes`, `Prv001RopaTests.Ropa_covers_all_fourteen_services_and_high_risk_dpias`, `Str005ProductionBindingTests` ×2.
Note: running the full suite rewrites tracked `testdata/*/out/*.json` reports — restore them with `git checkout -- testdata` before committing.

Build: `dotnet build HRManagement.slnx` → 3 errors, all pre-existing in `HRManagement.AcceptanceTests` (`PostAsJsonAsync`, see `reports/plan26-baseline.md`). All service, unit and integration projects build with 0 errors.
Integration tests were compiled but **not run** (they need the Docker/Postgres stack).

New test classes: `Hcm026AssessmentCriteriaRulesTests` (10), `Hcm026CriteriaServiceTests` (8), `Hcm026ScoringServiceTests` (14), `Hcm026InterviewLifecycleTests` (15 incl. theory cases; also holds the interview-gate scoring test and a full shortlist→Ranked path without seed scores); `AcceptanceSeedTests.Acceptance_seed_scores_match_assessment_formula` (1).

Existing tests modified:
- `RecruitmentSelectionServiceTests.RecordAssessment_Snapshots_CriteriaVersion` → replaced by `TransitionStatus_to_Assessed_is_rejected_and_status_unchanged` and `AllowedTargets_never_offer_Assessed` — the free-form path is intentionally closed (B3).
- `Hcm021RequisitionApprovalUiTests`, `Syr001RecruitmentLegalSourceTests` — pass `Options.Create(new RecruitmentOptions())` to the new lifecycle ctor parameter (no behaviour change; they don't publish).
- `IntegrationTests/InternalChannelIntegrationTests`, `IntegrationTests/RecruitmentRequisitionPolicyIntegrationTests` — `RequireCriteriaBeforePublish = false`; they test channels/policy, not criteria (A4).
- `IntegrationTests/PublicCareersApplyTests`, `PublicCareersRequisitionGateTests`, `RecruitmentTestControllers` — new ctor arguments (`RecruitmentOptions`, `RecruitmentScoringService`, an allow-all `IAuthorizationService` mock).

## 7. Deviations, known issues, markers

Deviations:
- **Self-FK `RescheduledFromSlotId` uses `NO ACTION`, not `RESTRICT`.** Both block deleting a referenced slot, but Postgres checks RESTRICT immediately, so cascading an application delete across a reschedule chain would fail; NO ACTION is checked at end of statement.
- **Commit 3 shipped scoring without the interview gate.** The gate needs `InterviewOutcome`, so it landed in commit 4 together with its test. Each commit builds and is green.
- The criteria migration also **locks** versions already used by legacy scores (not in the prompt) — consistent with "locked on first score".
- `RecruitmentAssessmentCriteriaController` has no `Activate`/`Details` actions (the plan lists them). New versions become active on creation, and versions are immutable, so the panel shows each version's items inline.
- Decimal inputs (weights, max scores, scores) are posted as strings and parsed culture-invariantly (`InvariantDecimal`), because the default request culture is `ar-SY`.
- `datetime-local` values are interpreted in the **server's local time** and converted to UTC (`LocalInputToUtc`), matching how slots are displayed (`.ToLocalTime()`).
- Assessor identity = the `sub`/NameIdentifier claim; `AssessorEmployeeId` = `HrOrgScopeVisibility.GetEmployeeId(User)`. Any `HrCoreWrite` user can score; there is no restriction to panel members yet (PLAN-28 links committees to scores).
- Seeded criteria use two items: `interview` (Interview, 40 %, max 100) and `experience` (Experience, 60 %, max 100). For pre-set Assessed/Ranked/Offer applications the acceptance seeder also adds a completed `Attended` interview slot, so data stays consistent with the interview gate.

Known issues / limits:
- Existing (already-seeded) acceptance databases are not re-seeded (fingerprint unchanged). Their legacy-scored applications keep `AssessmentScore` but have no `RecruitmentAssessmentScore` rows; the scoring panel shows no assessors for them.
- If a criteria version is replaced after an application was assessed, the application stays pinned to its old version until an assessor submits again. Resubmission re-pins it and recomputes; `AssessmentScore` becomes null until an assessor is complete on the new version.
- UI was verified by compilation and service-level tests only — not exercised in a browser (no local Postgres/Keycloak running).
- Local-only: `global.json` has `rollForward: latestPatch` (uncommitted) because SDK 10.0.400 isn't installed here.

Markers left:
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentInterviewService.cs:194` — `// PLAN-30: notify the candidate of the outcome.`
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentInterviewService.cs:46` — `// PLAN-26:` note on the removed regression.

## 8. Before starting session 2

- Branch from / continue on `feature/plan-26-assessment-interview-requisition`. Nothing is pushed.
- Session 2 scope remains: D (requisition integrity, `BlockingStatuses`, partial unique index, cancel cascade, establishment cascade), E (`ApplicationClosingDate`, `MinPostingDays`, `RecruitmentIntakeCloseJob`), F2, integration + acceptance tests, docs (`HCM_001_RECRUITMENT.md`, `HIRE_ONBOARDING_GUIDE.md`), scripts (`hcm026-validate.ps1`, `hcm026-find-duplicate-requisitions.ps1`).
- New migrations must be generated **after** `20261021105622_HCM026_InterviewLifecycle`. `dotnet ef migrations remove` needs a DB connection; without one, delete the files and `git checkout` the model snapshot.
- Any new `new RecruitmentRequisitionLifecycleService(...)`, `new RecruitmentInterviewService(...)` or `new RecruitmentScoringService(...)` in tests needs the options/ctor arguments listed in §5. Any test that publishes needs active criteria, or `RequireCriteriaBeforePublish = false`.
- Acceptance scenarios AC-HCM-26a..e are covered at unit level only; session 2 should add the integration/acceptance evidence (`AcHcm26AssessmentInterviewTests`).
- The demo seeder now creates active criteria for its Approved and Published requisitions. Session 2's closing-date work should also set `ApplicationClosingDate` there and in the acceptance seeder (not done here, as instructed).
