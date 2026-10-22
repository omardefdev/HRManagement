# HCM-001 — Recruitment, Selection, and Candidate Experience

## Purpose

Model the full recruitment lifecycle from requisition through offer acceptance, ensuring every vacancy is linked to an approved funded position, assessments are versioned and auditable, and conversion to employee is idempotent.

## Scope

- Requisition from funded position (EST-001)
- Public announcement and application case
- Screening, assessments, interviews, and committees
- Conflict of interest declaration and audit
- Ranking and offer letter
- Appeal workflow
- Privacy/retention of candidate data
- Idempotent conversion to employee record

## Domain Model

### RecruitmentRequisition Entity

Represents a staffing request linked to a funded position.

```
RecruitmentRequisition
├── Id (Guid)
├── TenantId (Guid)
├── RequisitionNumber (string) — unique identifier
├── PositionId (Guid) — FK to FundedPosition (EST-001)
├── JobTitle (string)
├── Status (enum) — Draft, PendingApproval, Approved, Published, Closed, Cancelled
├── RequestedDate (DateTime)
├── ApprovedDate (DateTime?)
├── ClosedDate (DateTime?)
├── JustificationNotes (string?)
├── VacancyCount (int) — number of vacancies
├── LegalSourceId (string?) — law/decree reference
├── CreatedAt / UpdatedAt
```

### CandidateApplication Entity

Tracks a candidate's application for a published requisition.

```
CandidateApplication
├── Id (Guid)
├── TenantId (Guid)
├── RequisitionId (Guid) — FK to RecruitmentRequisition
├── CandidateName (string)
├── NationalId (string?) — civil ID for identity verification
├── Email (string?)
├── Phone (string?)
├── Status (enum) — Submitted, UnderScreening, Shortlisted, InterviewScheduled, Assessed, Ranked, OfferExtended, Accepted, Rejected, Withdrawn
├── SubmittedAt (DateTime)
├── ScreenedAt (DateTime?)
├── AssessmentScore (decimal?)
├── Rank (int?)
├── Notes (string?)
├── ConflictOfInterestDeclared (bool)
├── CreatedAt / UpdatedAt
```

### OfferLetter Entity

An official offer issued to a ranked candidate.

```
OfferLetter
├── Id (Guid)
├── TenantId (Guid)
├── ApplicationId (Guid) — FK to CandidateApplication
├── PositionId (Guid) — FK to FundedPosition
├── OfferNumber (string) — unique offer reference
├── Status (enum) — Draft, PendingApproval, Issued, Accepted, Declined, Expired, Revoked
├── IssuedDate (DateTime)
├── ExpiryDate (DateTime)
├── AcceptedDate (DateTime?)
├── OfferedSalary (decimal)
├── GradeCode (string?) — MDM-001 grade reference
├── ProposedStartDate (DateTime)
├── LegalSourceId (string?)
├── IsConvertedToEmployee (bool) — idempotent conversion flag
├── IsMeritOverride / MeritOverrideReason — PLAN-27 merit exception
├── ValidityDays (int) — PLAN-27; ExpiryDate = IssuedDate + ValidityDays (end of UTC day)
├── DeclineReason / DeclinedAt / DeclinedBy, RevokeReason / RevokedAt, ExpiredAt — PLAN-27 outcomes
├── GradeDeviationReason, SalaryUnverified, SalaryJustification — PLAN-27 exception terms
├── GradeMinSalary / GradeMaxSalary / SalaryCheckedAt — PLAN-27 Payroll bounds snapshot
├── CreatedAt / UpdatedAt
```

## Runtime Status (Plans 1–5)

| Plan | Capability | Status |
|------|------------|--------|
| HCM-01 | Requisition + FundedPosition | Done |
| HCM-02 | Publish lifecycle + posting bridge | Done |
| HCM-03 | Unified intake (RequisitionId) | Done |
| HCM-04 | Selection + offer pipeline | Done |
| HCM-05 | Hire closure + saga lineage + legacy migration | Done |
| HCM-06 | Privacy + committee + appeal + candidate UX + portal security | Done |
| HCM-07 | Internal channel (TalentMarketplace + EssTalent + PromoteFromInternal) | Done |

Plan-7 delivers internal vacancy promotion: HR manages internal postings via `TalentMarketplaceController`; employees self-apply via `EssTalentController` (`ModuleEss`); promoted applications enter the unified selection pipeline.

**Gateway journey:** `/Home/JourneyDetails/new-hire` — phase 1 lists recruitment steps (including optional internal channel); phase 2 lists FUN-001 onboarding modules.

Plan-5 delivers `RecruitmentHireClosureService`: atomic hire with `EstablishmentService.AssignEmployee`, requisition close (`VacancyCount` decrement, `Closed` when exhausted, posting unpublished), idempotent `OfferLetter.IsConvertedToEmployee`, sibling rejection, `ICandidateAccepted` v3 lineage, and FUN-001 saga wiring via `RecruitmentApplicationId`.

## PLAN-26 — Assessment, interview and requisition integrity (HCM-D)

Addresses gaps **G-03** (assessment criteria), **G-08** (interview lifecycle) and **G-11** (requisition integrity), plus the
closing-date part of **G-20**. Plan: [PLAN-26](acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md);
execution: [PLAN-26-EXECUTION](acceptance-plans/execution/PLAN-26-EXECUTION.md). Settings live in the `Recruitment` section
(`RecruitmentOptions`).

### Assessment criteria versions

- `RecruitmentAssessmentCriteria` holds one immutable version per change; `CriteriaJson` is a list of
  `AssessmentCriteriaItem(Key, NameAr, Weight, MaxScore, Stage, IsMandatory, Description)` with
  `Stage ∈ {Screening, Exam, Interview, Experience, Other}`. The legacy seed format `[{"name","weight"}]` is still read
  (`item-n`, max 100, stage Other).
- Validation (`AssessmentCriteriaRules`): at least one item, unique keys (≤ 64), Arabic name (≤ 200), weight > 0,
  `MaxScore` > 0, weights sum to 100 (± 0.01).
- Exactly one **active** version per requisition (filtered unique index). A version is **locked** by its first submitted
  score; replacing a locked version needs `HrCoreApprove`, a reason (≥ 10 chars) and writes an audit row. Earlier scores
  stay on their own version.
- **Publish gate:** `Publish`/`PublishInternal` require an active version (`RequireCriteriaBeforePublish`, default true).

### Scoring formula

- Each assessor submits per-item scores (`RecruitmentAssessmentScore`, unique per application/assessor/item) with a
  mandatory no-conflict declaration. Rejected: score outside `0..MaxScore`, unknown item, applicant scoring themself,
  committee member with a declared conflict, status other than `InterviewScheduled`/`Assessed`.
- Assessor total = Σ (score ÷ MaxScore × Weight). An assessor is **complete** when all mandatory items are scored.
- `AssessmentScore` = mean of complete assessors, rounded to 2 decimals (AwayFromZero) — computed, never typed.
- `InterviewScheduled → Assessed` happens **only** automatically, once complete assessors ≥ `MinAssessors` (default 1) and
  the interview gate holds. Manual transition to `Assessed` is rejected and not offered.

### Interview lifecycle

- Slot states: `Scheduled`, `Completed`, `Cancelled`, `Rescheduled`; mode `InPerson`/`Remote`; outcome `Attended`/`NoShow`;
  panel members (first = chair).
- Schedule: future time, application `Shortlisted` or `InterviewScheduled` only, one `Scheduled` slot at a time, panel of
  existing employees excluding the applicant. Scheduling never moves `Assessed`/`Ranked` back.
- Reschedule (reason ≥ 5, chain limited by `MaxInterviewReschedules`, default 1), cancel (reason; status unchanged),
  complete (only once due; `NoShow` keeps the status).
- **Interview gate:** if the criteria contain Interview-stage items, those items can be scored — and `Assessed` reached —
  only after an interview completed as `Attended`. `/careers` shows `Scheduled` slots only.

### Requisition integrity (cancel / close / freeze cascade)

- **Blocking statuses** `Draft, PendingApproval, Approved, Published`: one blocking requisition per funded position —
  service check plus `IX_RecruitmentRequisitions_Tenant_Position_Blocking`. Before migrating an existing database run
  `scripts/hcm026-find-duplicate-requisitions.ps1`.
- **Cancel:** Draft/PendingApproval with `HrCoreWrite` (running approval workflow cancelled); Approved/Published need
  `HrCoreApprove` and a reason (≥ 10). Refused when a candidate is `Hired`/`OfferAccepted` or an offer is `Accepted`.
  Cascade (one save): posting unpublished, internal vacancy closed, Draft/PendingApproval/Issued offers revoked (with
  their workflows), open applications rejected («إلغاء الشاغر: …»), scheduled interviews cancelled, audit row.
- **Close:** refused while an offer is Draft/PendingApproval/Issued or accepted-but-not-converted; otherwise open
  applications are rejected («إغلاق الشاغر») and scheduled interviews cancelled.
- **Freeze:** applying a `FreezeFundedPosition` change cancels the position's blocking requisition with reason
  «تجميد المنصب: …», or fails while an offer is `Issued` / accepted-but-not-converted.

### Intake window

- `ApplicationClosingDate` is optional in Draft and required at first publish (≥ today + `MinPostingDays`, default 7);
  it is copied to the public posting and internal vacancy.
- `ExtendClosingDate`: later date only, reason ≥ 10, audited; reopens intake if it had been closed.
- `RecruitmentIntakeCloseJob` (`Recruitment:Jobs:IntakeCloseEnabled`, `IntakeCloseIntervalMinutes` = 60, advisory lock
  `8675312`) unpublishes expired postings, closes vacancies and sets `IntakeClosedAt`; the requisition stays `Published`
  so selection continues. Intake (HR, `/careers`, ESS) is rejected after the date or once intake is closed.

## PLAN-27 — Offer integrity, merit principle and reserve list (HCM-E)

Addresses gaps **G-06** (offer outcomes) and **G-07** (merit order), plus the offer-expiry part of **G-20**, and decision
**D-7** (reserve validity). Plan: [PLAN-27](acceptance-plans/PLAN-27-hcm-offer-merit-reserve.md); execution:
[PLAN-27-EXECUTION](acceptance-plans/execution/PLAN-27-EXECUTION.md).

### Application state machine (selection part)

```
Assessed ──rank──► Ranked ──offer draft──► OfferExtended ──accept──► OfferAccepted ──hire──► Hired
                     │  ▲                      │
                     │  └──revoke (admin error)┤
                     │                         ├──decline / expiry──► OfferDeclined (terminal)
                     │                         └──revoke──────────────► Rejected
                     └──sibling hired──► ReserveListed ──offer draft──► OfferExtended
                                              └──ReserveUntil passed / cancel / close──► Rejected
```

- New statuses (stored as int, appended): `OfferDeclined = 11` «عرض مرفوض/منتهٍ», `ReserveListed = 12` «قائمة احتياط».
  Both are **system-only** targets (never offered as manual transitions).
- Terminal statuses: `Hired`, `Rejected`, `Withdrawn`, `OfferDeclined` (`SelectionWorkflowRules.TerminalStatuses`).

### Offer state machine

```
Draft ──submit──► PendingApproval ──approved──► Issued ──accept──► Accepted (→ hire converts it)
  │                    │                          ├──decline──► Declined
  └──revoke──┬─────────┘                          ├──ExpiryDate passed (job)──► Expired
             ▼                                    └──revoke──► Revoked
          Revoked (running approval workflow cancelled)
```

`ExpiryDate` is set when the offer is **issued** (`IssuedDate + ValidityDays`, `Recruitment:OfferValidityDays` = 14,
accepted range 3..60). An expired Issued offer cannot be accepted.

### Merit rule

- Only `Ranked` or `ReserveListed` applications can receive an offer; no pending appeal; one offer occupying the requisition
  at a time (Draft / PendingApproval / Issued / Accepted-not-converted).
- **Best available rank** = lowest `Rank` among the requisition's `Ranked`/`ReserveListed` applications without a pending
  appeal. Offering to anyone else is a **merit override**: `IsMeritOverride` + `MeritOverrideReason` (≥ 30 chars).

### Exception offers

`OfferLetter.IsExceptionOffer` = merit override **or** grade deviation **or** unverified salary. Exception offers use
`RECRUITMENT-OFFER-OVERRIDE-APPROVAL` (3 steps: «مراجعة HR» hr-core → «اعتماد إداري» hr-management-approver →
«اعتماد استثناء الجدارة» hr-management-approver) with **distinct approvers**: an actor who approved an earlier step of the
instance is refused. Selection: policy rules `R-OFF-EXCEPTION` (priority 8, `isExceptionOffer`), `R-OFF-OVERRIDE`
(9, `isMeritOverride`), `R-OFF-ISSUE` (10); the static fallback and the lab path mirror them.
Step 3 moves to `compliance` once PLAN-25 is implemented.

### Offer terms (grade, salary, dates)

- `GradeCode` is pre-filled from the funded position; a different grade needs `GradeDeviationReason` (≥ 10 chars).
- Salary bounds come from Payroll through the S2S read `GET api/internal/pay-grades/{gradeCode}?asOf=` (policy
  `Service.PayrollGradeLookup`, scope `payroll.grades.read`, client `svc-hr-core`; tenant via `X-Tenant-ID`), using the
  **active salary scale effective at the proposed start date**:
  - found and within `[Min, Max]` → OK; bounds and check time are stored on the offer;
  - found and outside → «الراتب المعروض خارج حدود الدرجة {grade}: {min}–{max}.»;
  - not found → «الدرجة {grade} غير معرّفة في سلّم الرواتب الساري.»;
  - Payroll unavailable (not configured, timeout 5 s, transport error, non-404 failure) → allowed only with
    `SalaryJustification` (≥ 20 chars); the offer is flagged `SalaryUnverified` (exception offer).
- `ProposedStartDate` between today (UTC) and today + `Recruitment:MaxStartDays` (90).

### Decline, revoke and expiry

| Operation | Offer | Application |
|-----------|-------|-------------|
| Decline (HR on the candidate's behalf; `Candidate` reserved for PLAN-30), reason ≥ 5 | `Declined` | `OfferDeclined` |
| Revoke, reason ≥ 10, administrative error | `Revoked` (workflow cancelled) | back to `Ranked` |
| Revoke, other reason | `Revoked` | `Rejected` |
| `RecruitmentOfferExpiryJob` (hourly, lock 8675313) | `Expired` | `OfferDeclined` «انتهاء صلاحية العرض» |

After a decline/expiry/revoke on a still `Published` requisition, `RecruitmentRequisitions/Details` and
`OfferLetters/Details` suggest «المرشح التالي بالجدارة: {name} (رتبة n) — إنشاء عرض». With
`Recruitment:AutoDraftNextOffer=true` the next offer is drafted automatically (same grade/salary/validity and the same
eligibility and terms checks; from the expiry job it runs in the expired offer's tenant).

### Reserve list (D-7)

- On hire (`RejectSiblingApplicationsOnHire=true`), `Ranked` siblings become `ReserveListed` with
  `ReserveUntil = today + Recruitment:ReserveValidityMonths` (6) and `ReserveListedAt`; other open siblings are `Rejected`;
  siblings already on the reserve list keep their date. With the option `false` nothing changes.
- `RecruitmentReserveExpiryJob` (daily, `Recruitment:Jobs:ReserveExpiryEnabled` / `ReserveExpiryIntervalMinutes` = 1440,
  lock 8675314) rejects `ReserveListed` applications whose `ReserveUntil` has passed («انتهاء صلاحية قائمة الاحتياط»).
- Reserve status and «حتى {date}» are shown on application details, the application list and the rank board.
- Recall from the reserve on non-commencement is PLAN-29.

## Acceptance Criteria Mapping

- [x] No announcement without funded position — `PositionId` required on requisition
- [x] Assessment criteria versioned and auditable — score/rank tracked per application
- [x] Committee and conflicts audited — `ConflictOfInterestDeclared` field
- [x] Candidate tracks case — status progression with timestamps
- [x] Conversion idempotent — `IsConvertedToEmployee` flag prevents re-entry
- [x] Hire closes funded position and requisition — Plan-5 closure service
- [x] Saga carries recruitment lineage — `ICandidateAccepted` v3 + `StartHireSagaAsync`
- [x] Legacy applications migration script — `scripts/hcm001-migrate-legacy-applications.ps1`
- [x] Plan-6 privacy/retention — `NationalIdHash`, `CandidateApplicationLegalLifecycleService`, DSAR candidate slice
- [x] Plan-6 committee + appeal — `SelectionCommitteeSession`, `RecruitmentAppeal`, rank/offer gates
- [x] Plan-6 portal UX — `/careers` timeline, interview slots, rate limit, consent, a11y baseline
- [x] PLAN-26 G-03 — versioned, validated, lockable assessment criteria; publish requires an active version
- [x] PLAN-26 G-08 — interview lifecycle (schedule/reschedule/cancel/complete, panel, interview gate)
- [x] PLAN-26 G-11 — one blocking requisition per position (service + DB index); cancel/close/freeze cascades
- [x] PLAN-26 G-20 (part) — application closing date, extension, automatic intake close
- [x] PLAN-27 G-07 — offers only to ranked candidates in merit order; documented exceptions approved by three distinct approvers
- [x] PLAN-27 G-06 — decline/revoke/expiry settle the application; automatic offer expiry; next candidate by merit suggested
- [x] PLAN-27 G-20 (part) — offer validity and expiry job
- [x] PLAN-27 offer terms — grade from the funded position, salary within the Payroll grade bounds (or an unverified exception)
- [x] PLAN-27 D-7 — ranked siblings kept on a reserve list that expires automatically

## Plan map (HCM-001)

| Plan | Coverage |
|------|----------|
| 1 | Requisition + FundedPosition |
| 2 | Lifecycle + posting |
| 3 | Unified intake |
| 4 | Selection + offer |
| 5 | Hire closure + saga |
| 6 | Privacy + committee + appeal + candidate UX + portal security |
| 7 | Internal channel (TalentMarketplace, EssTalent, PromoteFromInternal) |
| PLAN-26 | Assessment criteria, structured scoring, interview lifecycle, requisition integrity, intake window (G-03, G-08, G-11 addressed; G-20 partly) |
| PLAN-27 | Merit order, exception offers, offer terms (Payroll grade lookup), decline/revoke/expiry, reserve list (G-06, G-07 addressed; G-20 partly; D-7) |

## Artifacts

| File | Purpose |
|------|---------|
| `docs/HCM_001_RECRUITMENT.md` | This document |
| `SharedKernel/Recruitment/RecruitmentRequisition.cs` | Requisition domain entity |
| `SharedKernel/Recruitment/CandidateApplication.cs` | Application domain entity |
| `SharedKernel/Recruitment/OfferLetter.cs` | Offer letter domain entity |
| `scripts/hcm001-validate.ps1` | Validates HCM-001 artifacts |
| `scripts/hcm001-migrate-legacy-applications.ps1` | Backfill RequisitionId / flag legacy apps |
| `src/Services/HRCore/HR.Core.Domain/Services/CandidateApplicationLegalLifecycleService.cs` | Candidate anonymize/purge (Plan-6) |
| `scripts/hcm006-a11y-scan.ps1` | Public careers a11y baseline (Plan-6) |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm06CandidateDsarTests.cs` | Candidate DSAR acceptance (Plan-6) |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs` | Atomic hire closure |
| `Hcm001RecruitmentTests.cs` | Unit tests |
| `scripts/hcm026-validate.ps1` | Validates PLAN-26 artifacts, migrations and `Hcm026` tests |
| `scripts/hcm026-find-duplicate-requisitions.ps1` | Read-only duplicate check before `HCM026_RequisitionIntegrity` |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm26AssessmentInterviewTests.cs` | AC-HCM-26a..i acceptance |
| `scripts/hcm027-validate.ps1` | Validates PLAN-27 artifacts, migrations and `Hcm027` tests |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/ReserveListService.cs` | Reserve list, next candidate |
| `src/Services/Payroll/Payroll.Web/Controllers/PayGradeLookupInternalController.cs` | Payroll grade bounds (S2S) |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm27OfferMeritTests.cs` | AC-HCM-27a..h acceptance |

## Validation

- `scripts/hcm001-validate.ps1`
- `Hcm001RecruitmentTests`
- `scripts/hcm026-validate.ps1` — `Hcm026*` unit tests + `Hcm021|Hcm022` regression
- `scripts/hcm027-validate.ps1` — `Hcm027*` unit tests + `Hcm026|Hcm022` regression
