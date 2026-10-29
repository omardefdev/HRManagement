# PLAN-31 — Progress log (checkpoint)

**If you are resuming:** read this file and `git log --oneline -30` first, then continue from **Next step**.
Prompt: [`docs/acceptance-plans/execution/PLAN-31-AGENT-PROMPT.md`](../docs/acceptance-plans/execution/PLAN-31-AGENT-PROMPT.md).
Plan: [`docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md`](../docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md).
Baseline: [`reports/plan31-baseline.md`](plan31-baseline.md).

## Phase status

| Phase | Scope | Status | Commit(s) |
|-------|-------|--------|-----------|
| P0 | Baseline, mapping, pre-implementation notes | Done | `docs(hcm031): baseline, pre-implementation notes and progress log` |
| P1 | Competition model, lines, approval workflow, announce, HR screens (31a) | Done | `feat(hcm031): recruitment competitions — lines, requisition grouping, approval workflow, announcement` |
| P2 | Intake with line preferences | Done | `feat(hcm031): competition intake with ordered line preferences` |
| P3 | Exams: seating, admission cards, attendance, anonymous double marking (31b, 31c) | Done | `feat(hcm031): exam sessions, seating and admission cards` (`a574ec3`); `feat(hcm031): anonymous double marking with reconciliation and marker role` |
| P4 | Results, approval, publication, objections, versions (31d, 31e) | Done | `feat(hcm031): competition results — compute, approve, publish, public list` (`9988e70`); `feat(hcm031): result objections with re-computation and versioned results` |
| P5 | Finalize, allocation, reserve recall, bulk offers (31f, 31g) | Pending | |
| P6 | Candidate portal, notifications, retention, DSAR | Pending | |
| P7 | Integration, scale, acceptance | Pending | |
| P8 | Docs, validate script, independent review, final report | Pending | |

## Branch

`feature/plan-31-competitions-exams` from the tip of `feature/plan-30-candidate-experience` (`35d4efc`). PLAN-30 is
complete (final report, `hcm030-validate.ps1` PASS) but **not merged into `main`**; the prompt allows branching from its tip.
`git diff main...HEAD` therefore also contains PLAN-30 — the P8 reviewer must diff against `35d4efc` instead.

## Decisions (prompt decisions 1–9 plus judgement calls)

Prompt decisions 1–9 are adopted as written (recorded in Arabic at the top of the plan document). Judgement calls:

| # | Decision | Reason |
|---|----------|--------|
| J1 | **Objection window is not a status.** The competition stays `ResultsPublished` from the first publication until `Finalized`; the window is `FirstPublishedAt + ObjectionWindowDays` (inclusive, UTC). Later result versions go through their own `Draft → PendingApproval → Approved → Published` statuses without changing the competition status. `ResultsPendingApproval` is used only for the first version. | Fewer statuses; a later version never reopens the window (prompt P4). |
| J2 | **One criteria shape per competition.** Each line points to an active criteria version of one of its attached requisitions (`CompetitionLine.CriteriaVersionId`). Submit-for-approval requires every line's criteria to contain ≥ 1 `Exam` item, weights summing to 100 (existing `AssessmentCriteriaRules`), and **the same items (key, stage, weight, max score) on all lines**. | One `FinalScore` and one `OverallRank` per candidate are only meaningful when every line weighs the same items; lines still differ by grade / job / org unit. |
| J3 | **Non-exam stages** are scored with the existing `RecruitmentAssessmentScore` rows against the competition's reference criteria (line 1's version); the exam item(s) take the reconciled exam score normalized to the item max. `ComputeDraftAsync` refuses (Arabic message with the count) when a present candidate lacks a complete non-exam assessment. | Reuses `AssessmentScoringRules`; no silent zero. |
| J4 | **Committee gate.** The PLAN-28 gate covers ranking only (`AssignRankAsync`, `ApplyCommitteeRankingAsync`, appeal rank correction), not offers. Allocation writes ranks directly after an approved, published results version whose workflow has a committee step, so it **replaces** the gate for attached requisitions. Guards: manual `AssignRankAsync`, committee ranking and new committee sessions are refused for a requisition attached to a competition. | `SelectionCommitteeSession.RequisitionId` is non-nullable; linking a session to a competition needs a schema change on a PLAN-28 table. |
| J5 | **Results approval committee step** = a role step (`hr-core`, «لجنة المسابقة») followed by `hr-management-approver`, with distinct people on each step and the person who computed the draft excluded (SoD). | See J4. |
| J6 | **Objection outcomes** are new values `ObjectionUpheld` and `ObjectionDismissed` on `RecruitmentAppealOutcome`; the rejection outcomes keep their meaning. Upheld = the reviewer enters an `ObjectionCorrection` score (no re-marking by a new marker). | Re-marking needs a new assignment cycle; a correction score is simpler and auditable. |
| J7 | **Marker access** follows the PLAN-28 "compliance access slice" precedent (`f7b6c28`): a new policy `HrCoreExamMarking`, the controller name `ExamMarking` added to the module filter allowlist, and one gateway path rule `/ui/hr-core/ExamMarking` → `HrCoreExamMarking`. These are role-specific additive entries; the gates' semantics are unchanged, so this is not the PLAN-25 generic-gate change of the Stop list. | Without them a marker gets 403 everywhere in HR.Core. |
| J8 | **Competition posting:** `RecruitmentCompetition.PublicJobPostingId` (posting with `RequisitionId = null`); the public apply resolves the competition by posting id. The citizen row has `RequisitionId = null` and links the competition application through `PromotedRecruitmentApplicationId`, so `/careers/my` (one application per session) works unchanged. | No column on `PublicJobPosting`. |
| J9 | **Eligibility per preferred line**: flags are evaluated against every attached requisition of each preferred line; the union is stored on the application (`EligibilityFlags`) and the per-line union on `CompetitionPreference.EligibilityFlags`. | "Union of flags, with the line shown." |
| J10 | **Legacy hole closed:** the legacy "hire without offer" path (`RequisitionId is null && Shortlisted`) and every `RequisitionId == null` legacy filter exclude competition applications (`CompetitionId != null`). | P0 map, §Dangerous. |
| J12 | **Reconcile rule**: within the threshold the exam score is always the average of the first and second marks; above it the reconciler marks and the rule decides — `Average` (mean of the three), `ReconcilerDecides` (reconciler's mark), `AverageOfClosestTwo` (mean of the closest pair; ties → higher pair). | The prompt names the three rules without defining them; this is the only reading where all three differ. |
| J13 | **Line criteria** are frozen automatically at submit: each line takes the active criteria version of its first attached requisition (by number); rejected approval unfreezes them. No criteria picker in the UI. | Simplest; J2 guarantees all versions share one shape anyway. |
| J14 | **Cancelling a competition** closes its posting/vacancy and rejects open competition applications with kind `RequisitionCancelled` (note «إلغاء المسابقة»); attached requisitions stay attached for history but are released (`HoldsRequisitions` = false), so they can be closed or cancelled individually. | No new rejection kind; requisition status cannot go back from Published. |
| J15 | **Workflow handler wiring**: `CompetitionWorkflowHandler` is an optional last constructor parameter of `WorkflowInstanceActionService` (DI supplies it), so the six manual constructions in tests keep compiling. | Avoids touching PLAN-27/28 test support. |
| J16 | **Public competition apply is a separate page** (`/careers/competitions/{id}`, view `PublicCareers/Competition.cshtml`) that mirrors the PLAN-30 form; the PLAN-30 `Details.cshtml` is untouched. The competition posting (no requisition) redirects there from `Details` and from `Apply`, so it can never produce a legacy unlinked citizen row. Profile checks were extracted to `ValidateProfileAsync` (same logic for both paths). | No change to the accessibility-scanned PLAN-30 view; one pipeline for profile/contact/documents. |
| J17 | **Required documents of a competition** = union of its attached requisitions' `RequiredDocumentTypes`. | A competition has no document list of its own. |
| J18 | **Bulk screening** reuses `RecruitmentSelectionService.TransitionStatusAsync`; admitting to the exam (`Shortlisted`) records the actor's conflict-of-interest declaration (checkbox) and needs the PLAN-30 override reason for flagged applications; `Submitted` passes through `UnderReview`. | Existing gates unchanged. |
| J19 | **Projection**: competition states are implemented now in the pure `CandidatePublicStatusProjection` (input `CandidateCompetitionInput`); `CandidatePublicStatusService.LoadCompetitionInputsAsync` fills exam/results/allocation data in P3–P5. The timeline relabels «مدعو للمقابلة» → «الامتحان» and «قيد التقييم النهائي» → «النتائج» for competition applications. | One place for all public states. |
| J20 | **Marker identity**: HR.Core has no user↔employee link, so markers are **assigned by employee** (`ExamMarkerAssignment.MarkerEmployeeId`, picked from the employee list) and the token subject is **bound on the marker's first visit** (`MarkerUserId`); a different token of the same employee is refused afterwards. A marker whose token also passes `HrCoreReadOnly` is flagged (`HoldsHrReadAccess`), shown «يملك هذا المصحح صلاحية الاطلاع على هوية المتقدمين» on the HR exams tab and on his marking screen, and audited (`ExamMarkerHoldsHrReadAccess`). The warning therefore appears at the marker's first visit, not at assignment time (HR.Core cannot see Keycloak roles of other users). | Only the token knows the roles. |
| J21 | **Exam numbers per application**: one number per application per competition, reused across exam types and on re-seating; uniqueness `(CompetitionId, ExamType, ExamNumber)` and `(CompetitionId, ExamType, RecruitmentApplicationId)`. Every admitted applicant sits every exam type that has sessions. | "One row per application per exam type" + "unique per competition". |
| J22 | **Marking lifecycle**: HR records attendance, then «اعتماد انعقاد الجلسة» (Seated → Held, all attendance known) opens marking; «إغلاق التصحيح» (Held → MarkingClosed) needs every present paper complete, stores `ExamCandidate.FinalMark` and locks the marks. A marker may correct his own mark until closed. | Explicit steps make "locked when MarkingClosed" testable. |
| J23 | **Rendered HTML check**: the marker views are asserted by a static scan of `Views/ExamMarking/*.cshtml` (no applicant field referenced) plus a JSON serialization of every marker view model returned by the controller (no name, national id or application id); the live rendered HTML is checked by the acceptance test (P7). | Unit tests do not host Razor. |
| J24 | **Outcome and line pass marks**: `Passed` when the final score reaches the **lowest** effective pass mark of the candidate's preferred lines (`LinePassMark ?? PassMark`); at allocation (P5) a line is open to the candidate only when the score reaches that line's own pass mark. | One outcome per candidate, line overrides still honoured. |
| J25 | **Unresolved ties** («تعادل يتطلب قراراً») keep the same rank and block «تقديم للاعتماد» until HR records a tie decision (order + reason ≥ 10, audited, shown with the row); the decision is part of the version that is approved. | "No silent ordering" with an explicit, approved decision. |
| J26 | **Exam points** = average over the exam types sat of `FinalMark / MaxScore`, × the summed weight of the Exam-stage items; non-exam points use `AssessmentScoringRules.ComputeFinal` on the non-exam items of the competition's reference criteria (line 1's frozen version); interview points (tie-break) on the Interview items. Absent in any exam type → `Absent`, no score, no rank. Ranks cover every present candidate (passed and failed). | Single-formula, testable. |
| J27 | **Results statuses**: the first version moves the competition Examining → ResultsDraft → ResultsPendingApproval → ResultsPublished (a returned approval goes back to ResultsDraft); later versions (objections) keep the competition at ResultsPublished. An open draft is recomputed in place. At most one published version (filtered unique index). | J1. |
| J28 | **Objection decision**: the PLAN-28 appeal page decides objections (reviewer: compliance, independent — the competition's markers are also excluded); `RecruitmentAppealsController.Decide` routes `ObjectionUpheld/ObjectionDismissed` to `CompetitionObjectionService.DecideAsync`, which stores the corrected mark, approves the review workflow, then recomputes a new draft version («اعتراض مقبول على الدفتر …»). HR files on behalf by exam number from the objections tab. | Reuse of the PLAN-28 independent review (decision 5). |
| J11 | **Exam numbers**: 8 decimal digits from `RandomNumberGenerator`, unique per competition (DB unique index + retry). | Fixed length, not derived from order or identity. |

## P0 map — RequisitionId-null assumptions (competition application = `CompetitionId` set, `RequisitionId` null)

Paths relative to `src/Services/HRCore/`. **Breaks** = refused/error; **Skipped** = silently no effect; **Danger** = wrong behaviour.

| Area | Location | Effect | Planned fix (phase) |
|------|----------|--------|---------------------|
| Intake | `UnifiedIntakeService.CreateFromCitizen/CreateFromInternal/SubmitInternalAsync` | Breaks (need a requisition) | competition overloads (P2) |
| Intake | `UnifiedIntakeService.HasOpenApplicationAsync` (:178) | Skipped duplicate check | competition variant + unique index (P1/P2) |
| Intake | `UnifiedIntakeService.ReevaluateDocumentFlagAsync` (:275) | Skipped | competition branch (P2) |
| Intake | `PublicCareersController.Apply` (:94, :131, :196) | Treated as legacy posting (no application) | competition apply action (P2) |
| Intake | `EssTalentController.Apply` (:93) | Breaks | competition internal apply (P2) |
| Index | `HRCoreDbContext` rank unique `(TenantId, RequisitionId, Rank)` (:410) | Unconstrained for competitions | ranks only set at allocation (RequisitionId set then) — OK |
| Index | Citizen/Internal unique indexes filtered `RequisitionId IS NOT NULL` (:664, :701) | Unconstrained | covered by `(TenantId, CompetitionId, NationalIdHash)` on applications (P1) |
| HR index | `RecruitmentApplicationsController` `LegacyOnly` filter (:805), `Index.cshtml:88` «قديم» badge | Danger (competition shown as legacy) | exclude `CompetitionId != null`; competition filter (P2) |
| Scoring | `RecruitmentScoringService.SubmitScoresAsync` (:41), `BuildSummaryAsync` (:175) | Breaks / skipped | competition branch (P4, J3) |
| Ranking | `RecruitmentSelectionService.AssignRankAsync` (:94), `ValidateRanking` (:227), null-equals-null duplicate (:101) | Breaks / danger | refuse for competition apps (P5, J4) |
| Committee | `CommitteeRankingGate`, `SelectionCommitteeService.GetCandidatesQuery` (:365) | Skipped | J4 guards (P4/P5) |
| Offers | `RecruitmentOfferService.CreateDraftAsync` (:64) | Breaks before allocation — correct | allocation sets `RequisitionId` (P5) |
| Offers | `TryAutoDraftNextOfferAsync` (:538), `AvailableForMerit` (:673) | Per requisition | line-level recall before it (P5) |
| Reserve | `ReserveListService.MoveSiblingsToReserveAsync` (:80) | Competition siblings untouched — correct (line reserve is separate) | — |
| Reserve | `ReserveListService.ExpireReserveAsync` (:122) | Would expire competition apps by `ReserveUntil` | competition pass expires entries (P5) |
| Hire | `RecruitmentWorkflowRules.CanHire` (:38), `SelectionWorkflowRules` (:74, :134), `RecruitmentHireClosureService` (:191) | **Danger**: legacy hire without offer for Shortlisted null-requisition | exclude competition apps (P1, J10) |
| Hire | `RecruitmentHireAppealGuard` (:20) | Skipped when null | allocated apps have a requisition — OK |
| Hire event | `ICandidateAccepted` (`RecruitmentHireClosureService` :517) | Fine (nullable) | allocated apps carry the requisition |
| Projection | `CandidatePublicStatusProjection` / `CandidatePublicStatusService` | Fine, but no competition states | competition states (P6) |
| Notifications | `CandidateNotificationDispatcher` | Fine | new templates (P6) |
| Retention | `CandidateRetentionService.PastRetentionPairs` (:624), `CandidateApplicationLegalLifecycleService.RetentionTrigger` (:97) | **Danger**: open competition anonymized by `AppliedAt` | competition `FinalizedAt`/`CancelledAt` trigger (P2 + P6) |
| DSAR | `DsarInternalController` (:53, :76, :99, :116) | Null-safe, no competition data | competition records (P6) |
| Portal | `CandidatePortalService.DocumentIntakeErrorAsync` (:474), `MissingDocumentTypes` (:458) | Breaks (no upload) | competition branch (P2/P6) |
| Appeals | `WorkflowInboxOrgScope.Allows` with `Requisition?.FundedPosition?.OrgUnitId` | Competition objections hidden from org-scoped reviewers | org unit from the competition line (P4) |
| Cascade | `RecruitmentRequisitionCascade` | Requisition-scoped | competition cancel cascade (P1) |

## P0 map — marker access

- Gateway: `GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy` (hr-core branch :139–186) sends every
  `/ui/hr-core` GET to `HrCoreReadOnly`, every mutation to `HrCoreWrite`; `RecruitmentAppeals` (PLAN-28) is the precedent
  for a dedicated path rule.
- Service: `HrModuleAuthorizationFilter` (:28–43) requires `ModuleHrCore` unless the controller is on the allowlist.
- Policies are code-defined in `HrManagementAuthorizationExtensions`; `HrCoreReadOnly` = the `ModuleHrCore` role set.
- A marker-only user today: 403 at the gateway and at the module filter for every HR.Core page; `LookupApi`
  (`HrCoreLookup`, any authenticated user, org-scoped) and static assets stay reachable (pre-existing, not ExamMarking).
- Fix (J7): role `hr-exam-marker`, policy `HrCoreExamMarking` (marker only), allowlist `ExamMarking`, gateway path rule
  placed before the GET/mutation branches, `[Authorize(Policy = HrCoreExamMarking)]` on the controller, a marker-only
  layout branch (no module nav). Persona catalog size asserted at `AcceptancePersonaCatalog.cs:108,222`,
  `prepare-keycloak-import.ps1:137,269`, `AcceptanceV2CatalogTests.cs:86–103`, `AcceptanceIdentityPopulationTests.cs:12,71`
  (currently 57 personas, Agency A 39). `sync-keycloak-dev-realm.ps1` never creates realm roles → a realm re-import
  (or `kcadm create roles`) is required for `hr-exam-marker`.

## P0 map — legislation sources

`LegislationSource` (platform-global, PK `LegalSourceId`), read through `ILegislationRegisterProvider`, validated by
`RecruitmentLegalSourceValidator.ValidateRequiredAsync`. Register (`docs/syr/legislation-register.json`, 7 active entries)
has no competition instrument. Usable as a competition legal source: `SYR-LAW-50-2004` (قانون العاملين الأساسي، م18 شروط
التعيين) and `SYR-LAW-53-2021`. The source is **chosen at creation**; no article numbers or values are invented —
`docs/syr/legal-source-binding.md` will list every competition input as «يُحدَّد من المرجع المعتمد عند اعتماد D-2».

## P0 map — PLAN-30 extension points

- Events: `RecruitmentApplicationEvent` + `RecruitmentApplicationEventRecorder.Record` (ChangeTracker switch); explicit
  adds with deterministic ids are precedented (`OfferExpiryReminder`). Competition events will be explicit string types.
- Projection: `CandidatePublicStatusProjection` (pure; codes/labels switch) via `CandidatePublicStatusService`.
- Notifications: event → template (`CandidateNotificationTemplates.TemplateCodeFor`), PII guard
  `Hcm030NotificationPiiGuardTests` bans «درجة/علامة/الترتيب/رتبة» and score/rank properties; no HR channel exists
  (HR sees a card) → the "no valid reserve" notice is an HR-visible row on the competition.
- Portal: one citizen application per `careers_session`; `CandidatePortalService.GetPageAsync` builds the page.
- Retention: `CandidateRetentionService.PastRetentionPairs` + `AnonymizePairAsync`; DSAR: `DsarInternalController.CandidateRecordsAsync`.
- a11y: `scripts/hcm006-a11y-scan.ps1` hard-coded view arrays.
- Jobs: advisory lock keys 8675309–8675316 used; **8675317+ free** (PLAN-31 uses 8675317 for the competition intake/reserve pass if a new job is needed).
- Workflows: `WorkflowPackSeeder` next id `f0050016-0016-4000-8000-000000000016`; handlers are injected into
  `WorkflowInstanceActionService` (ripple: 6 manual constructions in tests); deep links in `WorkflowDeepLinkResolver`;
  self-approval in `WorkflowInstanceActionService.ValidateSelfApprovalAsync`.
- Codes: new `CodeEntityKind` + `CodeGenerationRequests` + `CodeGenerationSeeder.Seed…IfMissingAsync`.
- Acceptance seed ids: next `AcceptanceSeedIds` suffix `…514`.

## Files created or changed

| Phase | Files |
|-------|-------|
| P4 | Domain: `CompetitionResultVersion`/`CompetitionResult`/`CompetitionAllocation`/`CompetitionReserveEntry` (+ statuses/outcomes), `CompetitionScoringRules`, `RecruitmentAppeal.Kind/CompetitionResultVersionId/ObjectionExamCandidateId/ObjectionCorrectedScore`, `RecruitmentAppealKind`, outcomes `ObjectionUpheld/ObjectionDismissed`, `RecruitmentRejectionKind.CompetitionResult`, `RecruitmentAppealWorkflowRules.ValidateObjectionOutcome`. Infra: `ResultConfigurations`, `HRCoreDbContext`, `CompetitionResultsService`, `CompetitionObjectionService`, `CompetitionWorkflowHandler` (results), `WorkflowPackSeeder` (`RECRUITMENT-RESULTS-APPROVAL`, `f0050017`), `WorkflowInstanceActionService` (results SoD), `RecruitmentSelectionService`/`SelectionCommitteeService` (J4 guards), `RecruitmentAppealService` (objection outcome + marker exclusion), `RecruitmentAppealWorkflowHandler` (correction mark). Web: `CompetitionsController` (results/objections actions), results + objections tabs, `_CompetitionWorkflowPanel` (version id), `PublicCareersController` (`CompetitionResults`, `CompetitionResultsPdf`), `Views/PublicCareers/CompetitionResults.cshtml`, `CompetitionResultsDocument`, `WorkflowDeepLinkResolver`, `RecruitmentAppealsController` + view + VM (objection branch); `scripts/hcm006-a11y-scan.ps1` (+results view). Tests: `Hcm031ResultsTests` (12), `Hcm031ObjectionTests` (7). |
| P3 | Domain: `ExamEnums` (session type/status, attendance, marker role, score round), `ExamSession`/`ExamCandidate`/`ExamMarkerAssignment`/`ExamScore`, `ExamMarkingRules` (validation, blindness, reconciliation, seating plan, random numbers), event types `AdmissionCardReady`…`CompetitionNotSuccessful`. Infra: `ExamConfigurations`, `HRCoreDbContext`, `ExamSeatingService`, `ExamMarkingService`, `ExamAdmissionCardDocumentService`. Web: `ExamAdmissionCardDocument` (QuestPDF), `CompetitionsController` (exam actions, `ExamSession` page, `AdmissionCard`), `Views/Competitions/ExamSession.cshtml`, exams/scores tabs, `ExamMarkingController` + `Views/ExamMarking/*` + `ExamMarkingViewModels`, `ExamMarkerAccess`, `_Layout.cshtml` (marker-only branch). Shared: `HrManagementRoles.HrExamMarker`, `HrManagementPolicies.HrCoreExamMarking`, policy in `HrManagementAuthorizationExtensions`, `HrModuleAuthorizationFilter` (+`ExamMarking`), `GatewayPathAuthorizationMiddleware` (+`/ui/hr-core/ExamMarking`), `AcceptancePersonaCatalog` (role + counts 60 / A 42). Personas `a-exam-marker`, `-2`, `-3` (`testdata/acceptance/personas.json`, HQ ordinals 41–43, `passwordEnv` only); `scripts/prepare-keycloak-import.ps1` (60). Tests: `Hcm031SeatingTests` (7), `Hcm031MarkingTests` (11), `Hcm031MarkerAccessTests` (17), `Hcm031AdmissionCardTests` (3). |
| P2 | Infra: `CompetitionIntakeService` (public/internal apply, preferences, per-line eligibility, bulk screening, required-document union), `RecruitmentIntakeCloser` (competition pass; attached requisitions excluded from the requisition pass), `UnifiedIntakeService.ReevaluateDocumentFlagAsync` (competition branch), `CandidatePublicStatusService` (competition input hook), `CandidateRetentionService` (competition trigger), `RecruitmentHireClosureService` (J10). Domain: `CandidatePublicStatusProjection` (competition states), `CandidateApplicationLegalLifecycleService.RetentionTrigger`, `RecruitmentWorkflowRules.CanHire` / `SelectionWorkflowRules` (J10). Web: `PublicCareersController` (`Competition`, `ApplyCompetition`, competition-posting redirects, `ValidateProfileAsync`), `CompetitionApplyViewModel`, `Views/PublicCareers/Competition.cshtml`, `EssTalentController.ApplyCompetition` + ESS index section, `CompetitionsController` («المتقدمون» tab + `ScreenApplicants`), `RecruitmentApplicationsController`/`Index.cshtml` (legacy filter, «مسابقة» badge, `CompetitionId` filter), `DsarInternalController` (competition record), `scripts/hcm006-a11y-scan.ps1` (+Competition view). Tests: `Hcm031IntakeTests` (16). |
| P1 | Domain: `RecruitmentCompetition`, `CompetitionLine`, `CompetitionPreference`, enums (`RecruitmentCompetitionStatus`, `CompetitionReconcileRule`, `CompetitionTieBreak`), `CompetitionWorkflowRules`; `RecruitmentRequisition.CompetitionLineId`, `RecruitmentApplication.CompetitionId/CompetitionLineId`. Infra: `Data/Configurations/Recruitment/CompetitionConfigurations.cs`, `HRCoreDbContext` (DbSets, FKs, unique `(TenantId, CompetitionId, NationalIdHash)`), `CompetitionLifecycleService`, `CompetitionWorkflowHandler`, `WorkflowPackSeeder` (`RECRUITMENT-COMPETITION-APPROVAL`, id `f0050016`), `CodeGenerationSeeder` + `CodeEntityKind.RecruitmentCompetition` + `CodeGenerationRequests.RecruitmentCompetition()`, `WorkflowInstanceActionService` (dispatch + creator SoD), `RecruitmentRequisitionLifecycleService` (attached guards), `RecruitmentOptions.Competition`. Web: `CompetitionsController`, `CompetitionViewModels`, `Views/Competitions/*`, menu entry, `WorkflowDeepLinkResolver`, `Program.cs`, `appsettings.Development.json` (acceptance/dev legal defaults only). Tests: `Hcm031TestSupport`, `Hcm031CompetitionLifecycleTests` (18). |
| P0 | `reports/plan31-baseline.md`, `reports/plan31-progress.md`, `docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md` (pre-implementation block), `docs/acceptance-plans/execution/PLAN-31-AGENT-PROMPT.md` (prompt, committed as-is) |

## Migrations

- `20261029082742_HCM031_Results` — additive only (4 tables, 4 nullable/defaulted columns on `RecruitmentAppeals` — `Kind` defaults to `'Rejection'` for existing rows, 15 indexes, 3 FKs); `has-pending-model-changes`: none.
- `20261029081103_HCM031_Exams` — additive only (4 tables, 11 indexes); `has-pending-model-changes`: none.
- `20261029073330_HCM031_Competitions` — additive only (3 tables, 3 nullable columns, indexes, FKs, check constraints); no data rewrite. `has-pending-model-changes`: none.

## Test results

| Phase | Run | Result |
|-------|-----|--------|
| P0 | baseline filter | 909 pass / 1 fail (pre-existing `Syr001…law50`) |
| P0 | full unit suite | 3675 pass / 10 fail (environment-only set, see baseline) |
| P1 | `Hcm031` | 18 / 18 pass |
| P1 | full unit suite | 3693 pass / 10 fail — exactly the baseline set (Ten002 lists only the pre-existing files) |
| P2 | `Hcm031` | 34 / 34 pass |
| P2 | full unit suite | 3708 pass / 11 fail — the baseline 10 + the known full-run flake `Hcm029CandidateAcceptedV4Tests.External_hire_publishes_v4_with_the_offer_terms` (passes alone, 4/4; recorded by PLAN-30) |
| P2 | `hcm006-a11y-scan.ps1` | PASS (11 views, incl. `PublicCareers/Competition.cshtml`) |
| P3 | `Hcm031` + `Hcm028Authorization` + `AcceptanceSeed` + `Gateway` filters | 216 / 216 pass (`Hcm031`: 72) |
| P3 | Acceptance catalog (`AcceptanceV2CatalogTests`, `AcceptanceIdentityPopulationTests`) | 7 pass / 1 skipped (live login test, stack not running) |
| P3 | full unit suite | 3746 pass / 11 fail — baseline 10 + the known `Hcm029…v4` full-run flake; BOLA / Ten002 offenders lists unchanged (no PLAN-31 file) |
| P2 | `ux003-no-native-confirm.ps1` | fails on pre-existing views only (`RecruitmentRequisitions/Details`, `ReferenceCatalog/Details`, `_WorkflowActionPanel`); no new view uses `confirm()` |

| P4 | `Hcm031ResultsTests` + `Hcm031ObjectionTests` | 19 / 19 pass |
| P4 | full unit suite | 3766 pass / 10 fail — exactly the baseline set |
| P4 | `hcm006-a11y-scan.ps1` | PASS (12 views, + `PublicCareers/CompetitionResults.cshtml`) |

## Modified existing tests (justification)

| Test file | Change | Why |
|-----------|--------|-----|
| `UnitTests/Hcm030TestSupport.cs` | `PublicCareersController` and `EssTalentController` constructions get the new `CompetitionIntakeService` argument | constructor parameter added in P2; behaviour unchanged |
| `IntegrationTests/PublicCareersApplyTests.cs`, `IntegrationTests/PublicCareersRequisitionGateTests.cs` | same argument added to `PublicCareersController` (4 constructions); P4 adds `CompetitionResultsService` too | same |
| `AcceptanceTests/AcceptanceV2CatalogTests.cs` | counts 57→60 personas/usernames, 51→54 with employee, 55→58 enabled, Agency A 39→42 | three exam-marker personas added (P3) |
| `AcceptanceTests/AcceptanceIdentityPopulationTests.cs` | 57→60 personas, 53→56 successful logins | same |

## Open issues

- None blocking.

## Next step

P5: `CompetitionAllocationRules` (pure: rank order → highest-preference line with capacity and own pass mark; deterministic requisition order by number; reserve per preferred passed line with `LineRank`), `CompetitionAllocationService.FinalizeAsync` (window passed, no pending objection, idempotent with a row lock/`Finalized` check; allocated → `RequisitionId`, `CompetitionLineId`, `Ranked`, `Rank` = overall rank, `AssessmentScore` = final, allocation row; passed-not-allocated → reserve entries + `ReserveListed` with `ReserveUntil`; others → `Rejected` kind `CompetitionResult`; events `CompetitionAllocated`/`CompetitionReserve`/`CompetitionNotSuccessful`); `CompetitionOfferService` (bulk drafts via `RecruitmentOfferService.CreateDraftAsync`, salary rule `Recruitment:Competition:DefaultOfferSalary` from `IPayrollGradeLookup`, per-item results; bulk submit); `CompetitionReserveService` (recall hook on offer Declined/Expired/Revoked and allocated withdrawal → first Active entry of the same line with `ReserveUntil ≥ today`; other entries Removed; no valid reserve → HR notice; expiry pass in `RecruitmentReserveExpirer`; `// PLAN-31 follow-up:` marker for non-commencement); HR «التخصيص والعروض» tab; tests `Hcm031AllocationTests`, `Hcm031ReserveRecallTests`, `Hcm031BulkOfferTests`. Two commits. Notes: (1) restore test outputs with `git diff --name-only -- testdata | ? { $_ -match "/out/" }` — never `git checkout -- testdata` (it reverts `testdata/acceptance/personas.json`); (2) Keycloak needs a realm re-import (or `kcadm create roles hr-exam-marker`) for the marker personas — `sync-keycloak-dev-realm.ps1` never creates roles.
