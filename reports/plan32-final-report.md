# PLAN-32 — Final report

**Date:** 2026-10-29
**Branch:** `feature/plan-32-recruitment-analytics` (local only, not pushed), from `main` @ `bcc582a`; local `main` (006ff3a,
the AC-HCM-31 live-run fixes) was merged in before P7.
**Plan:** [`docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md`](../docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md) —
Status **Implemented** (AC-HCM-32a..d pass live). Execution guide:
[`docs/acceptance-plans/execution/PLAN-32-EXECUTION.md`](../docs/acceptance-plans/execution/PLAN-32-EXECUTION.md).
Progress log with decisions K1–K25, maps, files and test runs: [`reports/plan32-progress.md`](plan32-progress.md); baseline:
[`reports/plan32-baseline.md`](plan32-baseline.md). KPI definitions: [`docs/STR_007_KPI_DEFINITIONS.md`](../docs/STR_007_KPI_DEFINITIONS.md).

## 1. Summary per phase

| Phase | Commit | Scope |
|-------|--------|-------|
| P0 | `79f7f4b` | Baseline (1008/1008 on the prompt filter; validators PASS), maps (decline paths, requisition units and scope, competition data, indexes, workflow history), pre-implementation block (decisions 1–10) in the plan, progress log |
| P1 | `11419e2` | Twelve `recruitment_*` KPIs in the STR-007 doc and matrix (targets TBD, frozen null baselines for the time KPIs; `hire_cycle_time` unchanged); pure calculators (`KpiStatistics`, `FunnelReachRules`, `TimeToFillRules`, `ApprovalSlaRules`, `SmallCellSuppression`); `OfferLetter.DeclineReasonCategory` (migration `HCM032_OfferDeclineCategory`), required on the HR and portal decline forms, `Other` for withdrawal-driven declines |
| P2 | `e54498e` | `RecruitmentMetricsService` (read-only, `AsNoTracking`, tenant-filtered, org-scoped; one method per KPI with n, coverage and fallback counts; requisition table and detail; batched event loads; memoised cohort/hires per request); `Recruitment:Analytics:*` options; hand-computed fixture tests (AC-HCM-32a) and scope tests |
| P3 | `ca801a3` | «تقارير التوظيف» (`RecruitmentReports/Index`, `Requisition/{id}`): KPI cards, funnel chart + data table, requisitions, overdue approvals, offers/declines, channels, appeals/objections, merit overrides, open vacancies, competitions (unrestricted only); CSP-safe chart script; menu entry; names only for HrCoreWrite |
| P4 | `117f7c9` | Excel (seven sheets + competitions, provenance block), PDF (official RTL layout), applications CSV (sync ≤ 10,000 rows, else export job `hrcore.recruitment.applications.csv` with scope and names flag captured and re-checked at download); GET actions, `BulkExport` audit, `ExportQueryLimits`; `QUERY_EXPORT_INVENTORY.md` regenerated |
| P5 | `d9ea351` | PostgreSQL scale test (50,000 applications, 250,000 events, 2,000 requisitions, 20,000 offers, 5,333 workflow instances) with SQL capture and `EXPLAIN ANALYZE`; migration `HCM032_AnalyticsIndexes` (two indexes); no snapshot needed |
| P6 | `a3188fe` | `IRecruitmentRequisitionClosed` v1 (aggregates only) staged on the HR.Core outbox by every close path; Dev.Analytics `RecruitmentRequisitionFact` (migration `ANA032_RecruitmentRequisitionFacts`, RLS), idempotent consumer, «التوظيف» card with small-cell suppression; semantic-layer registration (ANA-001, PROD-006); REL-004 inventory and message matrix |
| P7 | `608652e`, `5d80b38` | Merge of `main`; acceptance fixtures (`AcceptanceRecruitmentReportsPatch`), `AcHcm32RecruitmentReportsTests` run live 4/4; fix of the approval-SLA definition lookup under a tenant context (K24); regression runs |
| P8 | this commit | Validate script, execution guide, plan status/notes/deviations, ANA-001 / REP-001 / HCM-001 / gap study / schema inventory, independent review and fixes, this report |

## 2. Commits

```
(P8)    docs(hcm032): plan status, KPI definitions, execution guide, validate script, final report
5d80b38 test(hcm032): acceptance AC-HCM-32a..d and regression
608652e Merge branch 'main' into feature/plan-32-recruitment-analytics (AC-HCM-31 live-run fixes)
a3188fe feat(ana032): recruitment requisition aggregates to Dev.Analytics and semantic-layer registration
d9ea351 perf(hcm032): analytics indexes and scale test
117f7c9 feat(hcm032): recruitment report exports (Excel, PDF, scoped CSV job) without national id
ca801a3 feat(hcm032): recruitment reports screen with funnel, requisitions and overdue approvals
e54498e feat(hcm032): recruitment metrics service with org scope and coverage
11419e2 feat(hcm032): recruitment KPI definitions, calculators and offer decline categories
79f7f4b docs(hcm032): baseline, pre-implementation notes and progress log
```

## 3. Migrations

All additive; none rewrites or deletes business data.

| Migration | Service | Change |
|-----------|---------|--------|
| `20261029111828_HCM032_OfferDeclineCategory` | HR.Core | nullable `OfferLetters.DeclineReasonCategory` varchar(20) |
| `20261029122217_HCM032_AnalyticsIndexes` | HR.Core | `IX_RecruitmentApplications_TenantId_AppliedAt`, `IX_RecruitmentApplicationEvents_TenantId_EventType_ToStatus_Oc~` |
| `20261029123218_ANA032_RecruitmentRequisitionFacts` | Dev.Analytics | table `RecruitmentRequisitionFacts` (unique `(TenantId, RequisitionId)`, `(TenantId, ClosedAt)`), idempotent RLS hardening |

`has-pending-model-changes`: none for HR.Core and Dev.Analytics.

## 4. KPIs

Common rules: period `[From, To)` in UTC dates (default the last 12 full months); days = calendar days between UTC dates;
median of an even count = mean of the two middle values (one decimal); P90 = nearest rank `ceil(0.9 n)`; exact counts;
every KPI shows `n`. **Coverage** = share of the rows computed without an evidence fallback (time KPIs) or with an event
history (funnel, stage time); the fallbacks are counted separately.

| id | Definition (summary) | Coverage behaviour |
|----|----------------------|--------------------|
| `recruitment_funnel` | Cohort = applications applied in the period; per stage the count that reached it (events, or `ScreenedAt` / `RankedAt` / offer dates / hire evidence for older rows; the current status implies earlier stages, terminal statuses imply nothing; monotone) + current-status distribution | % of the cohort with an event history |
| `recruitment_time_to_fill` | Requisitions whose first hire is in the period: days from `ApprovedDate` (fallback `RequestedDate`) to the Hired event (fallback hired employee `CreatedAt`); median, P90 | rows without any fallback; start and hired fallbacks counted |
| `recruitment_time_to_hire` | Applications hired in the period: `AppliedAt` → Hired time | rows whose hire time is an event |
| `recruitment_stage_time` | Median days per status between consecutive status events (`Received`, `StatusChanged`) | event-covered rows only |
| `recruitment_offer_acceptance_rate` | Final outcome in the period: Accepted ÷ (Accepted + Declined + Expired); revoked excluded and shown | row-based (no events needed) |
| `recruitment_offer_decline_reasons` | Declines in the period by category («غير مصنف» before PLAN-32) and by decliner | row-based |
| `recruitment_channel_mix` | Cohort by `IntakeChannel` with hire rate | as the funnel |
| `recruitment_appeal_rate` | Rejection appeals filed ÷ appealable rejections in the period; outcomes decided in the period; objections filed/upheld/dismissed | row-based |
| `recruitment_merit_overrides` | Offers created in the period with merit override / any exception flag ÷ offers created | row-based |
| `recruitment_open_vacancies` | Today: vacant positions with no open requisition (and none on an open competition line); requisitions published > 60 days; oldest first | as of today |
| `recruitment_approval_sla` | In-progress recruitment instances whose current step age > SLA (step `TimeoutHours`, else per-workflow option, else 48 h policy value); median/P90 hours of steps completed in the period | bounded instance set reported (`InstancesExamined`) |
| `recruitment_competitions` | Unrestricted only: applicants, admitted, present, passed (current published version), allocated, reserve, recalled, objections filed/upheld | row-based |

## 5. Options per environment

`Recruitment:Analytics:*` — the code defaults apply in every environment (no value is set in Development, Acceptance, Staging
or Production configuration):

| Key | Default | Note |
|-----|---------|------|
| `StalePublishedDays` | 60 | stale published requisitions |
| `DefaultApprovalSlaHours` | 48 | organizational policy value, provisional, not legal |
| `ApprovalSlaHours:{WorkflowCode}` | — | per workflow; a step's `TimeoutHours` wins |
| `MinCellSize` | 5 | small-cell threshold for HR.Core breakdowns by a personal attribute (none today; K15) — Dev.Analytics uses the STR-007 value 5 |
| `DefaultPeriodMonths` | 12 | default report period |

## 6. Test results vs baseline

| Run | Result |
|-----|--------|
| Baseline (P0), prompt filter `Hcm02|Hcm030|Hcm031|Reports|Str007|Ana001|Prod006|Perf004` | 1008 / 1008 |
| `Hcm032*` unit tests (after the review fixes, theory cases counted) | **110 / 110** |
| Full unit suite (P7) | 3959 / 3976 — the 17 failures are environment-only: the 10 baseline ones (ar-SA/Hijri, `Ten002` offenders `BenefitPlanSeeder` and `AcceptanceCandidateExperiencePatch` — not PLAN-32, `Bola`, `San001`) and 7 `*_validate_script_passes` blocked by the execution policy (each passes with `-ExecutionPolicy Bypass`) |
| Integration `Hcm02|Hcm03|PublicCareers` (PostgreSQL 16) | 33 pass, 2 opt-in skips, 1 fail `Hcm028AppealGovernanceMigrationTests` (Hijri culture, baseline) |
| Scale `Hcm032Scale` | PASS (section 7) |
| Validators | STR-007 (22 KPIs), ANA-001, PROD-006, REL-004, `hcm006-a11y-scan` (12 views), `pdf-arabic-validate` — PASS; `scripts/hcm032-validate.ps1` — PASS |
| EF model | `has-pending-model-changes`: none (HR.Core, Dev.Analytics) |

**Modified existing tests** (one line each):

| Test | Why |
|------|-----|
| `Hcm027OfferOutcomeTests`, `Hcm027ReserveListTests`, `Hcm031ReserveRecallTests` | decline calls pass the new required category |
| `Hcm030OnBehalfAcceptanceTests` | same for the HR on-behalf decline; two helpers made `internal` for reuse |
| `Hcm030CandidatePortalTests` | portal decline takes the category and the new `CandidatePortalDeclineForm` (also added to the no-Guid form check); two helpers made `internal` |
| `Hcm029CandidateAcceptedV4Tests` (2) | a hire that fills the last vacancy now also publishes the requisition-closed aggregate |
| `AcHcm27OfferMeritTests`, `AcHcm30CandidateExperienceTests`, `AcHcm31CompetitionTests` | decline form posts carry the required category field |

## 7. Scale and snapshot decision

PostgreSQL 16 (Testcontainers), 50,000 applications / 250,000 events / 2,000 requisitions / 20,000 offers / 5,333 workflow
instances / 20 units, seeding 35 s. The 13 metric calls of `Index` (fresh service, after one warm-up run):

| Run | Unrestricted, 12 months | One branch |
|-----|------------------------|------------|
| Before indexes | 1,219 ms (cold 2,148) | 275 ms (cold 344) |
| After `HCM032_AnalyticsIndexes` | **1,225 ms** (budget 3,000) | **299 ms** (budget 1,500) |

`EXPLAIN ANALYZE` of every captured statement (`artifacts/hcm032-perf/plans*.txt`): the hires sub-plan scanned the events
sequentially (164 → 116 ms with the new events index, now a hashed sub-plan on it); the cohort scan stays sequential in
the single-tenant scale database because 88 % of rows match (correct plan; the `(TenantId, AppliedAt)` index serves
multi-tenant tables). **Snapshot table: not needed** — the budget is met with exact live queries.

## 8. Acceptance

**Run live, 4 / 4 Pass** on `hrm-acceptance` after rebuilding `hr-core-web` and `dev-analytics-web` from this branch (no
Keycloak or gateway change; the HCM-032 migrations applied at startup and the fixtures were seeded). Evidence:
`acceptance-evidence/AC-HCM-32/`.

| ID | Result |
|----|--------|
| 32a | REQ-2026-A-AC32-1: funnel 6,4,3,3,3,3,2,1,1 and time to fill 37 days (n = 1) on the requisition page and the filtered report |
| 32b | br1-mgr sees REQ-2026-A-AC32-1, not the Aleppo REQ-2026-A-AC32-2 or the competitions panel; filtering the Aleppo unit shows 0 applications and no AC32 requisition; the Aleppo requisition page answers 404 |
| 32c | central-a's Excel has no AC-HCM-32 national id, no 9–11 digit run and no masked id; a-executive-reader's CSV has no `CandidateName` column and no name; central-a's CSV has the names |
| 32d | REQ-2026-A-AC32-3's approval (pending since 2026-09-01 09:00 UTC) is in «الاعتمادات المتأخرة» with its link |

The first live run passed 3 / 4: the test's xlsx reader did not accept the `<x:t>` namespace prefix (test fix only).
After the review fixes `hr-core-web` was rebuilt and AC-HCM-32 re-run: **4 / 4 Pass** again (the committed evidence is from this run).
Regression live: AC-HCM-27 and AC-HCM-30 (both decline forms) — 10 pass, AC-30c skipped (File.Storage absent), `Ac27_f_g`
fails at the HR paper acceptance because the acceptance evidence upload needs File.Storage, which the stack does not run
(environment, accept path untouched by PLAN-32).

## 9. Decisions and deviations

Prompt decisions 1–10 are adopted as written (Arabic block at the top of the plan). Judgement calls K1–K25 are in the
progress log; the ones that change visible behaviour:

- **K2** funnel reach is monotone; `ReserveListed` counts as Ranked.
- **K3** an out-of-scope org-unit filter returns no data (not 403); a denied scope is 403 on the screen.
- **K8** the org-unit filter covers the unit and its descendants.
- **K10/K11** offer outcome dates (`ExpiredAt ?? ExpiryDate`), open-vacancy "vacant since".
- **K14** exports are GET actions (gateway POST rule), audited and capped.
- **K15** no small-cell suppression on the HR.Core screen (no personal-attribute breakdown); suppression in Dev.Analytics.
- **K17** export job filter carries scope + names flag; download re-checks HrCoreWrite and lost unrestricted scope.
- **K20** two indexes; no snapshot.
- **K21/K22** aggregate close event staged before the save of every close path; fact upsert per requisition.
- **K24** approval SLA reads workflow definitions of the default tenant through the sanctioned system bypass (bug fix).
- Deviation from the plan: time to fill starts at approval (decision 2); channel mix from `IntakeChannel` (decision 3).

### Independent review

A fresh read-only reviewer (the prompt, the plan and `git diff main...HEAD`; correctness only) reported six findings. Each was
then attacked by a refuter agent; three held and were fixed, each with a regression test:

| # | Severity | Finding | Refutation | Outcome |
|---|----------|---------|------------|---------|
| F1 | medium | Appeal-rate denominator read `RejectedAt`/`RejectionKind`, which an upheld appeal clears (`ClearRejection`) — 4 rejected, 4 appealed, 2 upheld gave 200 %; the refuter added a second undercount: rejections without a kind are appealable as `SelectionDecision` but were excluded | **Confirmed** (fixture seeded an impossible state, so the test missed it) | **Fixed**: denominator = rejected in the period with `RejectionKind ?? SelectionDecision` appealable, **or** a Rejection appeal upheld whose last `StatusChanged → Rejected` event before filing is in the period. Fixture made realistic (B4 cleared, A3 without a kind; hand tables updated: statuses, a 13-day Rejected stage). Test `Appeal_rate_counts_rejections_cleared_by_an_upheld_appeal` drives the real `RecruitmentAppealWorkflowHandler` (4 / 4 = 100 %) |
| F2 | low | Stale-published cut-off compared instants while the list shows calendar days (60 d 4 h → listed with "60") | **Confirmed** (both directions) | **Fixed**: cut-off on UTC dates; test `Stale_published_boundary_is_in_calendar_days` (fails on the old rule) |
| F3 | low | Open vacancies hide positions whose competition-linked requisition is closed/cancelled | **Refuted** — attaching only in Draft for Approved requisitions; close/cancel blocked while the competition holds them | no change |
| F4 | low | Competitions panel: recalled allocations not under "allocated"; unit filter shows whole-competition counts | **Refuted** (documented K13; spec-silent; unrestricted only) | no change; unit-filtered allocated/recalled noted as an option |
| F5 | low | Export provenance named an out-of-scope unit by id (the lookup API refuses that) | **Confirmed** | **Fixed**: named only when `scope.CanAccess`, else «خارج النطاق»; test `Out_of_scope_unit_is_not_named_in_the_export_header` |
| F6 | uncertain | Result-objection counts and appeal-review SLA reach branch users | **Refuted** — objections only count once the application is allocated into the unit (decision 6 clause 3); pending ones have no unit | no change |

No PII, culture or frozen-definition finding. The KPI documents now state the two appeal-rate rules and that the rate can
exceed 100 % when appeals of the period concern rejections of an earlier period.

## 10. Follow-ups

- **D10 / PLAN-25:** `RecruitmentApplicationsController` and `RecruitmentRequisitionsController` do not apply org scope
  (unlike `RecruitmentAppealsController` and `ReportsController`); the report drill-through links reach them with their own
  authorization. To be scoped in PLAN-25.
- **Fairness metrics** (transition rates by gender / age band, aggregated, k ≥ 10): deferred — no GOV-001 decision and the
  candidate's gender is not collected.
- **KPI targets:** TBD; product-hr sets them after the first frozen quarter; the null baselines of the time KPIs are then
  measured.
- **Snapshot** `RecruitmentDailySnapshot`: not built (budget met); revisit if the tenant data grows an order of magnitude.
- **Environment:** File.Storage (MinIO image) is still missing from the acceptance stack (AC-HCM-27f/g, AC-30c);
  `docs/DATABASE_SCHEMA_INVENTORY.*` should be regenerated with `scripts/extract_database_inventory.py` when Python is available
  (edited by hand).
- Merge: this branch is to be merged into local `main` (user rule) — see section 12.

## 11. Manual test script (Arabic UI)

1. `central-a` ← «نواة الموارد البشرية» ← «تقارير التوظيف». الفترة الافتراضية آخر 12 شهراً كاملاً؛ البطاقات تعرض n والتغطية.
2. «من تاريخ» 2026-08-01، «حتى تاريخ» 2026-10-01 ← «تطبيق». في «طلبات الشاغر» افتح `REQ-2026-A-AC32-1`: «القمع» 6، 4، 3، 3، 3، 3، 2،
   1، 1 و«زمن الملء 37»؛ «سجل العروض» يعرض أسماء المرشحين.
3. «الاعتمادات المتأخرة»: `REQ-2026-A-AC32-3` (اعتماد طلب شاغر، الخطوة 1، المهلة 48 «السياسة الافتراضية»)؛ الرابط يفتح طلب الشاغر.
4. «Excel» / «PDF» / «تفاصيل الطلبات (CSV)»: لا رقم وطني؛ ورقة كل ملف تبدأ بالفترة والنطاق ووقت الإنشاء وإصدار التعريفات والمصدر.
5. `a-executive-reader`: صفحة طلب الشاغر تعرض «مرجع الطلب» بدل الاسم، وCSV بلا عمود الاسم.
6. `br1-mgr`: لا يظهر `REQ-2026-A-AC32-2` ولا «المسابقات»؛ `?OrgUnitId=a0000001-0000-4000-8000-000000000104` ← «لا بيانات»؛
   `RecruitmentReports/Requisition/a0000001-0000-4000-8000-000000000802` ← 404.
7. «خطابات العروض» ← عرض صادر ← «رفض العرض»: «فئة سبب الرفض» إلزامية؛ `/careers/my` ← «الاعتذار عن العرض»: «سبب الاعتذار الرئيسي» إلزامي.
8. Dev.Analytics ← «لوحات ومؤشرات» ← بطاقة «التوظيف»: الأعداد من 1 إلى 4 تظهر «<5».

## 12. Repository hygiene

Nothing pushed. Never committed: `global.json`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`,
`tmp-vendors.html`, credentials, test-rewritten `testdata/**/out/*.json`, `artifacts/` (git-ignored). The AC-HCM-27/30
evidence rewritten by the regression runs was restored, not committed. Commits use explicit paths only (another session
worked in this checkout early on, K-open issues).
