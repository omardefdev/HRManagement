# PLAN-31 — Progress log (checkpoint)

**If you are resuming:** read this file and `git log --oneline -30` first, then continue from **Next step**.
Prompt: [`docs/acceptance-plans/execution/PLAN-31-AGENT-PROMPT.md`](../docs/acceptance-plans/execution/PLAN-31-AGENT-PROMPT.md).
Plan: [`docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md`](../docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md).
Baseline: [`reports/plan31-baseline.md`](plan31-baseline.md).

## Phase status

| Phase | Scope | Status | Commit(s) |
|-------|-------|--------|-----------|
| P0 | Baseline, mapping, pre-implementation notes | Done | `docs(hcm031): baseline, pre-implementation notes and progress log` |
| P1 | Competition model, lines, approval workflow, announce, HR screens (31a) | Pending | |
| P2 | Intake with line preferences | Pending | |
| P3 | Exams: seating, admission cards, attendance, anonymous double marking (31b, 31c) | Pending | |
| P4 | Results, approval, publication, objections, versions (31d, 31e) | Pending | |
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
| P0 | `reports/plan31-baseline.md`, `reports/plan31-progress.md`, `docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md` (pre-implementation block), `docs/acceptance-plans/execution/PLAN-31-AGENT-PROMPT.md` (prompt, committed as-is) |

## Migrations

None yet.

## Test results

| Phase | Run | Result |
|-------|-----|--------|
| P0 | baseline filter | 909 pass / 1 fail (pre-existing `Syr001…law50`) |
| P0 | full unit suite | 3675 pass / 10 fail (environment-only set, see baseline) |

## Open issues

- None blocking.

## Next step

P1: add the domain entities/enums for competitions, lines and preferences, `CompetitionWorkflowRules`, then migration
`HCM031_Competitions`, `CompetitionLifecycleService`, workflow `RECRUITMENT-COMPETITION-APPROVAL`, announce, HR UI, tests.
