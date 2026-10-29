# STR-007 — KPI definitions (human view)

Machine source: [`str-value/kpi-matrix.json`](str-value/kpi-matrix.json).

Rule: every KPI has definition, source, owner ≠ auditor, target, anti-gaming, and baseline freeze when `baselineRequired`.

| id | unit | owner | auditor | target | modules |
|---|---|---|---|---|---|
| hire_cycle_time | days | product-hr | audit | ↓ 45 | hr-core, hr-contracts |
| separation_cycle_time | days | product-hr | audit | ↓ 10 | hr-movements, auth-directory |
| payroll_cycle_time | hours | product-payroll | audit | ↓ 48 | payroll |
| self_service_rate | % | product | audit | ↑ 40 | hr-core (PROD-003 stub) |
| data_error_duplicate_rate | /1000 | data | audit | ↓ 5 | hr-core (GOV-003) |
| user_satisfaction | score | product | audit | ↑ 4.0 | hr-core, payroll |
| uptime_value | % | sre | audit | ↑ 99.5 | OBS J1 |
| cost_per_employee | currency | finance | audit | TBD FIN-001 | — |
| agency_onboarding_time | days | program | audit | ↓ 90 | payroll, hr-core |
| adoption_rate | % | product | audit | ↑ 70 | hr-core, payroll, hr-contracts |
| recruitment_funnel | count | product-hr | audit | TBD | hr-core |
| recruitment_time_to_fill | days | product-hr | audit | ↓ TBD | hr-core |
| recruitment_time_to_hire | days | product-hr | audit | ↓ TBD | hr-core |
| recruitment_stage_time | days | product-hr | audit | ↓ TBD | hr-core |
| recruitment_offer_acceptance_rate | % | product-hr | audit | ↑ TBD | hr-core |
| recruitment_offer_decline_reasons | count | product-hr | audit | TBD | hr-core |
| recruitment_channel_mix | count | product-hr | audit | TBD | hr-core |
| recruitment_appeal_rate | % | product-hr | audit | TBD | hr-core |
| recruitment_merit_overrides | % | product-hr | audit | ↓ TBD | hr-core |
| recruitment_open_vacancies | count | product-hr | audit | ↓ TBD | hr-core |
| recruitment_approval_sla | hours | product-hr | audit | ↓ TBD | hr-core |
| recruitment_competitions | count | product-hr | audit | TBD | hr-core |

Forbidden as benefit measures: `screenCount`, `pageCount`, `widgetCount`.

## Recruitment KPIs (HCM-032 / PLAN-32)

Implemented by `HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentMetricsService` with the pure rules in
`HR.Core.Domain/Services/RecruitmentKpi/`; screen «تقارير التوظيف» (`RecruitmentReports/Index`). Definitions version
**`STR-007/2026-10-29`** — frozen before each quarter (`freeze-definition-before-quarter`). Targets are **TBD**: product-hr
sets them after the first frozen quarter; baselines of the time KPIs are frozen rows with no value yet (no number invented).

### General rules

- Period `[From, To)` in UTC dates; default the last 12 full months.
- "Days" = calendar days between the two UTC dates (`DateOnly` difference).
- **Median**: even n → mean of the two middle values; one decimal. **P90**: nearest rank — the value at position
  `ceil(0.9 × n)` in ascending order.
- Every KPI shows `n` and its **coverage** — the share of the cohort with an event history
  (`RecruitmentApplicationEvent`, recorded since 2026-10-23). Older rows use the documented evidence fallbacks below; no
  synthetic event is ever back-filled. Fallbacks are counted separately (`count-fallback-separately`).
- Exact counts inside the tenant (ANA-001): no sampling, no approximation.
- **Scope**: a requisition's org unit is its funded position's `OrgUnitId`; a scoped user (branch manager) sees only
  requisitions and applications of the units in scope; an org-unit filter outside scope returns no data. An allocated
  competition application counts under its allocated requisition's unit; competition aggregates are shown to
  unrestricted users only.
- **Privacy**: no national id (not even masked) in any report or export; candidate names only for `HrCoreWrite`; small
  cells (1–4, `privacyRules.minCellSize = 5`) shown «<5» in everything published outside HR.Core and in any breakdown by a
  personal attribute. Fairness metrics are deferred (no GOV-001 decision; candidate gender is not collected).

| id | الاسم | Name | Definition |
|----|-------|------|------------|
| `recruitment_funnel` | قمع التوظيف | Recruitment funnel | **Cohort** = applications with `AppliedAt` in the period (and the filters). For each stage — Applied → Screened → Shortlisted → Interviewed → Assessed → Ranked → OfferIssued → OfferAccepted → Hired — the count of cohort applications that **reached** it. Reached = an event shows the stage (`StatusChanged` to that status or beyond; `InterviewScheduled`; `OfferIssued`; `OfferAccepted`), or evidence on older rows: `ScreenedAt` → Screened; `RankedAt` → Ranked; an offer with `IssuedDate` → OfferIssued; `AcceptedDate` → OfferAccepted; `HiredEmployeeId` or status Hired → Hired. The current status implies every earlier stage, except the terminal Rejected / Withdrawn / OfferDeclined, which imply nothing. Reach is monotone (a stage implies all earlier stages). ReserveListed counts as Ranked. Also: the current-status distribution of the cohort. |
| `recruitment_time_to_fill` | زمن الملء | Time to fill | Requisitions **filled** in the period: the first application of the requisition to reach Hired, with the Hired time in the period. Days from `ApprovedDate` (fallback `RequestedDate`) to the Hired time. **Hired time** = the `StatusChanged → Hired` event; fallback the hired employee's `CreatedAt`. Median, P90, n, both fallback counts. Starts at the approved vacancy like the frozen `hire_cycle_time` (deviation from the plan's `RequestedDate`, PLAN-32 decision 2); `hire_cycle_time` is unchanged. |
| `recruitment_time_to_hire` | زمن التعيين | Time to hire | Applications hired in the period: days from `AppliedAt` to the Hired time. Median, P90, n, fallback count. |
| `recruitment_stage_time` | الزمن في كل مرحلة | Stage time | Median days spent in each application status, between consecutive status events (`Received`, `StatusChanged`) of cohort applications with events. Event-covered rows only. |
| `recruitment_offer_acceptance_rate` | نسبة قبول العروض | Offer acceptance rate | Offers whose **final outcome** happened in the period: Accepted ÷ (Accepted + Declined + Expired). Outcome dates: `AcceptedDate`, `DeclinedAt`, `ExpiredAt`. **Revoked offers are excluded** (`exclude-cancelled-offers`). Counts shown. |
| `recruitment_offer_decline_reasons` | أسباب رفض العروض | Offer decline reasons | Declines in the period by `DeclineReasonCategory` (الراتب، مكان العمل، عرض آخر، أسباب شخصية، طبيعة العمل، أخرى; rows before PLAN-32 «غير مصنف») and by `DeclinedBy` (Candidate / HrOnBehalf). Withdrawal-driven declines are «أخرى». Expired offers are an outcome, not a reason. |
| `recruitment_channel_mix` | مزيج القنوات | Channel mix | Cohort by `IntakeChannel` (Public / Internal / HrEntered / Legacy), with the hire rate per channel = cohort applications that reached Hired ÷ cohort applications of the channel. |
| `recruitment_appeal_rate` | معدل الاستئناف | Appeal rate | Appeals (`Kind = Rejection`) filed in the period ÷ applications rejected in the period (`RejectedAt`) with an appealable `RejectionKind` (`Recruitment:AppealableRejectionKinds`). Outcome distribution of appeals decided in the period (`DecidedAt`). Result objections (`ResultObjection`) separately: filed, upheld (`ObjectionUpheld`), dismissed (`ObjectionDismissed`). |
| `recruitment_merit_overrides` | تجاوزات الجدارة | Merit overrides | Offers created in the period (`CreatedAt`) with `IsMeritOverride`, and with any exception flag (`IsExceptionOffer` = merit override, grade deviation or unverified salary), ÷ offers created. |
| `recruitment_open_vacancies` | الشواغر المفتوحة | Open vacancies | As of today: `Vacant` funded positions with **no** requisition in Draft / PendingApproval / Approved / Published and no requisition attached to an open competition line (competition not Finalized / Cancelled); plus requisitions `Published` for more than `Recruitment:Analytics:StalePublishedDays` (60). Oldest first. |
| `recruitment_approval_sla` | مهلة الاعتماد | Approval SLA | Recruitment workflow instances (codes `RECRUITMENT-*`). **(a)** `InProgress` instances whose current step age > SLA. SLA = the step's `TimeoutHours`, else `Recruitment:Analytics:ApprovalSlaHours:{WorkflowCode}`, else `Recruitment:Analytics:DefaultApprovalSlaHours` (48) — an organizational policy value, provisional, not legal. Step start = `ActionAt` of the latest history entry, else `StartedAt`. **(b)** Steps completed (approved or rejected) in the period: median and P90 duration in hours per workflow code and step; skipped steps only move the next step's start. |
| `recruitment_competitions` | المسابقات | Competitions | Unrestricted users only. Per competition: applicants, admitted (reached Shortlisted), present (an exam paper with attendance Present), passed (outcome Passed in the current published result version), allocated, reserve, recalled, objections filed / upheld. |
