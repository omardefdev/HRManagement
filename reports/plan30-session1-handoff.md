# PLAN-30 — Session 1 handoff (Step 0, A, B, C, D)

**Date:** 2026-10-23
**Branch:** `feature/plan-30-candidate-experience` (local only — not pushed), created from `main` @ `55fbcdd` (contains PLAN-26..29).

## 1. Commits

| SHA | Message |
|-----|---------|
| `0d58720` | feat(hcm030): unified intake — public and internal applications enter the recruitment path at Submitted (includes `reports/plan30-baseline.md`) |
| `f219fed` | feat(hcm030): application event log and candidate public status projection |
| `9f73536` | feat(hcm030): backfill of unlinked channel applications (script + SQL + tests) |
| `acf9e7d` | feat(hcm030): application profile, requisition criteria and eligibility flags |
| *(this commit)* | docs(hcm030): session 1 handoff (+ the `// PLAN-30 S2/S3` marker comments, no behaviour change) |

Not committed (local noise): `global.json`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, `tmp-vendors.html`, `testdata/*/out/*.json` (restored with `git checkout -- testdata` before each commit).

## 2. Migrations and columns

Both after `20261022162341_HCM029_OfferDocument`. `dotnet ef migrations has-pending-model-changes` → **No changes**.

### `20261022231545_HCM030_UnifiedIntake`

| Table | Change |
|-------|--------|
| `RecruitmentApplications` | + `IntakeChannel` `character varying(20)` not null, default `'HrEntered'` (enum `RecruitmentIntakeChannel` stored as string: `HrEntered`, `Public`, `Internal`, `Legacy`) |
| `RecruitmentApplications` | Postgres-only backfill of `IntakeChannel`: linked from `CitizenApplications` → `Public`; `SourceInternalApplicationId` set (or linked from `InternalApplications`) → `Internal`; `IsLegacyUnlinked` → `Legacy`; otherwise `HrEntered`. Only the new column is written. |
| `CitizenApplications`, `InternalApplications` | `IX_*_PromotedRecruitmentApplicationId` recreated **unique**, filter `"PromotedRecruitmentApplicationId" IS NOT NULL`. A Postgres `DO` block runs first and **raises** (no repair) if duplicates exist. |
| `RecruitmentApplicationEvents` (new) | see §4; FK → `RecruitmentApplications` (cascade); index `(TenantId, RecruitmentApplicationId, OccurredAt)`; filtered index `IX_RecruitmentApplicationEvents_NotificationsPending` on `OccurredAt` where `"NotificationsPlannedAt" IS NULL` |

**Duplicate-link check (before adding the indexes)**, read-only (`default_transaction_read_only = on`) against the local acceptance database `hr_core_db` (container `hrm-acceptance-postgresql-1`):

| Table | Rows | Linked | Duplicate links |
|-------|------|--------|-----------------|
| `CitizenApplications` | 1 | 0 | **0** |
| `InternalApplications` | 1 | 0 | **0** |
| cross-channel (citizen and internal row on the same application) | — | — | **0** |

No other database was available. On any other environment the migration's guard re-checks and stops with `HCM030: duplicate …` if needed.

### `20261022234315_HCM030_ApplicationProfile` (additive, all nullable, no data step)

| Table | Columns |
|-------|---------|
| `CitizenApplications` | `EducationLevelCode` varchar(20), `Specialization` varchar(200), `GraduationYear` int, `YearsOfExperience` numeric(4,1), `BirthDate` date |
| `RecruitmentApplications` | same five (snapshot) + `EligibilityFlags` varchar(200), `EligibilityEvaluatedAt` timestamptz, `EligibilityOverrideReason` varchar(1000) |
| `RecruitmentRequisitions` | `MinQualificationCode` varchar(20), `MinYearsOfExperience` numeric(4,1), `MinAge` int, `MaxAge` int |

`BirthDate` carries `[AuditRedact]` on both entities (the audit interceptor writes `***` for it).

## 3. `UnifiedIntakeService` and the status mapping

`src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/UnifiedIntakeService.cs` — scoped, ctor `(HRCoreDbContext db, IOptions<RecruitmentOptions> options)`.

| Member | Behaviour |
|--------|-----------|
| `RecruitmentApplication CreateFromCitizen(CitizenApplication, RecruitmentRequisition)` | Stages (Add, no save) a `Submitted` application: `TenantId` = citizen's (explicit — portal has no ambient tenant), name, `NationalId`/`NationalIdHash`, `RequisitionId`, `AppliedPosition` = `DenormalizeJobTitle`, `AppliedAt` = `SubmittedAt`, `IntakeChannel = Public`, `Notes` = «تقديم عبر البوابة العامة {ReferenceNumber}», profile snapshot, eligibility flags; sets `citizen.PromotedRecruitmentApplication`. |
| `RecruitmentApplication CreateFromInternal(InternalApplication, Employee, RecruitmentRequisition, IEnumerable<string?>? employeeQualificationCodes = null)` | Same for internal: `IntakeChannel = Internal`, `Notes` = «ترشّح داخلي», `SourceInternalApplicationId`, `BirthDate` from `Employee.BirthDate`, `EducationLevelCode` = highest-ranked code. Links the channel row only when it is **not** new (see deviation 1). |
| `Task<RecruitmentApplication> SubmitInternalAsync(InternalApplication, Employee, RecruitmentRequisition, ct)` | ESS path: one transaction, save 1 = both rows, save 2 = the channel link. |
| `Task<RecruitmentApplication?> EnsureLinkedAsync(CitizenApplication \| InternalApplication, ct)` | Returns the linked application, creates it for a requisition-linked unlinked row with the C2 mapping (no events — suppressed, no eligibility evaluation), or returns null for a legacy row. Copies citizen COI. Saves. |
| `Task<bool> HasOpenApplicationAsync(requisitionId, nationalId, nationalIdHash, forPublicPortal, ct)` | Cross-channel duplicate: a non-terminal (`SelectionWorkflowRules.TerminalStatuses`) application with the same hash or plaintext id on the requisition. |
| `EligibilityCriteria CriteriaFor(RecruitmentRequisition)` | Requisition criteria; null age bounds fall back to `AgeBoundsByRegime[regime]`. |
| `Task<IReadOnlyList<QualificationOption>> GetQualificationOptionsAsync(tenantId, ct)` | Active MDM `Qualification` entries (tenant copy, else platform), rank order. |
| `const string ManagedByUnifiedPathMessage` | «يُدار هذا الطلب من مسار التوظيف الموحد.» |

Callers: `PublicCareersController.Apply` (one `SaveChangesAsync`), `EssTalentController.Apply` (`SubmitInternalAsync`), `RecruitmentApplicationsController.PromoteFromCitizen/PromoteFromInternal` (`EnsureLinkedAsync`; linked rows → redirect to Details with `TempData["Info"]`). `RecruitmentApplicationsController.Create` sets `IntakeChannel = HrEntered` and now also `NationalIdHash` (needed by the duplicate check). Seeders set `HrEntered` explicitly.

### Status mapping — C# (`HR.Core.Domain/Services/UnifiedIntakeStatusMapping.cs`) = SQL (`scripts/sql/hcm030-backfill-unified-intake.sql`)

Source values are shared by both channel enums: 0 Received/Submitted, 1 UnderReview, 2 Shortlisted, 3 Accepted, 4 Rejected. `src` = source `UpdatedAt`; `end` = `CancelledAt ?? ClosedDate` (cancelled) or `ClosedDate` (closed), falling back to `src`.

| Source | Requisition not cancelled/closed | Requisition `Cancelled` / `Closed` |
|--------|----------------------------------|-------------------------------------|
| 0 | `Submitted` | `Rejected`, `RequisitionCancelled` / `RequisitionClosed`, `RejectedAt = ReviewedAt = end` |
| 1 | `UnderReview`, `ScreenedAt = ReviewedAt = src` | same |
| 2 | `Shortlisted`, `ReviewedAt = src` | same |
| 3 | `Shortlisted`, `ReviewedAt = src`, note « — كان مقبولاً في قناة التقديم قبل توحيد المسار.» | same |
| 4 | `Rejected`, `SelectionDecision`, `RejectedAt = ReviewedAt = src` | `Rejected`, `SelectionDecision`, `RejectedAt = ReviewedAt = src` |

`RejectedAt` is never "now", so no appeal window reopens. Tests: `Hcm030BackfillMappingTests` (unit, C# vs this table for both enums) and `Hcm030BackfillTests` (PostgreSQL: SQL result = `UnifiedIntakeStatusMapping.Map` per seeded row).

Backfill script: `scripts/hcm030-backfill-unified-intake.ps1` (`-DryRun` default = run and ROLLBACK; `-Apply`; Npgsql or libpq connection string; CSV in `artifacts/hcm030-backfill/`). Checked end-to-end against a throwaway `postgres:16-alpine` (migrated): dry run 2 rows / 0 written, apply 2, second apply 0. The SQL locks both channel tables (`SHARE ROW EXCLUSIVE`) for its short transaction. Backfilled internal rows get `NationalId = NULL` and the employee's hash (`Employees.NationalId` is encrypted at rest).

## 4. Application event log — session 2 contract

Entity `HR.Core.Domain.Entities.RecruitmentApplicationEvent : BaseEntity` (table `RecruitmentApplicationEvents`), append-only, **no PII**:

| Column | Type | Meaning |
|--------|------|---------|
| `Id` | uuid | |
| `TenantId` | uuid | from the changed entity (portal: the posting's tenant) |
| `RecruitmentApplicationId` | uuid | |
| `EventType` | varchar(40) | `RecruitmentApplicationEventTypes.*` |
| `FromStatus` / `ToStatus` | int? | statuses of the **entity that triggered the event**: `RecruitmentApplicationStatus` (Received, StatusChanged), `InterviewSlotStatus` (Interview*), `OfferStatus` (Offer*), `RecruitmentAppealStatus` (Appeal*); `FromStatus` null for added entities |
| `RelatedEntityId` | uuid? | slot / offer / appeal id; null for application events |
| `OccurredAt` | timestamptz | UTC time of the save |
| `NotificationsPlannedAt` | timestamptz? | null; set by the session 2 dispatcher |
| `CreatedAt` / `UpdatedAt` | timestamptz | BaseEntity |

Trigger rules (`HR.Core.Infrastructure/Data/RecruitmentApplicationEventRecorder.cs`), one row each:

| `EventType` | Trigger |
|-------------|---------|
| `Received` | `RecruitmentApplication` **Added** (to = its status; Submitted at intake) |
| `StatusChanged` | `RecruitmentApplication.Status` modified to a different value (from/to) |
| `InterviewScheduled` | `RecruitmentInterviewSlot` Added with `RescheduledFromSlotId == null` |
| `InterviewRescheduled` | slot Added with `RescheduledFromSlotId` set (the original moving to `Rescheduled` records nothing) |
| `InterviewCancelled` | slot `Status` modified to `Cancelled` |
| `OfferIssued` / `OfferAccepted` / `OfferDeclined` / `OfferExpired` / `OfferRevoked` | `OfferLetter.Status` **modified** to Issued / Accepted / Declined / Expired / Revoked (Draft and PendingApproval record nothing; an offer *added* directly as Issued records nothing) |
| `AppealFiled` | `RecruitmentAppeal` Added |
| `AppealDecided` | appeal `Status` modified from `Pending` to another value |

Mechanics:
- Called from `HRCoreDbContext.SaveChanges(bool)` and `SaveChangesAsync(bool, ct)` (the parameterless overloads route there), before the interceptors, so the rows are in the same transaction and get stamped/audited like any row. Change tracker only, no queries.
- A retried save after a failure does not add duplicates (key: application, type, from, to, related id).
- **Suppression:** `using (db.SuppressApplicationEvents()) { … SaveChanges … }` — scoped to the context instance, restores the previous value on dispose; `db.ApplicationEventsSuppressed` reads it. Used by `EnsureLinkedAsync` (historic links). The SQL backfill writes no events. Use it for bulk data fixes only: suppressed changes are never notified.
- Note: `AuditSaveChangesInterceptor` audits every `BaseEntity`, so each event row also gets an `EntityAuditLog` "Created" row.
- Session 2: read `NotificationsPlannedAt IS NULL` (filtered index), plan messages, set `NotificationsPlannedAt` (an update — the only one allowed). Historic/backfilled applications have no events, so they are never notified.

## 5. `CandidatePublicView` — session 3 contract

`HR.Core.Domain/Services/CandidatePublicStatusProjection.cs` (pure). Loader: `HR.Core.Infrastructure/Services/Recruitment/CandidatePublicStatusService` — `GetViewAsync(id, forPublicPortal)` / `GetViewsAsync(ids, forPublicPortal)`; on the portal it reads with `IgnoreTenantFilterForSystem(PublicCareersPortal)` (pass only ids resolved from the candidate's reference + national id). Labels use `TimeZoneInfo.Local`.

```csharp
record CandidatePublicView(
    string Code, string LabelAr, string? DetailAr, DateTime? DeadlineUtc,
    bool CanRespondToOffer, bool CanAppeal, DateTime? AppealOpenUntil, bool CanWithdraw,
    CandidatePublicInterview? NextInterview,            // (ScheduledAtUtc, ModeAr, Location)
    IReadOnlyList<CandidatePublicTimelineStep> Timeline); // (Code, LabelAr, Reached, AtUtc, DetailAr)
```

| Internal | `Code` | `LabelAr` / `DetailAr` | `DeadlineUtc` |
|----------|--------|------------------------|---------------|
| Submitted, UnderReview | `UnderReview` | «قيد المراجعة» | — |
| Shortlisted, InterviewScheduled | `InterviewInvited` | «مدعو للمقابلة» / «موعد المقابلة: … — حضوري/عن بُعد — المكان» | next scheduled interview |
| Assessed, Ranked, OfferExtended with Draft/PendingApproval offer | `FinalEvaluation` | «قيد التقييم النهائي» | — |
| OfferExtended with **Issued** active offer | `OfferPending` | «عرض بانتظار ردك حتى yyyy/MM/dd» | offer `ExpiryDate` |
| OfferAccepted, Hired | `Accepted` | «مقبول — بانتظار استكمال التعيين» | — |
| ReserveListed | `ReserveListed` | «في قائمة الاحتياط حتى yyyy/MM/dd» | end of `ReserveUntil` |
| OfferDeclined | `OfferClosed` | «العرض منتهٍ/مرفوض» | — |
| Rejected | `NotAccepted` | «غير مقبول» / «يمكنك الاستئناف حتى …» (appealable kind and window open) or «استئنافك قيد المراجعة» (pending) | appeal deadline when open |
| Withdrawn | `Withdrawn` | «منسحب» | — |

Flags: `CanRespondToOffer` = OfferExtended + Issued + not expired; `CanAppeal` / `AppealOpenUntil` from `CandidatePublicStatusProjection.AppealWindowOf(app, now, AppealWindowDays, AppealableRejectionKinds)` (= `RecruitmentAppealWorkflowRules.GetAppealError` is null) and no pending appeal; `CanWithdraw` = not in `SelectionWorkflowRules.TerminalStatuses`.

Timeline steps (`Steps.*`): `Received`, `Review`, `Interview`, `FinalEvaluation`, `Offer`, `Appeal` (only when there is one), `Decision` (reached only for Accepted/ReserveListed/OfferClosed/NotAccepted/Withdrawn, with the label as detail). Reached steps come from the events with their dates; rows without events are synthesized from the current status, undated. The offer step is reached only by `OfferIssued` or an Issued/Accepted active offer. The view has no rank, score, committee, panel, note or salary field (test `Public_view_exposes_no_internal_data`).

Used by `/careers/track` (`TrackResult`), ESS `MyApplications`, `PublicJobPostings/Details` and `TalentMarketplace/Details` (linked rows).

## 6. Readers of `CitizenApplication.Status` / `InternalApplication.Status`

For linked rows these columns are **no longer authoritative and no longer written** (they keep the value of intake: `Received` / `Submitted`). Documented on both properties.

| Reader | What it does now |
|--------|------------------|
| `PublicCareersController.Track` + `BuildLegacyTimeline`, `Views/PublicCareers/TrackResult.cshtml` | Linked rows → `CandidatePublicView`. Unlinked rows → citizen status (the «المقابلة»/«التعيين» steps, which only meant "promoted", were removed). |
| `PublicJobPostingsController.ChangeApplicationStatus` / `DeclareCitizenCoi` (+ `CitizenSelectionWorkflowRules`) | Refuse requisition-linked rows with `ManagedByUnifiedPathMessage`; legacy rows unchanged. |
| `TalentMarketplaceController.ChangeApplicationStatus` | Same for internal rows. (There is no internal COI action.) |
| `Views/PublicJobPostings/Details.cshtml`, `Views/TalentMarketplace/Details.cshtml` | Requisition-linked rows: projected label + link to the application, no status `<select>`, no COI form; unlinked (pre-backfill) rows: «ربط بمسار التوظيف» button (promote endpoint). Legacy rows: unchanged select. Header «فرز الطلبات» → `RecruitmentApplications?RequisitionId=…`. |
| `Views/EssTalent/MyApplications.cshtml` | Projected label for linked rows; raw status only for unlinked rows; the raw internal-enum column was removed. |
| `RecruitmentApplicationsController.PromoteFromCitizen/Internal` | Old status gate removed; status read once by `EnsureLinkedAsync` (mapping). |
| `UnifiedIntakeService.EnsureLinkedAsync`, backfill SQL | Read the status once to map historic rows. |
| `DsarInternalController` (`candidates/{id}`, `candidates/by-hash/{hash}`) | Still exports the stored citizen status (data held); now adds `recruitmentStatus` (linked application) and `promotedRecruitmentApplicationId` — additive JSON fields; the Compliance consumer passes the JSON through. |
| `DemoDataSeeder`, `AcceptanceDataSeeder` (writers) | Unchanged: they create requisition-linked channel rows **without** links (pre-unification shape). Run the backfill (or the promote endpoint) after seeding. No acceptance test relies on the old promote behaviour (grepped). |
| Tests (`Hcm026ScoringServiceTests`, `Hcm029CandidateAcceptedV4Tests`, `InternalChannelIntegrationTests`, `PromoteCitizenApplicationTests`) | Fixture writes only. |

## 7. Eligibility flags and options

`HR.Core.Domain/Services/RecruitmentEligibilityRules.cs` (pure) — flags only, never rejects:

| Code | Arabic | When |
|------|--------|------|
| `BelowMinQualification` | «دون المؤهل المطلوب» | rank(held) < rank(minimum) |
| `QualificationUnranked` | «تعذّر تقييم المؤهل» | minimum or held code has no rank |
| `BelowMinExperience` | «خبرة أقل من المطلوب» | years < minimum |
| `AgeBelowMin` | «دون الحد الأدنى للعمر» | completed years on the application date < `MinAge` |
| `AgeAboveMax` | «فوق الحد الأعلى للعمر» | age > `MaxAge` (bounds inclusive) |
| `MissingProfileData` | «بيانات ناقصة» | a criterion is set and its datum is missing (once) |

Stored comma-separated in display order (`Format`/`Parse`), with `EligibilityEvaluatedAt`. Evaluated in `CreateFromCitizen`/`CreateFromInternal` (intake) only — HR-entered, backfilled and `EnsureLinkedAsync` rows stay `null` (Details shows «لم تُقيَّم»).

Options (`RecruitmentOptions.Eligibility`, no appsettings change):
- `Recruitment:Eligibility:QualificationRanks` — `Dictionary<string,int>` (case-insensitive), defaults `DIP=1, BACH=2, MASTER=3, PHD=4`; configured entries add to/override the defaults. MDM seed pack untouched.
- `Recruitment:Eligibility:AgeBoundsByRegime` — `{ "law50": { "MinAge": …, "MaxAge": … } }`, regime from `LegalSourceToRegimeMapper.MapOfferToRegime(requisition.LegalSourceId)`; used only when the requisition sets no bound. **Empty by default; no Production values** (no legal age data in the repository).

Override: `RecruitmentSelectionService.TransitionStatusAsync(id, to, legacy, rejectionNote, eligibilityOverrideReason, actorUserId, ct)` — moving a flagged application to `Shortlisted` needs a reason ≥ 10 chars (trimmed) → `EligibilityOverrideReason` + `EntityAuditLog` `Action = "EligibilityOverride"`, `ChangedFields = {"flags":[…],"reason":"…"}` only. No other code path moves an application to Shortlisted (grepped; bulk ranking/committee paths target Ranked; `EnsureLinkedAsync` rows have no flags). The Details status card shows a required textarea for the Shortlisted target when flags exist. Index: badges + filter «مع ملاحظات أهلية» (`WithEligibilityFlags`).

Requisition criteria: Create/Edit forms (`_EligibilityCriteriaFields` partial; Edit only in Draft, as before), validated (active MDM code, 0–60 years, ages 14–100, min ≤ max); shown on the public posting as «شروط التقديم». The posting now shows «آخر موعد للتقديم» from `Requisition.ApplicationClosingDate ?? ClosingDate` (D5); the closed notice was already there.

## 8. Tests

### New (all pass)

| Class | Count | Covers |
|-------|-------|--------|
| `Hcm030UnifiedIntakeTests` (unit) | 21 | public apply: both rows, one save, Submitted/Public/link/explicit TenantId; internal apply: both rows in one transaction (2 saves); legacy posting unchanged; HR-entered duplicate refused, terminal duplicate allowed; employee who applied publicly refused in ESS; promote on linked rows redirects (citizen, internal); promote links an unlinked row with COI; `EnsureLinkedAsync` mapping (8 rows incl. cancelled/closed, `RejectedAt` not now); idempotent + legacy null; status/COI change refused on linked rows (public, internal), legacy still works |
| `CandidatePublicStatusProjectionTests` (unit) | 26 | every B2 row; Draft/PendingApproval offer not an offer; issued offer label/deadline/flag; expired offer; next interview; reserve date; appeal line only when open; pending appeal; `AppealWindowOf`; AC-HCM-30b negative (no «التعيين» step, nothing reached beyond reception); synthesized timeline; event timeline dates; upheld-appeal history; withdraw flag; no internal fields |
| `Hcm030ApplicationEventLogTests` (unit) | 15 | Received; StatusChanged via selection service; no-op save; sync save; interview scheduled/rescheduled/cancelled; each offer event (5); draft/pending offer records nothing; appeal filed/decided; suppression scope; portal tenant; no PII columns |
| `RecruitmentEligibilityRulesTests` (unit) | 26 | each flag; unranked; configured ranks; experience; age edges (birthday, leap day); missing data once; null criteria; order/format; highest-ranked; apply snapshot + flags; invalid code/birth date refused; internal snapshot; regime age defaults; override required, stored and audited (flags + reason only); unflagged needs no reason |
| `Hcm030BackfillMappingTests` (unit) | 15 | C# mapping = C2 table for both enums; cancel-date fallbacks; enum names/values |
| `Hcm030BackfillTests` (integration, Testcontainers PostgreSQL) | 1 | 16 seeded channel rows (all mappings, cancelled/closed, internal) + already-linked + legacy; counts; SQL = C# per row; `RejectedAt` historic; no events; idempotent second run; both unique link indexes reject duplicates. Skips without Docker. |

### Results vs. baseline

| Run | Baseline (`main`) | Branch |
|-----|-------------------|--------|
| Baseline filter (`Hcm02\|Recruitment\|Citizen\|PublicCareers\|Ess\|Candidate\|Offer\|Appeal`) | 551 pass / 1 fail | **626 pass / 1 fail** (same `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50`) |
| Regression unit (`Hcm026..29\|PublicCareers\|EssTalent\|RecruitmentHire\|Fun001`) | 354 pass | **354 pass** |
| Full unit suite | — | 3230: 3222 pass / 8 fail — exactly the pre-existing set (2 Syr001, `BolaArchitectureTests` HRContracts/Payroll, `Ten002…IgnoreQueryFilters` `BenefitPlanSeeder.cs`, `Prv001RopaTests`, `San001…Validate_script_passes`, `Str005ProductionBindingTests` ×2) |
| Integration (`Hcm02\|Hcm03\|RecruitmentHire\|Fun001\|PublicCareers\|EssTalent\|InternalChannel\|Promote*\|Recruitment\|Offer\|HireSagaRecruitmentLineage\|InternalHireClosure`) | 46 pass / 1 fail | **47 pass / 1 fail** — `EstablishmentHireIntegrationTests.HirePartyCompensate…` fails identically on a clean `main` worktree (`Expected: Vacant, Actual: Occupied`); its sibling `HirePartyVerify…` also fails on both when run alone. Not touched here. |

`dotnet build HRManagement.slnx` → 0 errors (11 pre-existing warnings). `scripts/ux003-no-native-confirm.ps1` reports the same pre-existing violations as `main` (none in the views touched here). Acceptance tests not run (`RUN_ACCEPTANCE`). The UI was not exercised in a browser — views compile and the controller paths are covered by tests.

### Modified existing tests

| File | Change | Why |
|------|--------|-----|
| `IntegrationTests/InternalChannelIntegrationTests.cs` (`PromoteFromInternalTests`) | expects `UnderReview` (was `Shortlisted`) + `IntakeChannel.Internal` | A3: linking keeps the channel stage, never jumps to Shortlisted |
| `IntegrationTests/PublicCareersApplyTests.cs`, `PublicCareersRequisitionGateTests.cs` | controller ctor: `UnifiedIntakeService` + `CandidatePublicStatusService` instead of `RecruitmentInterviewService` | constructor changed |
| `IntegrationTests/RecruitmentTestControllers.cs`, `UnitTests/Hcm029TestSupport.cs` | pass `new UnifiedIntakeService(...)` | `RecruitmentApplicationsController` ctor gained it (last) |

`PromoteCitizenApplicationTests` is unchanged and still passes (a Shortlisted citizen maps to Shortlisted).

## 9. Deviations, known issues, markers

**Deviations from the prompt**
1. *Internal intake is one transaction with two saves*, not one save: the new `InternalApplication` and `RecruitmentApplication` reference each other (`PromotedRecruitmentApplicationId` ↔ `SourceInternalApplicationId`) and EF Core refuses the insert cycle. `SubmitInternalAsync` inserts both, then sets the link, inside one transaction. Public intake is one save.
2. *Promote buttons:* the old «إضافة للمسار الداخلي/لمسار الاختيار» buttons are gone; requisition-linked rows **without** a link (pre-backfill) show «ربط بمسار التوظيف», which calls the same endpoint (now `EnsureLinkedAsync`), so those rows remain reachable before the backfill runs.
3. *Event `FromStatus`/`ToStatus`* hold the triggering entity's enum (application, slot, offer or appeal), documented on the entity.
4. *C# = SQL check* runs on PostgreSQL in `Hcm030BackfillTests` (the SQL cannot run on SQLite); the unit test `Hcm030BackfillMappingTests` pins the C# side to the table.
5. *Eligibility* is not evaluated for backfilled/`EnsureLinkedAsync`/HR-entered rows (left null). Internal candidates have no experience/specialization/graduation source on `Employee`, so a requisition with `MinYearsOfExperience` flags them `MissingProfileData`.
6. *Legacy timeline* on `/careers/track` lost the two steps that were driven only by "promoted".
7. `PublicCareersController` no longer takes `RecruitmentInterviewService`; the portal now reads interviews with the portal tenant bypass (the old call went through the tenant filter on an anonymous request).

**Known issues**
- Each event row also produces an `EntityAuditLog` "Created" row (generic audit interceptor).
- Pre-existing inline `style` attributes remain in `PublicCareers/Details` and the legacy branch of `TrackResult`; new markup uses classes (`careers-section-title` added to `_PublicLayout`).
- Seeders still create unlinked requisition-linked channel rows (§6).

**Retention (session 2)** — `CandidateApplicationLegalLifecycleService.CanPurge(CitizenApplication, …)` returns false whenever `PromotedRecruitmentApplicationId` has a value, and after unification every requisition-linked citizen row is linked, so no new citizen row is ever purgeable. Retention must treat the pair (channel row + recruitment application) together and extend anonymization to the HCM-030 profile fields (BirthDate first) and `EligibilityOverrideReason`. Not changed here.

**Markers**
- `src/Services/HRCore/HR.Core.Domain/Services/CandidateApplicationLegalLifecycleService.cs:43` — `PLAN-30 S2:` retention of the linked pair
- `src/Services/HRCore/HR.Core.Domain/Services/CandidateApplicationLegalLifecycleService.cs:82` — `PLAN-30 S2:` anonymize the citizen profile
- `src/Services/HRCore/HR.Core.Domain/Services/CandidateApplicationLegalLifecycleService.cs:101` — `PLAN-30 S2:` anonymize the snapshot + override reason
- `src/Services/HRCore/HR.Core.Domain/Entities/CitizenApplication.cs:79`, `RecruitmentApplication.cs:138` — `PLAN-30 S2:` profile in anonymization
- `src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentApplicationEvent.cs:39` — `PLAN-30 S2:` `NotificationsPlannedAt`
- `src/Services/HRCore/HR.Core.Infrastructure/Data/RecruitmentApplicationEventRecorder.cs:51` — `PLAN-30 S2:` dispatcher reads pending rows
- `src/Services/HRCore/HR.Core.Web/Controllers/PublicCareersController.cs:153` — `PLAN-30 S2:` CandidateDocument uploads + channel consent at apply
- `src/Services/HRCore/HR.Core.Web/Views/PublicCareers/Details.cshtml:153` — `PLAN-30 S2:` attachments on the form
- `src/Services/HRCore/HR.Core.Web/Controllers/PublicCareersController.cs:200` — `PLAN-30 S3:` `/careers/my` actions
- `src/Services/HRCore/HR.Core.Web/Views/PublicCareers/TrackResult.cshtml:148` — `PLAN-30 S3:` offer reply / withdraw / appeal from the view flags

## 10. What session 2 needs to do

1. **Dispatcher:** consume `RecruitmentApplicationEvents` where `NotificationsPlannedAt IS NULL` (§4), idempotency key `ApplicationId + EventType + RelatedEntityId/OccurredAt`; set `NotificationsPlannedAt`. Use `CandidatePublicView` texts for the candidate wording; never the national id or salary in SMS.
2. **Documents:** `CandidateDocument` + upload on the apply form (marker in `Details.cshtml`/`Apply`); File.Storage through HR.Core's service identity.
3. **Retention:** pair-wise purge/anonymize (§9), including the new profile columns and `RecruitmentApplicationEvents` (no PII, but cascade-deleted with the application).
4. Run `scripts/hcm030-backfill-unified-intake.ps1 -Apply` on each environment after the migrations (dry run first).
