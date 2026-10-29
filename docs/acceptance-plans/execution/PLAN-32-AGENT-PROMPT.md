# Task: PLAN-32 — Recruitment reports and KPIs: funnel, time-to-fill/hire, offers, channels, appeals, merit overrides, open vacancies, approval SLA, scoped exports (one autonomous session)

## How to run this task — read before anything else

This is a **single prompt for one autonomous session**. Run it as phases **P0–P8** in order.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx`, 0 errors.
2. Run that phase's tests, plus the phase's regression filter.
3. Commit, using the phase's commit message.
4. Update **`reports/plan32-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions taken (with reasons);
   - files changed;
   - migrations;
   - test results;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan32-progress.md` and `git log --oneline -20` **before doing anything else**. Continue from the recorded next step. Never redo a committed phase.

**Context discipline.** Use subagents for broad read-only searches. On the mounted repo, prefer `git grep`: a recursive grep over `bin`/`obj` is very slow.

**Orchestration rules** (ultracode / dynamic workflows may be on):
- Parallel agents are allowed for:
  - read-only exploration;
  - writing **separate new test files**;
  - independent review.
- Never let two agents edit the same file concurrently.
- These edits are made **serially by you, the lead**:
  - `HRCoreDbContext`, migrations and the model snapshot
  - `Program.cs`
  - `RecruitmentOptions`
  - seeders
  - shared enums
  - `docs/str-value/kpi-matrix.json`
  - the semantic-layer JSON files
- One migration at a time, created only after the previous one is committed.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write a `BLOCKED:` entry in the progress file, with the question and the options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

**Keep it simple and exact.** Every number on the screen must come from a documented definition and be proved by a test against a hand calculation.

---

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) / ClosedXML / QuestPDF engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). The machine culture is **ar-SA (Hijri calendar)**.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md` (Arabic). This prompt refines it. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md` — **G-21**.
3. Final reports and implementation notes of PLAN-27..31:
   - `reports/plan30-final-report.md`, `reports/plan31-final-report.md`, `reports/plan31-progress.md`
   - the plan docs
4. KPI and analytics governance:
   - `docs/STR_007_KPI_DEFINITIONS.md` and its machine source `docs/str-value/kpi-matrix.json` (required KPI fields; `privacyRules.minCellSize = 5`; the existing `hire_cycle_time`)
   - `docs/STR_007_DASHBOARD_TENANT_PRIVACY.md`, `docs/STR_007_ANTI_GAMING.md`
   - `docs/ANA_001_ANALYTICS.md`, `docs/ana/semantic-layer.json`
   - `docs/PROD_006_*.md` (KPI definitions, privacy thresholds, report provenance), `docs/prod-semantic/semantic-layer-matrix.json`
   - `docs/REP_001_REPORTS.md`
   - `docs/EXPORT_JOBS.md`, `docs/QUERY_EXPORT_INVENTORY.md`
   - validators: `scripts/str007-value-kpi-validate.ps1`, `scripts/ana001-validate.ps1`, `scripts/prod006-semantic-layer-validate.ps1`, `scripts/inventory-unbounded-queries.ps1`, `scripts/ac026-scan-pii.ps1`
5. Read fully before changing:
   - `HR.Core.Web/Controllers/ReportsController.cs` and `Views/Reports/Index.cshtml` — the pattern for scope, ClosedXML, QuestPDF, `SecurityAudit.Log(BulkExport)`, `ExportQueryLimits`, `IHRCoreExportJobService`, Chart.js
   - `HR.Core.Infrastructure/ExportJobs/{HRCoreExportJobService,ExportJobRequestedConsumer}.cs`
   - `src/Shared/HRManagement.SharedKernel/Exports/ExportJobModels.cs`
   - `HR.Core.Infrastructure/OrgScopeService.cs`, `src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs` (`OrgAccessScope`)
   - `HR.Core.Domain/Entities/{RecruitmentApplication,RecruitmentApplicationEvent,RecruitmentRequisition,FundedPosition,OfferLetter,RecruitmentAppeal,RecruitmentCompetition*}.cs`
   - `Workflow/{WorkflowInstance,WorkflowDefinition}.cs`
   - `src/Shared/HRManagement.SharedKernel/Workflow/{WorkflowStepHistoryEntry,WorkflowStepSnapshot}.cs`
   - the HR and portal offer-decline paths: `OfferLettersController.DeclineOffer`, `DeclineOfferForm`, the PLAN-30 portal decline, `RecruitmentOfferService.DeclineAsync`
   - `HR.Core.Web/ModelBinding/InvariantDecimalModelBinder.cs` (PLAN-31)
   - Dev.Analytics: `src/Services/DevAnalytics/*` (consumers, `DemoAnalyticsSeeder`, dashboards), `src/Tests/HRManagement.AcceptanceTests/Ac19AnalyticsReconciliationTests.cs`

## Decisions for this implementation (record them in P0)

| # | Decision |
|---|----------|
| 1 | **Timings come from `RecruitmentApplicationEvent`** (PLAN-30, recorded since 2026-10-23). Older rows use **documented evidence fallbacks** (D2), and every time-based KPI shows its **coverage** (% of the cohort with an event history). **No synthetic event backfill.** |
| 2 | **Time-to-fill** starts at the requisition's `ApprovedDate` (fallback `RequestedDate`, counted as "fallback" in coverage). This aligns with the existing STR-007 `hire_cycle_time` ("approved vacancy"). Do **not** change `hire_cycle_time` — its definition is frozen (anti-gaming). Add new KPIs. Record the deviation from the plan's `RequestedDate`. |
| 3 | **Channel mix** uses `RecruitmentApplication.IntakeChannel` (PLAN-30), not the source-id columns. |
| 4 | **Decline reasons** get a category: new `OfferLetter.DeclineReasonCategory`, required for new declines on both the HR and portal paths. The free text is kept. Old rows show «غير مصنف». Expired offers count as their own outcome, not a reason. |
| 5 | **Approval SLA.** The step's `TimeoutHours` when set. Otherwise `Recruitment:Analytics:ApprovalSlaHours:{WorkflowCode}`. Otherwise `Recruitment:Analytics:DefaultApprovalSlaHours` (48). No seeded definition sets `TimeoutHours` today, so this is an **organizational policy value**: provisional, documented, and not a legal value. |
| 6 | **Scope** goes through `OrgScopeService`. A requisition's org unit is its funded position's `OrgUnitId`. Scoped users (branch managers) see only requisitions and applications in their units, and an out-of-scope org-unit filter returns no data. Competition aggregates are shown **only** to unrestricted users. An allocated competition application counts under its allocated requisition's unit. |
| 7 | **Privacy.** No national ID (not even masked) in any report, drill list or export. Candidate **names only for `HrCoreWrite`**; others see the application reference (`IntakeChannel` + short id or the citizen reference number). Small-cell suppression (< `minCellSize` 5 → «<5») applies to anything published outside HR.Core (D9) and to any breakdown by a personal attribute. **Fairness metrics are deferred**: no GOV-001 decision, and candidate gender is not collected. Record this. |
| 8 | **Exact counts** inside the tenant (ANA-001): no sampling or approximation. A daily snapshot table is built **only if** the scale budget in P5 fails. |
| 9 | **Dev.Analytics publication and semantic-layer registration** are in scope as P6. They are **additive**: the demo data and `Ac19` stay untouched. |
| 10 | **PLAN-25 stays deferred.** Do not change the authorization or org scope of the existing recruitment list screens. `RecruitmentApplicationsController` and `RecruitmentRequisitionsController` do not apply org scope today, while `RecruitmentAppealsController` and `ReportsController` do. Record this as a follow-up finding in the final report. |

## Scope boundaries

- **Out of scope:** predictive analytics; external BI (Grafana and similar); fairness metrics; changes to existing list scoping.
- Arabic UI; nonce CSP — inline `<script>` blocks get the nonce from `CspNonceTagHelper`, but **no inline event handlers or style attributes**; no native `confirm()`; accessible tables and charts (caption, `scope`, a text alternative or data table for every chart).
- **Culture safety:**
  - dates in reports and exports are **Gregorian** with an explicit format and invariant culture, whatever the request culture;
  - numbers are invariant in exports;
  - tests must not depend on the current culture (the ar-SA Hijri issue from PLAN-31).
- **Never commit:**
  - `global.json`
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets
  - test-rewritten `testdata/*/out/*.json` — run `git checkout -- testdata` before each commit
- Do not push.

## Repository facts (verified on `main` @ `bcc582a`, 2026-10-29 — re-verify)

- **`RecruitmentApplicationEvent`** (append-only, no PII):
  - fields: `RecruitmentApplicationId`, `EventType`, `FromStatus`/`ToStatus` (int of the triggering entity's status enum), `RelatedEntityId`, `OccurredAt`, `NotificationsPlannedAt`;
  - types in `RecruitmentApplicationEventTypes`: `Received`, `StatusChanged`, `Interview*`, `Offer*`, `AppealFiled`/`AppealDecided`, `OfferExpiryReminder`, and seven competition events;
  - recorded from `HRCoreDbContext` saves; explicit events are added by services.
- **`RecruitmentApplication`:**
  - `Status` (int);
  - `IntakeChannel` (`RecruitmentIntakeChannel`: Public / Internal / HrEntered / Legacy);
  - timestamps: `AppliedAt`, `ScreenedAt`, `RankedAt`, `ReviewedAt` (overwritten on every transition), `RejectedAt`, `ReserveListedAt`;
  - `RejectionKind`, `HiredEmployeeId`, `ActiveOfferLetterId`, `EligibilityFlags`;
  - `CompetitionId`/`CompetitionLineId` — `RequisitionId` is **null** for competition applications until allocation.
- **`RecruitmentRequisition`:**
  - `RequestedDate`, `ApprovedDate`, `PublishedAt`, `ClosedDate`, `CancelledAt`, `ApplicationClosingDate`, `FundedPositionId`;
  - `RequisitionStatus`: Draft, PendingApproval, Approved, Published, Closed, Cancelled.
  - `FundedPosition` has `Status` (Vacant/Occupied/Frozen) and `OrgUnitId`.
- **`OfferLetter`:** `IssuedDate`, `AcceptedDate`, `DeclinedAt`, `DeclinedBy`, `DeclineReason` (free text), `ExpiredAt`, `RevokedAt`, `IsMeritOverride`, `IsExceptionOffer`; acceptance channel (PLAN-30).
- **`RecruitmentAppeal`:**
  - `Kind` (Rejection / ResultObjection), `Status`, `FiledBy`, `DecidedAt`;
  - `Outcome` (`RecruitmentAppealOutcome`): Upheld, UpheldRankCorrection, Dismissed, UpheldNoRemedy, LegacyReopen, ObjectionUpheld, ObjectionDismissed.
- **Workflows:**
  - `WorkflowInstance`: `DefinitionId`, `EntityType`/`EntityId`, `CurrentStepLevel`, `Status`, `StepHistoryJson` (list of `WorkflowStepHistoryEntry`: `Level`, `StepName`, `Status`, `ActorId`, `ActionAt`), `StartedAt`, `CompletedAt`.
  - `WorkflowDefinition.StepsJson` holds `WorkflowStepSnapshot` (`Level`, `Name`, `RoleRequired`, `TimeoutHours?`, `EscalationRole?`). `TimeoutHours` is set nowhere.
  - Recruitment workflow codes: `RECRUITMENT-REQUISITION-APPROVAL`, `RECRUITMENT-OFFER-APPROVAL`, `RECRUITMENT-OFFER-OVERRIDE-APPROVAL`, `RECRUITMENT-APPEAL-REVIEW`, `RECRUITMENT-SELECTION-COMMITTEE`, `RECRUITMENT-HIRE`, `RECRUITMENT-COMPETITION-APPROVAL`, `RECRUITMENT-RESULTS-APPROVAL`.
- **Policies:** `HrCoreReadOnly` = Admin, HrCore, HrCoreSupervisory, HrCoreExecutive, BranchManager, HrManagementApprover (+ service lookup). `HrCoreWrite` = Admin, HrCore (+ service workflow scope).
- **Exports:** the `ExportJobs` mechanism streams **CSV** line by line (templates in `ExportJobTemplates`, `HRCoreExportJobService.ResolveTemplate`). Excel and PDF are produced synchronously in `ReportsController` and bounded by `ExportQueryLimits`.
- **STR-007:** `kpi-matrix.json` requires these fields: `id`, `name`, `definition`, `unit`, `formula`, `sourceRef`, `ownerRole`, `auditorRole` (≠ owner), `target`, `baselineRequired`, `antiGamingRules`, `benefitCategory`, `moduleIds`.

## Stop (record `BLOCKED:` and end the turn) if

- a KPI definition in the plan conflicts with a frozen STR-007 definition in a way decision 2 does not cover;
- the decline-category migration or any other migration would rewrite or delete business data (new nullable columns only);
- scope cannot be enforced for some KPI without changing generic authorization (PLAN-25);
- the semantic-layer or KPI validators reject an addition, and satisfying them would mean weakening an existing rule.

---

## KPI definitions (implement exactly; write them into `docs/STR_007_KPI_DEFINITIONS.md` and `kpi-matrix.json`)

**General rules**
- Period `[From, To)` in UTC dates. Default: the last 12 full months.
- "Days" = calendar days between the two UTC dates (`DateOnly` difference).
- **Median:** even n → mean of the two middle values (1 decimal).
- **P90:** nearest-rank, i.e. the value at position `ceil(0.9 × n)` in ascending order.
- Every KPI shows `n` and its coverage.

| id (new) | Definition |
|----------|------------|
| `recruitment_funnel` | **Cohort** = applications with `AppliedAt` in the period (and the filters). For each stage, the count of cohort applications that **reached** it: Applied → Screened → Shortlisted → Interviewed → Assessed → Ranked → OfferIssued → OfferAccepted → Hired. **"Reached"** = an event shows the stage (`StatusChanged` to that status or beyond; `InterviewScheduled`; `OfferIssued`; `OfferAccepted`), **or** evidence on older rows: `ScreenedAt` → Screened; `RankedAt` → Ranked; any offer of the application with `IssuedDate` → OfferIssued; `AcceptedDate` → OfferAccepted; `HiredEmployeeId` or status Hired → Hired. The current status implies every earlier stage, except the terminal Rejected/Withdrawn/OfferDeclined, which imply nothing. Also show the current-status distribution of the cohort. |
| `recruitment_time_to_fill` | For requisitions **filled** in the period: the first application of the requisition reaching Hired, with the Hired time in the period. Days from `ApprovedDate` (fallback `RequestedDate`) to the Hired time. **Hired time** = the `StatusChanged` → Hired event; fallback: the hired employee's `CreatedAt` (coverage marks fallbacks). Median, P90, n. |
| `recruitment_time_to_hire` | For applications hired in the period: days from `AppliedAt` to the Hired time. Median, P90, n. |
| `recruitment_stage_time` | Median days spent in each stage, for cohort applications with events (between consecutive `StatusChanged` events). Event-covered rows only. |
| `recruitment_offer_acceptance_rate` | Offers whose **final outcome** happened in the period: Accepted ÷ (Accepted + Declined + Expired). **Revoked are excluded** (STR-007 anti-gaming `exclude-cancelled-offers`). Show the counts too. |
| `recruitment_offer_decline_reasons` | Declines in the period by `DeclineReasonCategory` (old rows «غير مصنف»), and by `DeclinedBy` (Candidate / HR on behalf). |
| `recruitment_channel_mix` | Cohort by `IntakeChannel`, with the hire rate per channel (hired ÷ applied). |
| `recruitment_appeal_rate` | Appeals (`Kind = Rejection`) filed in the period ÷ applications rejected in the period with an appealable `RejectionKind` (the configured kinds). Outcome distribution of appeals decided in the period. Objections (`ResultObjection`) are reported separately: filed, upheld, dismissed. |
| `recruitment_merit_overrides` | Offers created in the period with `IsMeritOverride`, and with any exception flag (`IsExceptionOffer`), ÷ offers created. |
| `recruitment_open_vacancies` | As of today: `Vacant` funded positions with **no** requisition in Draft/PendingApproval/Approved/Published (and not attached to an open competition line), plus requisitions `Published` for more than `Recruitment:Analytics:StalePublishedDays` (default 60). List the oldest first. |
| `recruitment_approval_sla` | Recruitment workflow instances. **(a)** `InProgress` instances whose current step age > SLA (D5). Step start = the `ActionAt` of the latest history entry, or `StartedAt` when there is none. **(b)** For steps completed in the period: median and P90 step duration per workflow code and step. |
| `recruitment_competitions` | Unrestricted users only. Per competition: applicants, admitted, present, passed, allocated, reserve, recalled, objections (filed/upheld). |

**Every new KPI in `kpi-matrix.json`** gets:
- `ownerRole: "product-hr"`, `auditorRole: "audit"`;
- `baselineRequired: true` for the time KPIs;
- anti-gaming rules: `exclude-cancelled-offers` (offer rates), `freeze-definition-before-quarter` (all), `count-fallback-separately` (time KPIs);
- `moduleIds: ["hr-core"]`;
- targets as `"TBD"` **only if the validator allows it** — otherwise `BLOCKED`. Never invent targets.

---

## P0 — Baseline, orientation, notes

1. Run `git checkout -- testdata`. From `main`, create `feature/plan-32-recruitment-analytics`.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm02|Hcm030|Hcm031|Reports|Str007|Ana001|Prod006|Perf004"
   ```

   Record the results in `reports/plan32-baseline.md`, including the known environment-only (culture) failures.
3. Run `scripts/str007-value-kpi-validate.ps1`, `scripts/ana001-validate.ps1` and `scripts/prod006-semantic-layer-validate.ps1`, and record them.
4. Map, using subagents, and record in the progress file:
   - every place an offer is declined (HR, portal, withdrawal, competition recall paths);
   - where requisition org units come from;
   - how competition applications and allocations appear in the data;
   - the existing indexes on the tables the KPIs read.
5. Add a «تعديلات قبل التنفيذ (2026-10-29)» block (Arabic) to the top of the PLAN-32 doc with decisions 1–10. Set `Depends on` to "PLAN-26..31 منفذة، PLAN-25 مؤجلة".
6. Create `reports/plan32-progress.md`. If `docs/acceptance-plans/execution/PLAN-32-AGENT-PROMPT.md` (this prompt) is untracked, include it in the commit.

Commit: `docs(hcm032): baseline, pre-implementation notes and progress log`.

## P1 — Definitions, pure calculators, decline category

- **Pure Domain calculators** (`HR.Core.Domain/Services/RecruitmentKpi/*`), testable without EF:
  - `KpiStatistics` (median, P90 nearest-rank, rates with zero-denominator → «—»);
  - `FunnelReachRules` (events + evidence → the reached stage set);
  - `TimeToFillRules`, `ApprovalSlaRules` (step start/age/SLA resolution);
  - `SmallCellSuppression` (threshold from options, default 5).
- **Migration `HCM032_OfferDeclineCategory`:** nullable `OfferLetter.DeclineReasonCategory`, string, values:
  - `Salary` «الراتب»
  - `Location` «مكان العمل»
  - `OtherOffer` «عرض آخر»
  - `Personal` «أسباب شخصية»
  - `JobContent` «طبيعة العمل»
  - `Other` «أخرى»

  `DeclineAsync` requires the category for new declines from HR and the portal: add a `<select>` to both forms. Withdrawal-driven declines use `Other` with their text. Old rows stay null and display «غير مصنف».
- **Docs:** write the definitions table above into `docs/STR_007_KPI_DEFINITIONS.md` (Arabic + English names), and the new entries into `kpi-matrix.json`. Run the STR-007 validator.
- **Tests:**
  - `Hcm032KpiStatisticsTests` — odd/even median, P90 edge cases (n = 1, 9, 10, 11), rates;
  - `Hcm032FunnelReachRulesTests` — events vs. evidence, terminal statuses;
  - `Hcm032ApprovalSlaRulesTests`;
  - `Hcm032DeclineCategoryTests` — required on new declines on both paths; old rows unaffected.

Commit: `feat(hcm032): recruitment KPI definitions, calculators and offer decline categories`.

## P2 — `RecruitmentMetricsService` (read-only, scoped)

- `HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentMetricsService` — `AsNoTracking` only, tenant-filtered.
- **Filters:** period, org unit (within scope), requisition, channel, competition (unrestricted users only).
- **Scope:** pass the `OrgAccessScope`:
  - `IsDenied` → empty result;
  - `MustFilter` → restrict by the requisition's funded-position org unit;
  - an org-unit filter outside scope → empty result (**AC-HCM-32b negative**).
- **Queries:** projections and grouped queries; no N+1. Load events only for the cohort's application ids, in batches.
- **`StepHistoryJson`** is JSON in a text column: parse it in memory **only for the bounded set of recruitment instances in scope and period**. Document the bound.
- One method per KPI returning DTOs (values, n, coverage, fallback count), plus `GetRequisitionTableAsync` (requisitions with status, ages, applicants, stage counts, time-to-fill when filled).
- **Options** `Recruitment:Analytics:*`: `StalePublishedDays` (60), `DefaultApprovalSlaHours` (48), `ApprovalSlaHours:{Code}`, `MinCellSize` (5), `DefaultPeriodMonths` (12).
- **Tests** — `Hcm032RecruitmentMetricsTests` with a **hand-built fixture** (SQLite):

  | Fixture element | Count / contents |
  |-----------------|------------------|
  | requisitions | 3, across 2 org units |
  | applications | ~30, every channel |
  | event histories | explicit `OccurredAt` (suppress the recorder; write the events yourself) |
  | legacy rows | some without events (evidence fallbacks) |
  | offers | in every outcome, including revoked |
  | appeals and objections | yes |
  | workflow instances | with history and an overdue step |
  | competition | one, with allocated and reserve candidates |

  Assert **every** KPI against numbers computed by hand and written in a comment table in the test (**AC-HCM-32a**). Add scope tests: unrestricted, branch, denied, and an out-of-scope filter.

Commit: `feat(hcm032): recruitment metrics service with org scope and coverage`.

## P3 — UI

- **`RecruitmentReportsController`** (`HrCoreReadOnly`):
  - `Index` — filters, KPI cards with n and coverage, a funnel chart (Chart.js horizontal bar plus an accessible data table), the requisitions table (ages, links), an overdue approvals table (**AC-HCM-32d**: entity link, workflow, step, role, age, SLA), offers/declines, channels, appeals/objections, merit overrides, open vacancies, the competitions panel (unrestricted only);
  - `Requisition/{id}` — the funnel, stage times and offer history for one requisition, scope-checked.
- **Drill-through:** links to existing screens. The existing `RecruitmentApplications?RequisitionId=&Status=` filter stays as is (its scoping is D10).
- **Names:** drill lists inside the reports show candidate names **only** for `HrCoreWrite`; others see the reference.
- **Charts:** follow `Reports/Index.cshtml` (local Chart.js, nonce via the tag helper). Pass data as JSON in a `<script type="application/json">` block, or as data attributes. No inline handlers.
- **Menu:** «تقارير التوظيف» under the recruitment menu.
- **Tests:** controller tests (scope, names rule, out-of-scope filter, rendering without culture dependence).

Commit: `feat(hcm032): recruitment reports screen with funnel, requisitions and overdue approvals`.

## P4 — Exports

- **Excel** (ClosedXML, synchronous, aggregates): sheets المؤشرات، القمع، طلبات الشاغر، الاعتمادات المتأخرة، العروض، الاستئنافات، الشواغر المفتوحة. A header row carries period, scope, generation time (Gregorian, invariant), the definitions version, and the source (REP-001 provenance).
- **PDF** (QuestPDF, `HrOfficialDocumentLayout`, Arabic RTL): the same summary.
- **Applications detail:** CSV via the **ExportJobs** mechanism when over `MaxExportRows`, otherwise synchronous. New template `hrcore.recruitment.applications.csv`. Columns:
  - reference;
  - name — **only when the requester had `HrCoreWrite` at request time, captured in the auth snapshot and re-checked at download**;
  - channel, requisition, org unit, status, stage dates, offer outcome.
- **Never a national ID**, not even masked.
- Every export: `SecurityAudit.Log(..., BulkExport, ...)` and `ExportQueryLimits`.
- Regenerate `docs/QUERY_EXPORT_INVENTORY.md` with `scripts/inventory-unbounded-queries.ps1`.
- **Tests** — `Hcm032ExportTests`:
  - no national-ID pattern in any export (reuse the `ac026-scan-pii.ps1` regex or helper) (**AC-HCM-32c**);
  - names present for `HrCoreWrite` and absent for read-only (**AC-HCM-32c negative**);
  - branch scope respected in exports;
  - the job path re-checks the snapshot;
  - Gregorian dates under ar-SA.

Commit: `feat(hcm032): recruitment report exports (Excel, PDF, scoped CSV job) without national id`.

## P5 — Scale

- **Integration test category `Hcm032Scale`** (Testcontainers PostgreSQL):
  - 50,000 applications, 250,000 events, 2,000 requisitions, 20,000 offers, 5,000 workflow instances;
  - the `Index` set of metric calls for a 12-month unrestricted period must finish in **< 3 s** in total, and a branch scope in **< 1.5 s**;
  - write timings to `artifacts/hcm032-perf/` and assert the limits.
- **Indexes:** add supporting indexes in migration `HCM032_AnalyticsIndexes` only where `EXPLAIN` shows a sequential scan on a hot path, for example `RecruitmentApplicationEvents (TenantId, EventType, ToStatus, OccurredAt)` and `RecruitmentApplications (TenantId, AppliedAt)`. Record the plans.
- **Snapshot:** only if the budget still fails after indexes, add `RecruitmentDailySnapshot` with a nightly leader-elected job (exact values, rebuildable, documented). Otherwise record "not needed" with the timings.

Commit: `perf(hcm032): analytics indexes and scale test` (plus the snapshot commit if it was needed).

## P6 — Analytics integration (additive)

- **Semantic layer:** register the new measures (funnel, time-to-fill, time-to-hire, offer acceptance, appeal rate, open vacancies) in `docs/ana/semantic-layer.json` and `docs/prod-semantic/semantic-layer-matrix.json` in their existing shape: owner, grain, as-of semantics, privacy threshold. Run `ana001` and `prod006` validation.
- **Event `IRecruitmentRequisitionClosed`** (MessageContracts):
  - aggregates only: `TenantId`, `RequisitionId`, `OrgUnitId`, `ClosedAt`, applicants, hired count, time-to-fill days, channel counts, offer outcome counts — **no person-level data**;
  - published through the HR.Core outbox when a requisition closes (all close paths: hire, cancel, job);
  - update the message/compat baselines (`scripts/rel004-compat-validate.ps1`, `docs/MESSAGE_EVENT_MATRIX.md`).
- **Dev.Analytics:**
  - consumer storing a `RecruitmentRequisitionFact`, idempotent per requisition;
  - a «التوظيف» card on its dashboard, with small-cell suppression (< 5 → «<5») on channel and outcome breakdowns;
  - **demo data and `Ac19` unchanged**.
- **Tests:** contract compat, consumer idempotency, suppression.

Commit: `feat(ana032): recruitment requisition aggregates to Dev.Analytics and semantic-layer registration`.

## P7 — Acceptance and regression

**`src/Tests/HRManagement.AcceptanceTests/AcHcm32RecruitmentReportsTests.cs`:**
- `EvidenceRecorder("AC-HCM-32", correlationId)`; `Skip.If` for missing preconditions (never `WriteResult("Skip"); return;`).
- **Seed** (deterministic, idempotent `AcceptanceDataSeeder` patch): two org units, requisitions, applications with explicit events and timestamps (recorder suppressed), offers, appeals, and one recruitment workflow instance with a step older than its SLA.

| ID | Scenario |
|----|----------|
| 32a | funnel and time-to-fill values equal the hand-computed seed values |
| 32b | the branch-manager persona sees only its unit (positive); requesting another unit's report returns no data (negative) |
| 32c | the Excel export has no national ID (positive); the read-only persona's export has no names (negative) |
| 32d | the overdue approval appears in the overdue table |

Run with `RUN_ACCEPTANCE=true` if the stack is current (rebuild the `hr-core-web` image if it predates this branch; no Keycloak change is expected) and produce `acceptance-evidence/AC-HCM-32/`. Otherwise report "not run" and why.

**Regression** — green or matching the baseline:
- `Hcm026`–`Hcm031`
- `Reports`, `Perf004`, `Str007`, `Ana001`, `Prod006`
- `Ac19` (if run)
- the offer decline paths (HR and portal)

List every modified test with a one-line justification.

Commit: `test(hcm032): acceptance AC-HCM-32a..d and regression`.

## P8 — Docs, validate script, independent review, final report

- **`scripts/hcm032-validate.ps1`** (pattern `hcm031-validate.ps1`):
  - migrations, key files, options;
  - runs `Hcm032` unit tests and the STR-007 / ANA-001 / PROD-006 validators;
  - checks that no report or export code reads `NationalId`.
- **Docs:**
  - `docs/acceptance-plans/execution/PLAN-32-EXECUTION.md` — prerequisites, commands, personas, Arabic manual steps, evidence folder;
  - the PLAN-32 doc — status (`Implemented` or `Implemented (acceptance evidence pending)`), implementation notes and deviations (decisions 1–10, the judgement calls), deferred items (fairness metrics, list scoping D10, KPI targets, the snapshot if not built);
  - `docs/ANA_001_ANALYTICS.md` (the recruitment section), `docs/STR_007_KPI_DEFINITIONS.md`, `docs/HCM_001_RECRUITMENT.md` (G-21 addressed), `docs/HCM_001_GAP_STUDY.md` (G-21 status), `docs/REP_001_REPORTS.md` (the new report and provenance), `docs/MESSAGE_EVENT_MATRIX.md`, `docs/DATABASE_SCHEMA_INVENTORY.*` (use the generator if one exists).
- **Independent review:** a fresh subagent reads this prompt, the plan and `git diff main...HEAD`. It reports gaps in:
  - the KPI definitions vs. the implementation;
  - scope leaks;
  - PII in reports or exports;
  - culture-dependent output;
  - frozen-definition violations.

  Correctness only. Each finding is attacked by a refuter agent before it is fixed. Fix what survives, with a regression test each, and record it in the progress file.
- **`reports/plan32-final-report.md`** (English; quote Arabic UI strings as-is):
  1. Summary per phase.
  2. Commits (`git log --oneline main..HEAD`).
  3. Migrations.
  4. KPI table with definitions and the coverage behaviour.
  5. Options per environment.
  6. Test results vs. baseline; modified tests.
  7. Scale timings and the snapshot decision.
  8. Acceptance: run or not, evidence.
  9. Decisions and deviations.
  10. Follow-ups, including D10.
  11. Manual test script (Arabic UI paths).

Commit: `docs(hcm032): plan status, KPI definitions, execution guide, validate script, final report`.

## Definition of done

- Every KPI above is defined in STR-007 docs and matrix, implemented exactly, shows n and coverage, and matches a hand calculation in tests.
- Reports and exports respect org scope, never contain a national ID, and show names only to `HrCoreWrite`.
- Overdue approvals are listed against a documented SLA.
- Exports are audited and bounded.
- Output is culture-safe.
- The scale budget is met.
- The Dev.Analytics publication is aggregate-only, with suppression.
- Build has 0 errors; `has-pending-model-changes` reports none; tests pass; all validators pass; `scripts/hcm032-validate.ps1` passes.
- Acceptance is written, and either run with evidence or reported as not run.
- `reports/plan32-final-report.md` exists.

Then reply with a short summary and the path of the final report.
