# PLAN-32 — Progress log (checkpoint)

**If you are resuming:** read this file and `git log --oneline -20` first, then continue from **Next step**.
Prompt: [`docs/acceptance-plans/execution/PLAN-32-AGENT-PROMPT.md`](../docs/acceptance-plans/execution/PLAN-32-AGENT-PROMPT.md).
Plan: [`docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md`](../docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md).
Baseline: [`reports/plan32-baseline.md`](plan32-baseline.md).

## Phase status

| Phase | Scope | Status | Commit |
|-------|-------|--------|--------|
| P0 | Baseline, orientation, notes | Done | `docs(hcm032): baseline, pre-implementation notes and progress log` |
| P1 | Definitions, pure calculators, decline category | Not started | |
| P2 | `RecruitmentMetricsService` (read-only, scoped) | Not started | |
| P3 | UI | Not started | |
| P4 | Exports | Not started | |
| P5 | Scale | Not started | |
| P6 | Analytics integration (additive) | Not started | |
| P7 | Acceptance and regression | Not started | |
| P8 | Docs, validate script, independent review, final report | Not started | |

## Branch

`feature/plan-32-recruitment-analytics` from `main` @ `bcc582a` (PLAN-26..31 merged). Local only, never pushed.

## Decisions

Prompt decisions 1–10 are adopted as written (recorded in Arabic at the top of the plan document). Judgement calls:

| # | Decision | Reason |
|---|----------|--------|
| K1 | **Targets** of the new KPIs use the existing `cost_per_employee` precedent: `target = { direction, value: null, note: "TBD — set by product-hr after the first frozen quarter" }`. The STR-007 validator accepts any target object. **Baselines** for `baselineRequired` KPIs are added with `value: null`, `prePilotFreeze: true` and a note (same precedent); no number is invented. | Prompt: never invent targets; the validator requires a frozen baseline row for every `baselineRequired` KPI. |

## P0 maps

Paths relative to `src/Services/HRCore/`.

### Offer decline paths

| Path | Entry | Reaches | Category handling (P1) |
|------|-------|---------|------------------------|
| HR on behalf | `OfferLettersController.DeclineOffer` (`DeclineOfferForm`: `Reason`, `EvidenceFile`) → `OfferOnBehalfService.DeclineAsync` → `RecruitmentOfferService.DeclineAndDraftNextAsync` → `DeclineAsync` | `Status=Declined`, `DeclineReason`, `DeclinedAt`, `DeclinedBy=HrOnBehalf` | `<select>` on `Views/OfferLetters/Details.cshtml` (decline form), required |
| Candidate portal (PLAN-30) | `CandidatePortalController.DeclineOffer` (`CandidatePortalTextForm.Text`) → `CandidatePortalService.DeclineOfferAsync` → `DeclineAndDraftNextAsync` | `DeclinedBy=Candidate` | `<select>` on `Views/CandidatePortal/Index.cshtml`, required |
| Candidate withdrawal with an Issued offer | `CandidatePortalService.WithdrawAsync` → `RecruitmentSelectionService.WithdrawByCandidateAsync` → `RecruitmentOfferService.SettleForWithdrawalAsync` (does **not** call `DeclineAsync`; reason prefixed «انسحاب المرشح: ») | `DeclinedBy=Candidate` | set `Other` (decision 4) |
| Competition reserve recall | `CompetitionReserveService.Recall*` | reacts to Declined/Expired/Revoked, never declines | none |
| Jobs | `RecruitmentOfferExpirer` sets Expired; no job declines | — | none |

Tests calling decline (signature changes ripple there): `Hcm027OfferOutcomeTests`, `Hcm027ReserveListTests`,
`Hcm031ReserveRecallTests` (service `DeclineAsync`/`DeclineAndDraftNextAsync`), `Hcm030OnBehalfAcceptanceTests`
(`OfferOnBehalfService.DeclineAsync`), `Hcm030CandidatePortalTests` (`DeclineOfferAsync`, controller `DeclineOffer`),
acceptance `AcHcm27OfferMeritTests`, `AcHcm31CompetitionTests` (form posts to `OfferLetters/DeclineOffer`).

### Requisition org unit and scope

- `RecruitmentRequisition` has no org unit of its own; its unit is `FundedPosition.OrgUnitId` (non-null).
  `CompetitionLine.OrgUnitId` is optional and not used for scope (decision 6 uses the allocated requisition's unit).
- `OrgScopeService.GetAllowedOrgUnitIdsAsync(User)` → `OrgAccessScope` (`IsUnrestricted`, `IsDenied`, `AllowedOrgUnitIds`
  = root + descendants, `CanAccess(Guid?)`, `MustFilter = !IsUnrestricted`).
- `ReportsController`: `IsDenied → Forbid`, `MustFilter →` `Where(AllowedOrgUnitIds.Contains(unit))`, out-of-scope
  org-unit filter → `Forbid`. `RecruitmentAppealsController`: in-memory `WorkflowInboxOrgScope.Allows` on
  `Application.Requisition.FundedPosition.OrgUnitId` (null unit = central only).
- PLAN-32 returns an **empty result** (not 403) for an out-of-scope filter, per AC-HCM-32b «لا بيانات».

### Competition data

- Competition application: `CompetitionId` set, `RequisitionId`/`CompetitionLineId` null until allocation
  (`CompetitionAllocationService`: allocated → `RequisitionId`, `Status=Ranked`, `CompetitionAllocation` row, event
  `CompetitionAllocated`; reserve → `ReserveListed` + `CompetitionReserveEntry`, event `CompetitionReserve`; otherwise
  `Rejected` kind `CompetitionResult`, event `CompetitionNotSuccessful`). Recall: entry `Recalled`, event
  `CompetitionReserveRecalled`, application gets the requisition.
- Admitted = reached `Shortlisted` (seating admits Shortlisted); present = an `ExamCandidate` with `Attendance=Present`;
  passed = `CompetitionResult.Outcome=Passed` in the competition's `CurrentResultVersionId`; objections =
  `RecruitmentAppeal.Kind=ResultObjection` (`ObjectionUpheld`/`ObjectionDismissed`).
- Requisitions attach to lines through `RecruitmentRequisition.CompetitionLineId`; an "open" competition = status not in
  `Finalized`, `Cancelled`.
- Appealable rejection kinds: `Recruitment:AppealableRejectionKinds`, default `SelectionDecision`, `NotSelectedOnHire`,
  `OfferRevoked` (`RecruitmentOptions.GetAppealableRejectionKinds()`).

### Existing indexes on KPI tables

| Table | Indexes |
|-------|---------|
| RecruitmentApplications | (TenantId, RequisitionId); unique (TenantId, RequisitionId, Rank) filtered; unique (TenantId, CompetitionId, NationalIdHash) filtered; (TenantId, CompetitionId, Status); FK indexes |
| RecruitmentApplicationEvents | (TenantId, RecruitmentApplicationId, OccurredAt); (OccurredAt) filtered `NotificationsPlannedAt IS NULL` |
| RecruitmentRequisitions | unique (TenantId, RequisitionNumber); (TenantId, Status); (FundedPositionId); unique (TenantId, FundedPositionId) filtered open statuses; FK CompetitionLineId |
| OfferLetters | unique (TenantId, OfferNumber); unique (TenantId, RecruitmentApplicationId, Status) filtered `Status IN (2,3)`; FK indexes |
| RecruitmentAppeals | (TenantId, RecruitmentApplicationId, Status); FK indexes |
| WorkflowInstances | (EntityType, EntityId); (Status); (DefinitionId) |
| FundedPositions | unique (TenantId, PositionCode, BudgetYear); (TenantId, Status); FK OrgUnitId |
| Competition tables | see `CompetitionConfigurations.cs`, `ExamConfigurations.cs`, `ResultConfigurations.cs` (per-competition / per-application indexes) |

### Workflow history

`WorkflowInstanceActionService` appends one `WorkflowStepHistoryEntry` per approve/reject (`ActionAt` = action time);
`CompletedAt` set on completion. A step's start is therefore the previous entry's `ActionAt` or `StartedAt`.
`WorkflowStatus` (NotStarted, InProgress, Completed, Rejected, Cancelled); entry `Status` (Pending, Approved, Rejected,
Skipped).

## Files changed

| Phase | Files |
|-------|-------|
| P0 | `reports/plan32-baseline.md`, `reports/plan32-progress.md`, `docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md`, `docs/acceptance-plans/execution/PLAN-32-AGENT-PROMPT.md` (added as-is) |

## Migrations

None yet.

## Test results

| Phase | Run | Result |
|-------|-----|--------|
| P0 | Baseline filter `Hcm02|Hcm030|Hcm031|Reports|Str007|Ana001|Prod006|Perf004` | 1008 / 1008 pass |
| P0 | STR-007 / ANA-001 / PROD-006 validators | PASS / PASS / PASS |

## Open issues

None.

## Next step

P1 — pure calculators in `HR.Core.Domain/Services/RecruitmentKpi/`, migration `HCM032_OfferDeclineCategory`, decline
category on both decline forms, KPI definitions in STR-007 docs and matrix.
