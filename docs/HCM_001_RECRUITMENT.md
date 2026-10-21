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

## Validation

- `scripts/hcm001-validate.ps1`
- `Hcm001RecruitmentTests`
- `scripts/hcm026-validate.ps1` — `Hcm026*` unit tests + `Hcm021|Hcm022` regression
