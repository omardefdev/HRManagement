# PLAN-32 — Progress log (checkpoint)

**If you are resuming:** read this file and `git log --oneline -20` first, then continue from **Next step**.
Prompt: [`docs/acceptance-plans/execution/PLAN-32-AGENT-PROMPT.md`](../docs/acceptance-plans/execution/PLAN-32-AGENT-PROMPT.md).
Plan: [`docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md`](../docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md).
Baseline: [`reports/plan32-baseline.md`](plan32-baseline.md).

## Phase status

| Phase | Scope | Status | Commit |
|-------|-------|--------|--------|
| P0 | Baseline, orientation, notes | Done | `docs(hcm032): baseline, pre-implementation notes and progress log` |
| P1 | Definitions, pure calculators, decline category | Done | `feat(hcm032): recruitment KPI definitions, calculators and offer decline categories` |
| P2 | `RecruitmentMetricsService` (read-only, scoped) | Done | `feat(hcm032): recruitment metrics service with org scope and coverage` |
| P3 | UI | Done | `feat(hcm032): recruitment reports screen with funnel, requisitions and overdue approvals` |
| P4 | Exports | Done | `feat(hcm032): recruitment report exports (Excel, PDF, scoped CSV job) without national id` |
| P5 | Scale | Done (snapshot not needed) | `perf(hcm032): analytics indexes and scale test` |
| P6 | Analytics integration (additive) | Done | `feat(ana032): recruitment requisition aggregates to Dev.Analytics and semantic-layer registration` |
| P7 | Acceptance and regression | Done (AC-HCM-32a..d Pass live) | `test(hcm032): acceptance AC-HCM-32a..d and regression` |
| P8 | Docs, validate script, independent review, final report | Not started | |

## Branch

`feature/plan-32-recruitment-analytics` from `main` @ `bcc582a` (PLAN-26..31 merged). Local only, never pushed.

## Decisions

Prompt decisions 1–10 are adopted as written (recorded in Arabic at the top of the plan document). Judgement calls:

| # | Decision | Reason |
|---|----------|--------|
| K1 | **Targets** of the new KPIs use the existing `cost_per_employee` precedent: `target = { direction, value: null, note: "TBD — set by product-hr after the first frozen quarter" }`. The STR-007 validator accepts any target object. **Baselines** for `baselineRequired` KPIs are added with `value: null`, `prePilotFreeze: true` and a note (same precedent); no number is invented. | Prompt: never invent targets; the validator requires a frozen baseline row for every `baselineRequired` KPI. |

| K2 | **Funnel reach is monotone**: reaching a stage implies every earlier stage (events "to that status or beyond", current status and evidence alike), so stage counts never increase. `ReserveListed` maps to Ranked; `Received` maps to Applied. | "or beyond" + "current status implies every earlier stage"; one rule for all sources keeps the funnel consistent. |
| K3 | **Out-of-scope org-unit filter → empty result, not 403** (unlike `ReportsController`). `IsDenied` → empty too in the service; the controller returns `Forbid` only for a denied scope. | AC-HCM-32b negative says «لا بيانات». |
| K4 | **Small-cell rule**: counts 1–4 shown «<5»; zero and ≥ 5 shown; a rate whose numerator or denominator is suppressed is suppressed. | A zero reveals nothing about a person; suppressing derived rates blocks back-calculation. |
| K5 | **Decline category validation** lives in `RecruitmentOfferService.GetDeclineError` (new parameter, null or undefined value refused with «يجب اختيار فئة سبب الرفض.»), so both the HR on-behalf pre-check and the service enforce it. `SettleForWithdrawalAsync` sets `Other`. The portal decline gets its own form `CandidatePortalDeclineForm` (`Text`, `Category`); the shared `CandidatePortalTextForm` is unchanged. | One rule for both paths; no change to the other portal forms. |
| K6 | **Stage time** uses `Received` as the first status event (its `ToStatus` is the initial status), then consecutive `StatusChanged` events; the last status (no end) is not counted. | Otherwise the time in Submitted would never be measured. |
| K7 | **Approval SLA ages and step durations in hours** (median/P90 one decimal); skipped history entries are not steps but move the next step's start. | SLA is configured in hours. |
| K8 | **Org-unit filter = the unit and its descendants** (MaterializedPath prefix, as `OrgScopeService`), intersected with the caller's scope. | A ministry-level unit should include its directorates. |
| K9 | **Shared loads per request**: the cohort (applications applied in the period, their funnel events and offer evidence, loaded in id batches of 5,000) and the hires (all-time, in scope) are memoised in the scoped service, so the `Index` set of KPI calls reads them once. The memo key is culture-free (a record `ToString` formats dates with the current culture — it crashed under ar-SA/Um Al-Qura). | Scale budget (P5); culture safety. |
| K10 | **Offer outcome dates**: Accepted `AcceptedDate`, Declined `DeclinedAt`, Expired `ExpiredAt ?? ExpiryDate` (offers expired before the expiry job), Revoked `RevokedAt`; the current status is the final outcome. Offers, appeals and objections are scoped through their application. | Documented fallback for old expired rows. |
| K11 | **Open vacancies**: "vacant since" = the latest close/cancel date of a requisition on the position, else the position's creation; the vacant-position list is empty under a requisition or competition filter; stale = `PublishedAt ?? ApprovedDate ?? RequestedDate` older than `StalePublishedDays`. Lists keep the oldest 100; counts are exact. | The funded position has no vacant-since column. |
| K12 | **Approval SLA scope**: a workflow entity's unit — requisition → its position; offer → the offer's funded position; application/appeal → the application's requisition; committee session → its requisition; competition / result version → none (unrestricted only). Bound: recruitment instances in progress, or started before the period end and not completed before its start; only those histories are parsed. | Decision 6; bounded JSON parsing (prompt P2). |
| K13 | **Competitions panel**: competitions announced in the period, or announced and not finalized/cancelled (or the filtered one), max 100; admitted = reached Shortlisted (status, event or an exam paper), present = a paper with attendance Present, passed = `Passed` in `CurrentResultVersionId`, allocated = allocations not from reserve, reserve = applications with a reserve entry (ever), recalled = entries `Recalled`. | Definitions of the prompt made countable. |
| K14 | **Exports are GET actions** (audited `BulkExport`, bounded) on `RecruitmentReportsController`. The gateway maps every POST under `/ui/hr-core` to `HrCoreWrite`, so a read-only user (executive, branch manager) could not reach a POST export; AC-HCM-32c needs the read-only export. No gateway rule is changed (PLAN-25 deferred). | Additive and keeps generic authorization untouched. |
| K15 | **No small-cell suppression on the HR.Core screen**: its breakdowns (stage, status, channel, decline category, appeal outcome, workflow step) are not personal attributes; suppression applies to the Dev.Analytics publication (P6). Dates/numbers on the screen and exports go through `RecruitmentReportFormat` (invariant, Gregorian); filter dates are `yyyy-MM-dd` strings parsed invariantly. The org-unit and competition option lists convert ids in memory (SQLite printed upper-case GUIDs). | Decision 7 scope of suppression; culture safety. |
| K16 | **Overdue approval links** go to the existing screen of the entity (requisition, offer, application, appeal, committee, competition; result version → its competition), else to the workflow instance. | Drill-through without new authorization. |
| K17 | **Applications CSV job**: template `hrcore.recruitment.applications.csv` (`ExportJobTemplates.HrCoreRecruitmentApplicationsCsv`); the job's `FilterJson` carries the filter, the allowed org units at request time (null = unrestricted) and `IncludeNames` (requester held HrCoreWrite). `EnqueueAsync` no longer replaces a non-employees filter with the employees scope filter. Download re-check (besides the generic same user/tenant/scope hash): names file → the user must still hold HrCoreWrite (`RecruitmentReportAccess`, a role mirror of the policy pinned by a test over every role); unrestricted file → the user must still be unrestricted. The job streams rows in pages of `ChunkSize`. | Prompt P4: names captured in the snapshot and re-checked at download. |
| K19 | **Scale measurement**: `Hcm032RecruitmentAnalyticsScaleTests` (category `Hcm032Scale`, opt-in `HCM032_PERF=1`, `perf/hcm032-recruitment-analytics-scale.ps1`) runs the 13 metric calls of `Index` on a fresh service per run; the budget is asserted on the run after one warm-up (EF query compilation; cold timings are recorded too). Every SELECT of the measured run is captured by a `DbCommandInterceptor` and `EXPLAIN ANALYZE`d with its parameters (`artifacts/hcm032-perf/plans.txt`). | Steady-state timings; indexes decided from real plans. |
| K20 | **Indexes** (`HCM032_AnalyticsIndexes`): `RecruitmentApplicationEvents (TenantId, EventType, ToStatus, OccurredAt)` — the hires sub-plan sequentially scanned the events (hashed SubPlan now on the index, 164 → 116 ms); `RecruitmentApplications (TenantId, AppliedAt)` — the cohort scan; in the single-tenant scale DB 88 % of rows match so PostgreSQL still prefers the sequential scan (correct plan), the index serves multi-tenant tables. Other sequential scans are on small per-tenant tables (≤ 8 ms each), no index added. **No snapshot table**: the budget is met with exact live queries. | Prompt P5: indexes only where EXPLAIN shows a sequential scan on a hot path. |
| K21 | **`IRecruitmentRequisitionClosed` v1** (aggregates: tenant, requisition, org unit, outcome Closed/Cancelled, closed at, applicants, hires, time-to-fill days, four channel counts, four offer outcome counts; no person-level member — pinned by a test). Staged by `RecruitmentRequisitionClosedEvent.StageAsync` just before the save of every close path (hire closure when `TryClose` closes it, `CloseAsync`, `CancelAsync`, position-freeze cascade) so the MassTransit EF outbox writes it in the same transaction. The aggregates are the saved rows overlaid with the tracked unsaved changes of that save; a hire made in the same save counts from now. `IPublishEndpoint` is an optional last constructor parameter of `RecruitmentRequisitionLifecycleService` and `EstablishmentChangeApplier` (DI supplies it; manual test constructions keep compiling and publish nothing). | Atomic with the close without new transactions; decision 9. |
| K22 | **Dev.Analytics**: new table `RecruitmentRequisitionFacts` (migration `ANA032_RecruitmentRequisitionFacts`, unique `(TenantId, RequisitionId)`, RLS hardening re-applied with the idempotent `TenantRlsMigrationHelper`), consumer `RecruitmentRequisitionClosedConsumer` upserting per requisition (redelivery = same row; the global inbox filter also dedupes by message id). «التوظيف» card on `Dashboards/Index` (last 12 months): closed / cancelled counts, filled and median time-to-fill, channel and offer-outcome breakdowns with counts 1–4 shown «<5» (`RecruitmentFactRules`, threshold 5); the median is suppressed with a small filled count. Demo data, `AnalyticsEvents`, read models and `Ac19` untouched. | Decisions 7, 9. |
| K23 | **Semantic layer**: six measures (`recruitment_funnel`, `_time_to_fill`, `_time_to_hire`, `_offer_acceptance`, `_appeal_rate`, `_open_vacancies`) added to `docs/ana/semantic-layer.json` `measures` in their shape plus `kpi_ref`, `as_of`, `privacy` (min group 5); PROD-006 has no measures list, so `semanticMetrics.registeredMeasures` was added (id, kpiRef, ownerRole, grain, asOf, privacyMinCellSize, sourceRef). Both validators PASS unchanged. Contract registered in `docs/rel-compat/event-schema-inventory.json` (category analytics, outbox) and `docs/MESSAGE_EVENT_MATRIX.md` §2; REL-004 validator PASS. | Additive. |
| K24 | **Approval SLA reads the workflow definitions through the sanctioned system bypass** (`IgnoreTenantFilterForSystem(PolicyResolution)`, current tenant or `TenantDefaults.DefaultTenantId`, as `WorkflowStarter`): the national workflow pack is published in the default tenant, so under a tenant context the tenant-filtered read found no recruitment definitions and the approval KPI was empty for every agency. Found by `Hcm032AcceptanceSeedTests`; regression test `Approval_sla_resolves_definitions_of_the_default_tenant_under_a_tenant_context`. Instances stay tenant-filtered. | Real bug the single-tenant scale and SQLite fixtures could not show. |
| K25 | **AC-HCM-32 fixtures** (`AcceptanceRecruitmentReportsPatch`, ids `…0801–0804`, positions `AC32-POS-*`, events suppressed and marked planned, national ids `320320000NN`) — added once; the tests are GET-only so they are repeatable. | Isolation from the other journeys. |
| K18 | **Exports**: Excel sheets المؤشرات، القمع (with stage time and channels)، طلبات الشاغر (all active requisitions, capped by `ExportQueryLimits`)، الاعتمادات المتأخرة، العروض، الاستئنافات، الشواغر المفتوحة, plus المسابقات for unrestricted users; every sheet starts with the provenance block (report, period, scope, generation time UTC, definitions version, source); numbers are numeric cells, dates `yyyy-MM-dd` text. PDF = the same summary (KPIs, funnel, overdue approvals, vacancy counts) on the official RTL layout. CSV with RFC 4180 CRLF line ends and a formula-injection guard. | REP-001 provenance; culture safety. |

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
| P1 | Domain: `Enums/OfferDeclineReasonCategory.cs`, `Services/RecruitmentKpi/{KpiStatistics,FunnelReachRules,TimeToFillRules,ApprovalSlaRules,SmallCellSuppression}.cs`, `Entities/OfferLetter.cs`; Infrastructure: `HRCoreDbContext`, migration + snapshot, `RecruitmentOfferService` (decline category); Web: `OfferLettersController`, `OfferOnBehalfService`, `CandidatePortalController`/`Service`, `CandidatePortalViewModels` (`CandidatePortalDeclineForm`), `DeclineOfferForm`, `Views/OfferLetters/Details.cshtml`, `Views/CandidatePortal/Index.cshtml`; docs `STR_007_KPI_DEFINITIONS.md`, `str-value/kpi-matrix.json`; tests `Hcm032{KpiStatistics,FunnelReachRules,ApprovalSlaRules,DeclineCategory}Tests` |
| P6 | MessageContracts `Events/IRecruitmentRequisitionClosed.cs`; HR.Core `Analytics/RecruitmentRequisitionClosedEvent.cs`, `RecruitmentHireClosureService`, `RecruitmentRequisitionLifecycleService`, `EstablishmentChangeApplier`; Dev.Analytics `Domain/Entities/RecruitmentRequisitionFact.cs`, `Domain/Services/RecruitmentFactRules.cs`, `Infrastructure/Consumers/RecruitmentRequisitionClosedConsumer.cs`, `AnalyticsDbContext`, migration `20261029123218_ANA032_RecruitmentRequisitionFacts` + snapshot, `Web/Controllers/DashboardsController.cs`, `Views/Dashboards/Index.cshtml`; docs `ana/semantic-layer.json`, `prod-semantic/semantic-layer-matrix.json`, `rel-compat/event-schema-inventory.json`, `MESSAGE_EVENT_MATRIX.md`; `RecruitmentReportsController.Exports.cs` merged into `RecruitmentReportsController.cs`; tests `Hcm032AnalyticsPublicationTests`, `Hcm029CandidateAcceptedV4Tests` (modified), `Hcm032ExportTests` (path) |
| P5 | `HRCoreDbContext` (two indexes), migration `HCM032_AnalyticsIndexes` + snapshot; `IntegrationTests/Hcm032RecruitmentAnalyticsScaleTests.cs`; `perf/hcm032-recruitment-analytics-scale.ps1` |
| P4 | Web: `Controllers/RecruitmentReportsController.Exports.cs`, `Services/RecruitmentReportDocuments.cs`, `Views/RecruitmentReports/Index.cshtml` (buttons); Infrastructure: `Analytics/{RecruitmentApplicationsCsv,RecruitmentReportAccess}.cs`, `RecruitmentMetricsService` (export id queries, requisition page cap), `ExportJobs/HRCoreExportJobService.cs` (template, payload, download re-check); SharedKernel `ExportJobModels.cs` (template constant); `docs/QUERY_EXPORT_INVENTORY.md` (regenerated); tests `Hcm032ExportTests` |
| P3 | Web: `Controllers/RecruitmentReportsController.cs`, `ViewModels/RecruitmentReportsViewModels.cs` (filter form, view models, `RecruitmentReportFormat`), `Views/RecruitmentReports/{Index,Requisition}.cshtml`, `wwwroot/js/recruitment-reports.js`, `Views/Shared/_Layout.cshtml` (menu «تقارير التوظيف»); `RecruitmentMetricsModels` (overdue row carries requisition/competition id); tests `Hcm032RecruitmentReportsControllerTests` |
| P2 | `Infrastructure/Services/Recruitment/Analytics/{RecruitmentMetricsService,RecruitmentMetricsModels}.cs`, `RecruitmentOptions` (`RecruitmentAnalyticsOptions`), `HR.Core.Web/Program.cs` (DI); tests `Hcm032MetricsFixture`, `Hcm032RecruitmentMetricsTests` |
| P0 | `reports/plan32-baseline.md`, `reports/plan32-progress.md`, `docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md`, `docs/acceptance-plans/execution/PLAN-32-AGENT-PROMPT.md` (added as-is) |

## Migrations

| Migration | Phase | Change |
|-----------|-------|--------|
| `20261029111828_HCM032_OfferDeclineCategory` | P1 | nullable `OfferLetters.DeclineReasonCategory` varchar(20); no data rewritten |
| `20261029122217_HCM032_AnalyticsIndexes` | P5 | two indexes: `IX_RecruitmentApplications_TenantId_AppliedAt`, `IX_RecruitmentApplicationEvents_TenantId_EventType_ToStatus_Oc~`; no data change |
| `20261029123218_ANA032_RecruitmentRequisitionFacts` (Dev.Analytics) | P6 | new table `RecruitmentRequisitionFacts` + 2 indexes + idempotent RLS hardening; no existing data touched |

## Test results

| Phase | Run | Result |
|-------|-----|--------|
| P0 | Baseline filter `Hcm02|Hcm030|Hcm031|Reports|Str007|Ana001|Prod006|Perf004` | 1008 / 1008 pass |
| P0 | STR-007 / ANA-001 / PROD-006 validators | PASS / PASS / PASS |
| P1 | `Hcm032` unit tests | 47 / 47 pass |
| P1 | Regression `Hcm02|Hcm03|Reports|Str007|Ana001|Prod006|Perf004|ValueKpi|CandidatePortal|Offer` | 1080 / 1081 — the failure is `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50` (environment-only baseline list) |
| P1 | STR-007 validator (22 KPIs); `has-pending-model-changes` | PASS; none |
| P2 | `Hcm032RecruitmentMetricsTests` (every KPI vs the hand tables; unrestricted, branch, denied, out-of-scope, competition-filter, org-unit, channel/requisition filters; names/references) | 21 / 21 pass |
| P2 | Regression `Hcm032|Hcm02|Hcm03|Reports|Str007|Perf004|Program|Options` | 1059 / 1059 pass |
| P3 | `Hcm032*` unit tests | 82 / 82 pass |
| P3 | Regression `Architecture|Ux0|Csp|A11y|Accessib|Bola|Hcm006|Nonce|Authoriz|Reports|Hcm02|Hcm03|Layout|Menu|Nav` | 1167 / 1168 — the failure is `BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard` (environment-only baseline list; offenders are HR.Contracts/Payroll, not PLAN-32) |
| P4 | `Hcm032*` unit tests | 96 / 96 pass |
| P4 | Regression `Inventory|Perf00|Export|Hcm032|Reports|Hcm02|Hcm03|Pdf` | 1131 / 1132 — the failure is `ArabicPdfGenerationTests.Pdf_arabic_validate_script_passes` (execution policy; the script passes with Bypass) |
| P4 | `inventory-unbounded-queries.ps1` | regenerated (401 rows, 20 unbounded — none in PLAN-32 code) |
| P5 | Scale (PostgreSQL 16, 50,000 apps / 250,000 events / 2,000 requisitions / 20,000 offers / 5,333 workflow instances / 20 units; seeding 35 s) — before indexes | unrestricted 1,219 ms (cold 2,148), branch 275 ms (cold 344) — PASS |
| P5 | Scale — after `HCM032_AnalyticsIndexes` | unrestricted 1,225 ms (budget 3,000), branch 299 ms (budget 1,500) — PASS; per step unrestricted: funnel 718, time-to-fill 136, stage time 115, requisitions 131, approvals 47, rest < 30 |
| P5 | `Migration|Schema|Index|Hcm032` unit filter; `has-pending-model-changes` | 226 / 226 pass; none |
| P6 | `Hcm032AnalyticsPublicationTests` | 8 / 8 pass |
| P6 | Full unit suite (before the two fixes below) | 3951 / 3971 — 10 baseline env failures, 7 `*_validate_script_passes` (execution policy; scripts pass with Bypass), and 3 real: `Hcm029CandidateAcceptedV4Tests` ×2 (now also publishes the close event → expectations updated) and `ModuleSidebarNavTests` (partial controller split → exports merged into `RecruitmentReportsController.cs`) |
| P6 | After the fixes: `Hcm029|ModuleSidebarNav|Hcm032|Est0|Establishment|Requisition|Rel004|MessageContract|Analytics|Outbox` | 354 / 354 pass |
| P6 | ANA-001 / PROD-006 / REL-004 validators; Analytics `has-pending-model-changes` | PASS / PASS / PASS; none |
| P7 | Merge of local `main` (006ff3a) into the branch | clean (merge commit `608652e`); our AC-31 `ReasonCategory` line kept |
| P7 | `Hcm032AcceptanceSeedTests` (+ the default-tenant regression test in `Hcm032RecruitmentMetricsTests`) | pass; `Hcm032*` 120 / 120 with `Ten002` |
| P7 | Full unit suite | 3959 / 3976 — the 17 failures are environment-only: the 10 baseline ones (`Str005`×4, `Learning…Certificate_Number`, `Syr001`×2, `Ten002…IgnoreQueryFilters` (offenders `BenefitPlanSeeder`, `AcceptanceCandidateExperiencePatch` — not PLAN-32), `San001…Validate_script`, `Bola…org_scope_guard`) and 7 `*_validate_script_passes` (execution policy) |
| P7 | Integration `Hcm02|Hcm03|PublicCareers` (PostgreSQL 16) | 33 pass, 2 skipped (opt-in scale tests), 1 failed: `Hcm028AppealGovernanceMigrationTests` (Hijri culture, environment-only baseline) |
| P7 | **AC-HCM-32a..d live** (`hrm-acceptance`, `hr-core-web` and `dev-analytics-web` rebuilt from this branch; migrations `HCM032_*` applied at startup; fixtures seeded) | **4 / 4 Pass** (`acceptance-evidence/AC-HCM-32/`); first run 3 / 4 — the test's xlsx reader missed the `<x:t>` prefix (test fix, no product change) |
| P7 | Decline paths live: AC-HCM-27 and AC-HCM-30 | 10 pass, 1 skip (AC-30c: File.Storage not in the stack), 1 fail: `Ac27_f_g` at the HR paper acceptance (line 223: offer stays Issued) — the step uploads acceptance evidence to File.Storage, which the stack does not run (MinIO image not pullable, as in PLAN-30/31); PLAN-32 does not touch the accept path. The HR decline with `ReasonCategory` (27a/c/d/h) and the portal decline (30f/g) pass. Their evidence folders were restored (not committed). |

## Modified existing tests

| Test | Why |
|------|-----|
| `Hcm027OfferOutcomeTests`, `Hcm027ReserveListTests`, `Hcm031ReserveRecallTests` | `DeclineAsync` / `DeclineAndDraftNextAsync` take the new required category |
| `Hcm030OnBehalfAcceptanceTests` | same for `OfferOnBehalfService.DeclineAsync`; `IssueOfferAsync`/`CreateService` made `internal` for reuse |
| `Hcm030CandidatePortalTests` | `DeclineOfferAsync` takes the category; the controller takes `CandidatePortalDeclineForm`; the no-Guid form check covers the new form; `LinkCitizenAsync`/`CreateController` made `internal` for reuse |
| `AcHcm27OfferMeritTests`, `AcHcm31CompetitionTests`, `AcHcm30CandidateExperienceTests` | decline form posts carry the new required `ReasonCategory` / `Category` field |
| `Hcm029CandidateAcceptedV4Tests` (2 tests) | the hire fills the last vacancy, so the requisition-closed aggregate is now published too; the exact hire-event lists are unchanged (asserted without/with it) |

## Open issues

- **Another session (PLAN-31 acceptance) worked in this same checkout** (observed 14:22–14:28). It has since moved its
  work to its own worktree `.claude/worktrees/plan31-acceptance` (branch `fix/plan-31-acceptance-run`) and reverted its
  files here. It uses the **acceptance stack (hrm-acceptance)** for AC-HCM-31 "for the next while"; the images are built
  from `main` (no PLAN-32 code) — relevant for P7. This session commits explicit paths only.
- 15:xx update from that session: local `main` is now `006ff3a` (AC-HCM-31 live-run fixes: gateway workflow actions for
  results approval, `AcceptanceCompetitionPatch` cycle fix, `AcHcm31CompetitionTests` waits — our `ReasonCategory` line
  untouched). The acceptance stack is free and was rebuilt from `main`; `scripts/acceptance-reset.ps1` re-arms it.
  **Merge `main` into this branch before P7**, and merge this branch into local `main` when PLAN-32 is done (user rule).
- `ArabicPdfGenerationTests.Pdf_arabic_validate_script_passes` fails here only because the test host's PowerShell
  execution policy blocks the script; `powershell -ExecutionPolicy Bypass -File scripts/pdf-arabic-validate.ps1` passes
  (same environment class as the baseline `*Validate_script_passes` entries). The full unit run shows the same for
  `Iam005`, `Rel006`, `Doc001`, `Prv003`, `Iam002`, `Psirt001` `*_validate_script_passes` (each script passes with
  `-ExecutionPolicy Bypass`, checked 2026-10-29).

## Next step

P8 — docs (drafted, uncommitted: `PLAN-32-EXECUTION.md`, `HCM_001_GAP_STUDY.md` G-21, `HCM_001_RECRUITMENT.md`;
to do: PLAN-32 status/notes/deviations, `ANA_001_ANALYTICS.md`, `REP_001_REPORTS.md`, `DATABASE_SCHEMA_INVENTORY.md` by hand
(no Python)); `scripts/hcm032-validate.ps1` (drafted) run to PASS; independent review subagent + refuters; fixes with
regression tests; `reports/plan32-final-report.md`; commit; then merge the branch into local `main` (user rule) and test there.
