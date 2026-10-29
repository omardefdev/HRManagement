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

The runtime model lives in HR.Core: entities in `src/Services/HRCore/HR.Core.Domain/Entities`, enums in
`src/Services/HRCore/HR.Core.Domain/Enums`, rules in `src/Services/HRCore/HR.Core.Domain/Services`. All entities are
tenant-scoped (`AuditableEntity` / `BaseEntity`). The former `HRManagement.SharedKernel.Recruitment` classes
(`RecruitmentRequisition`, `CandidateApplication`, `OfferLetter`) had no runtime consumer and were deleted in PLAN-33;
`scripts/hcm001-validate.ps1` fails if that folder comes back.

### RecruitmentRequisition

A staffing request for one funded position (EST-001).

```
RecruitmentRequisition
├── RequisitionNumber, JobTitle, VacancyCount, JustificationNotes, LegalSourceId
├── FundedPositionId (Guid) — FK to FundedPosition
├── Status (RequisitionStatus) — Draft, PendingApproval, Approved, Published, Closed, Cancelled
├── RequestedDate / ApprovedDate / PublishedAt / ClosedDate
├── WorkflowInstanceId (Guid?) — approval instance (RECRUITMENT-REQUISITION-APPROVAL)
├── PublishingChannel (RecruitmentPublishingChannel) — PublicOnly, InternalOnly, Both
├── PublicJobPostingId / InternalVacancyId — channel rows created at publish
├── ApplicationClosingDate (DateOnly?) / IntakeClosedAt — intake window (PLAN-26)
├── CancellationReason / CancelledAt / CancelledBy — PLAN-26 cascade
├── RequiresCommitteeApproval (bool) — ranking gate (PLAN-28, D-5)
├── MinQualificationCode, MinYearsOfExperience, MinAge, MaxAge, RequiredDocumentTypes — PLAN-30 criteria
└── CompetitionLineId (Guid?) — attached to a competition line (PLAN-31)
```

### RecruitmentApplication

One candidate's case on a requisition (or on a competition before allocation).

```
RecruitmentApplication
├── ApplicantFullName, NationalId ([AuditRedact]) / NationalIdHash, LegalHold, AnonymizedAt
├── RequisitionId (Guid?) / CompetitionId / CompetitionLineId / SourceInternalApplicationId
├── IntakeChannel (RecruitmentIntakeChannel) — HrEntered, Public, Internal, Legacy
├── Status (RecruitmentApplicationStatus) — Submitted, UnderReview, Shortlisted, InterviewScheduled, Assessed,
│     Ranked, OfferExtended, OfferAccepted, Hired, Rejected, Withdrawn, OfferDeclined (11), ReserveListed (12)
├── AssessmentScore (final score written by the scoring service), CriteriaVersionId, Rank / RankedAt, RankOverrideReason / RankOverriddenBy / At
├── ConflictOfInterestDeclared / ConflictOfInterestDeclaredAt
├── ActiveOfferLetterId, ReserveUntil / ReserveListedAt
├── RejectedAt, RejectionKind (RecruitmentRejectionKind), RejectionNote, RankBeforeRejection
├── AppealStatus (RecruitmentAppealStatus) — None, Pending, Decided
├── Profile: EducationLevelCode, Specialization, GraduationYear, YearsOfExperience, BirthDate
├── EligibilityFlags / EligibilityEvaluatedAt / EligibilityOverrideReason
└── HiredEmployeeId
```

`RecruitmentRejectionKind`: SelectionDecision, RequisitionCancelled, RequisitionClosed, NotSelectedOnHire,
ReserveExpired, OfferRevoked, CompetitionResult. `RecruitmentApplicationEvent` is the append-only, PII-free event log of
an application (`EventType` from `RecruitmentApplicationEventTypes`, `FromStatus`/`ToStatus`, `RelatedEntityId`,
`OccurredAt`).

### OfferLetter

```
OfferLetter
├── RecruitmentApplicationId, FundedPositionId, OfferNumber, LegalSourceId, WorkflowInstanceId
├── Status (OfferStatus) — Draft, PendingApproval, Issued, Accepted, Declined, Expired, Revoked
├── IssuedDate (DateTime?) / ValidityDays (default 14) / ExpiryDate (set at issue)
├── OfferedSalary, GradeCode, ProposedStartDate
├── IsMeritOverride / MeritOverrideReason — merit exception (PLAN-27)
├── GradeDeviationReason, SalaryUnverified / SalaryJustification, GradeMinSalary / GradeMaxSalary / SalaryCheckedAt
├── AcceptedDate, AcceptedBy (OfferAcceptedBy: Candidate | HrOnBehalf), AcceptanceNote, AcceptanceEvidenceFileId
├── DeclineReason, DeclineReasonCategory (OfferDeclineReasonCategory), DeclinedAt, DeclinedBy, DeclineEvidenceFileId
├── RevokeReason / RevokedAt, ExpiredAt
├── DocumentSha256 / DocumentGeneratedAt — SHA-256 of the letter's canonical rendered fields, stored at issue; the PDF prints its first 12 characters as the verification code (PLAN-29)
└── IsConvertedToEmployee (bool) — idempotent hire conversion
```

### Other recruitment entities

| Entity | Role |
|--------|------|
| `RecruitmentAppeal` | `Kind` = `Rejection` or `ResultObjection` (PLAN-31); `Status` Pending/Decided; independent reviewer identified by employee (`IndependentReviewerEmployeeId`; legacy `IndependentReviewerId`), `AssignedAt`/`AssignedBy`, `WorkflowInstanceId`; `Outcome` (`RecruitmentAppealOutcome`: Upheld, UpheldRankCorrection, Dismissed, UpheldNoRemedy, LegacyReopen, ObjectionUpheld, ObjectionDismissed), `CorrectedRank`/`ObjectionCorrectedScore`; `FiledBy`; `InfoRequest`/`AdditionalInfo` |
| `SelectionCommitteeSession` / `SelectionCommitteeMember` / `SelectionCommitteeVote` / `SelectionCommitteeDecision` / `SelectionCommitteeMemberCoi` | Committee per requisition (`Purpose`, chair, submit/lock/approval), members by employee, per-candidate votes, decisions (`Recommendation`, `ProposedRank`, `Rationale`) and conflict declarations (PLAN-28) |
| `RecruitmentInterviewSlot` / `RecruitmentInterviewPanelMember` | Interview lifecycle (status, mode, outcome, reschedule chain) and panel by employee (PLAN-26) |
| `RecruitmentAssessmentCriteria` / `RecruitmentAssessmentScore` | Versioned, lockable criteria per requisition; per-item, per-assessor scores (PLAN-26) |
| `RecruitmentCompetition` / `CompetitionLine` / `CompetitionPreference` / `CompetitionResultVersion` (+ `CompetitionResult`, `CompetitionAllocation`, `CompetitionReserveEntry`) / `ExamSession` (+ `ExamCandidate`, `ExamMarkerAssignment`, `ExamScore`) | Multi-vacancy competitions, line preferences, versioned results, allocation, line reserves and anonymous double-marked exams (PLAN-31) |
| `CitizenApplication` | Public-channel row (reference number, contact, consent, portal lock), linked through `PromotedRecruitmentApplicationId` (PLAN-30) |
| `CandidateDocument` | Uploaded document stored in File.Storage (`FileId`) |
| `CandidateNotification` | Planned/sent candidate message (masked recipient, idempotency key, retry state) |
| `CandidateOtpChallenge` | `/careers/my` one-time code, stored as HMAC only |
| `CandidateRetentionRun` | One retention job run (dry run flag, counts, skips) |

### Rule classes (`HR.Core.Domain/Services`)

`RequisitionWorkflowRules` (requisition transitions, blocking statuses, closing date), `SelectionWorkflowRules`
(application transitions, system-only targets, terminal statuses, transition gates), `OfferWorkflowRules` (offer
transitions, issue/expiry/accept), `OfferEligibilityRules` (who can receive an offer, merit order),
`RecruitmentAppealWorkflowRules` (appeal window, appealable kinds, outcome validation, assigned reviewer), `SelectionCommitteeRules`
(chair, quorum, majority, lock). Related: `AssessmentCriteriaRules`, `ApplicationRejection`,
`RecruitmentEligibilityRules`.

## Runtime Status (PLAN-21 – PLAN-33)

"Implemented — verified" is used only when the evidence folder's `result.json` has `status: Pass`. All live runs below
are on the local acceptance stack (lab profile: `NationalScope:IsLabScale=true`, policy engine Off); nothing has run
in remote CI (branch not pushed). Plan documents: `docs/acceptance-plans/PLAN-2x-*.md` / `PLAN-3x-*.md`.

| Plan | Scope | Status | Evidence path | Last live result |
|------|-------|--------|---------------|------------------|
| PLAN-21 | Requisition approval from the UI (AC-HCM-21) | Implemented — verified | `acceptance-evidence/AC-HCM-21/` | Pass, 21a–d — PLAN-33 P6 clean-stack run, 2026-10-29 (`ACCEPTANCE_REQUIRE_ALL=true`) |
| PLAN-22 | Remaining recruitment approvals from the UI (AC-HCM-22) | Implemented — verified | `acceptance-evidence/AC-HCM-22/` | Pass, 22a–e — PLAN-33 P6, 2026-10-29 (`ACCEPTANCE_REQUIRE_ALL=true`) |
| PLAN-23 | Unified work inbox (AC-WF-23) | Implemented — verified | `acceptance-evidence/AC-WF-23/` | Pass, 23a–f — PLAN-33 P6, 2026-10-29 (`ACCEPTANCE_REQUIRE_ALL=true`) |
| PLAN-24 | Establishment change approval + inbox extension (AC-EST-24) | Implemented — verification pending: no `AC-EST-24*` evidence folder is committed (never run live); `AcEst24EstablishmentWorkflowUiTests` is not a `[RequiredAcceptance]` scenario, so a missing precondition is recorded as `Blocked` and skipped even under `ACCEPTANCE_REQUIRE_ALL=true` (before PLAN-33 it skipped silently: `reports/plan33-acceptance-inventory.md`) | — | none recorded |
| PLAN-25 | Production workflow binding and server-side role enforcement (G-01, G-02, G-04, G-05, G-14, G-15) | Proposed (deferred) | — | — |
| PLAN-26 | Assessment criteria, interview lifecycle, requisition integrity (AC-HCM-26) | Implemented — verified for 26a–g and 26i; AC-HCM-26h verification pending (Blocked: no second central `hr-core` persona to approve the establishment step; the freeze cascade is covered by `Hcm026EstablishmentCascadeIntegrationTests`) | `acceptance-evidence/AC-HCM-26/` (folder status Blocked) | PLAN-33 P6, 2026-10-29: 4 tests Pass, 26h Blocked |
| PLAN-27 | Offer integrity, merit order, reserve list (AC-HCM-27) | Implemented — verified | `acceptance-evidence/AC-HCM-27/` | Pass ×4 — PLAN-33 P6, 2026-10-29 (27f–g failed in PLAN-32 without File.Storage; passes now) |
| PLAN-28 | Committee and appeal governance (AC-HCM-28) | Implemented — verified | `acceptance-evidence/AC-HCM-28/` | Pass ×4 — PLAN-33 P6, 2026-10-29 |
| PLAN-29 | Hire closure, contract terms, official documents (AC-HCM-29) | Implemented — verified | `acceptance-evidence/AC-HCM-29/` | Pass ×3 — PLAN-33 P6, 2026-10-29 |
| PLAN-30 | Candidate experience, unified channel, notifications (AC-HCM-30) | Implemented — verified | `acceptance-evidence/AC-HCM-30/`; `reports/plan30-final-report.md` | Pass ×8 — PLAN-33 P6, 2026-10-29 (30c document scan now runs with File.Storage) |
| PLAN-31 | Multi-vacancy competitions and exams (AC-HCM-31) | Implemented — verified | `acceptance-evidence/AC-HCM-31/`; `reports/plan31-final-report.md` | Pass ×2 — PLAN-33 P6 after `acceptance-reset.ps1`, 2026-10-29 |
| PLAN-32 | Recruitment reports and KPIs (AC-HCM-32) | Implemented — verified | `acceptance-evidence/AC-HCM-32/`; `reports/plan32-final-report.md` | Pass ×4 — PLAN-33 P6, 2026-10-29 |
| PLAN-33 | Live verification of the recruitment path and documentation (AC-HCM-21/22, AC-WF-23, AC-HCM-33) | Implemented — verified on the acceptance (lab) profile (CI not executed); the prod-like journey fails at the first approval (G-02, PLAN-25) | `acceptance-evidence/AC-HCM-33/`, `acceptance-evidence/AC-HCM-33/prodlike/`; `reports/plan33-final-report.md` | Journey Pass (P6 clean stack, 2026-10-29); 33a/33b/33d Pass; prod-like Fail at stage «1 requisition» (2026-10-29) |

PLAN-33 verification assets: `src/Tests/HRManagement.AcceptanceTests/AcceptanceGate.cs` (a missing precondition is
recorded as `Blocked` and skipped, or fails `[RequiredAcceptance]` scenarios under `ACCEPTANCE_REQUIRE_ALL=true`),
deterministic recruitment states (`HR.Core.Infrastructure/Seeding/AcceptanceRecruitmentStatesPatch.cs` +
`testdata/acceptance/recruitment-states.json`), the requisition-to-hire journey
`src/Tests/HRManagement.E2E/RecruitmentToHireJourneyE2ETests.cs` (`Category=Journey`; the prod-like variant
`Category=JourneyProdLike` runs on `docker-compose.acceptance-prodlike.yml` and is excluded from CI until PLAN-25), and
`src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs` (G-02 behaviour).

### Known gaps

Details: [HCM_001_GAP_STUDY](HCM_001_GAP_STUDY.md) (section 4.1).

- **G-02** — with `NationalScope:IsLabScale=false` and the policy engine Off (the Staging/Production profile), a
  submitted requisition or offer stays `PendingApproval` with no workflow instance (the UI shows a warning): approval is
  refused and the offer is never `Issued`; the selection committee is locked without a workflow and shows no warning. Proven by
  `Hcm033LabScaleBehaviourTests` and by the prod-like journey failure above. Fix: [PLAN-25](acceptance-plans/PLAN-25-hcm-production-workflow-binding.md).
- **G-04** — a workflow step's `RoleRequired` is checked only in the UI (workflow panel / work inbox), not on the
  server POST. Fix: PLAN-25.

### Earlier plans (HCM-01 – HCM-07)

Scope per plan is in the [Plan map](#plan-map-hcm-001); none of them has an evidence folder under
`acceptance-evidence/` with a `result.json` (`acceptance-evidence/HCM-01/` holds only `steps.md`).

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
| Decline (HR on the candidate's behalf, optional evidence; or the candidate on `/careers/my` — PLAN-30), reason ≥ 5 | `Declined` | `OfferDeclined` |
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
- `CandidateNotificationJob` (COM-030, every 60 s, `Recruitment:Jobs:CandidateNotificationsEnabled` /
  `CandidateNotificationsIntervalSeconds`, lock 8675315) records the offer reminder (`Recruitment:OfferReminderHours` = 48,
  one `OfferExpiryReminder` event per Issued offer), plans candidate messages from the application events and sends them
  (see `docs/COM_001_NOTIFICATIONS.md`).
- `CandidateRetentionJob` (HCM-030 G, weekly, `Recruitment:Jobs:CandidateRetentionEnabled` /
  `CandidateRetentionIntervalMinutes` = 10080, lock 8675316) anonymizes due candidate pairs after
  `Recruitment:Retention:Months` (36); a dry run unless `Recruitment:Retention:Apply` (see `docs/GOV_001_RETENTION_RUNBOOK.md`).
- Reserve status and «حتى {date}» are shown on application details, the application list and the rank board.
- Recall from the reserve on non-commencement is PLAN-29.

## PLAN-28 — Selection committee and appeal governance (HCM-F)

G-09, G-10 and the appeal part of G-05, decision **D-5** (committee switch). Plan:
[PLAN-28](acceptance-plans/PLAN-28-hcm-committee-appeal-governance.md); execution:
[PLAN-28-EXECUTION](acceptance-plans/execution/PLAN-28-EXECUTION.md).

### Committee model (session 1)

- `SelectionCommitteeSession`: `Purpose` (Interview / Ranking — default / Final), `ChairEmployeeId`, `SubmittedAt`,
  `LockedAt`, `ApprovedAt`. Members are picked from the employee picker; the unique index
  `(TenantId, SessionId, EmployeeId)` refuses duplicates.
- Per candidate: `SelectionCommitteeMemberCoi` (a conflict removes that member's vote on that candidate only),
  `SelectionCommitteeVote` (Agree / Disagree / Abstain — recorded by the HR secretary on the member's behalf) and
  `SelectionCommitteeDecision` (Recommend / Reserve / Reject, `ProposedRank`, `Rationale`).
- Rules (`SelectionCommitteeRules`): one non-recused chair, quorum of at least 3 non-recused members, a
  Recommend/Reserve decision needs a majority of the eligible voters (`Agree * 2 > eligible`), every Assessed/Ranked
  candidate needs a decision before submit. Submit locks the session (no member, vote, COI or decision change).
- `SELECTION-COMMITTEE-APPROVAL` (3 steps), checked on the server: the session creator never approves; quorum is
  re-checked from step 2; members do not approve step 3.
- Minutes: `GET SelectionCommittees/Minutes/{id}` builds the PDF on demand from the locked data (not stored —
  archiving via File.Storage is PLAN-29).

### Ranking gate (D-5)

`RecruitmentRequisition.RequiresCommitteeApproval` (initial value `Recruitment:CommitteeApprovalDefault`: false;
true in Production/Staging; frozen once a rank exists). When on, `AssignRankAsync` needs the latest approved
Ranking/Final session to Recommend or Reserve the candidate, and the rank must equal `ProposedRank` unless a
`RankOverrideReason` (20+ chars, audited `RankOverride`) is given. «تطبيق ترتيب اللجنة»
(`ApplyCommitteeRanking`) applies all proposed ranks in one transaction.

### Rejection kinds and the appeal window (session 2)

Every rejection goes through `ApplicationRejection.Apply`, which records `RejectedAt`, `RejectionKind`,
`RejectionNote` and `RankBeforeRejection`:

| Kind | Where |
|------|-------|
| `SelectionDecision` | manual «مرفوض» on the application (optional note) |
| `RequisitionCancelled` / `RequisitionClosed` | requisition cancel (incl. position freeze) / close cascade |
| `NotSelectedOnHire` | unranked open siblings when a candidate is hired |
| `ReserveExpired` | `RecruitmentReserveExpiryJob` |
| `OfferRevoked` | offer revoke without «خطأ إداري» |

An appeal is filed within `Recruitment:AppealWindowDays` (14) of `RejectedAt` (legacy rows: `ReviewedAt`), only for
`Recruitment:AppealableRejectionKinds` (default SelectionDecision, NotSelectedOnHire, OfferRevoked; legacy rows count
as SelectionDecision), once per rejection (a new rejection after an upheld appeal can be appealed again).
Otherwise: «لا يقبل هذا النوع من الرفض الاستئناف.» / «انقضت مهلة الاستئناف على هذا الرفض.»

### Appeal lifecycle (single path)

1. **File** — HR on the candidate's behalf (`FiledBy = HrOnBehalf`) or the candidate on `/careers/my` (`FiledBy = Candidate`, PLAN-30), reason of
   20+ chars. No reviewer and no workflow yet; the application's `AppealStatus` becomes `Pending` (no rank and no offer
   for it, and no hire on the requisition — `Recruitment:BlockHireOnPendingAppeal`, default true).
2. **Assign** — HR (`HrCoreWrite`) picks the reviewer from the employee picker (`IndependentReviewerEmployeeId`).
   Refused («يجب أن يكون المراجع مستقلاً عن اللجنة والمقابلة والتقييم.») for the applicant, any committee member of
   the requisition (recused ones included), the interview panel and the assessors of the application, the filer and
   the assigner. Assignment starts `RECRUITMENT-APPEAL-REVIEW` (whatever the policy mode); reassignment while pending
   cancels the running instance.
3. **Decide** — only the assigned reviewer: `compliance` role (or Admin) and `employee_id` claim equal to
   `IndependentReviewerEmployeeId` (legacy rows: Keycloak user id equal to `IndependentReviewerId`); otherwise
   «غير مصرح: يجب أن تكون المراجع المستقل المعيّن.». Explicit outcome and a rationale of 30+ chars; the outcome is
   stored, the step approved, and the workflow handler applies it:

| Outcome | Effect |
|---------|--------|
| `Upheld` | application → `UnderReview`, rejection fields and rank cleared, scores kept |
| `UpheldRankCorrection` | application → `Assessed` → ranked at `CorrectedRank` with the rationale as override reason (audited `AppealRankCorrection`; the committee gate does not apply). Needs a rank before rejection and a Published requisition |
| `Dismissed` | stays `Rejected` with its original kind/date/note |
| `UpheldNoRemedy` | stays `Rejected`; audited `AppealUpheldNoRemedy` (compliance alert: deferred follow-up, `// PLAN-30: alert compliance` in `RecruitmentAppealWorkflowHandler`) |

   On a Closed/Cancelled requisition only `Dismissed` / `UpheldNoRemedy` are allowed. A generic workflow approval
   without a stored outcome is refused.
4. **Request information** — the reviewer returns the step with a question (`InfoRequest`); the appeal stays pending;
   HR **resubmits** (`AdditionalInfo`) and a new review starts for the same reviewer.

The removed direct-reopen path (`ReopenAppeal`) left `AppealNotes`; migration `HCM028_AppealGovernance` turns them
into Decided `LegacyReopen` appeals (insert-only). If no `RECRUITMENT-APPEAL-REVIEW` definition can be resolved, the
assigned reviewer decides directly under the same checks (audited `AppealDecidedWithoutWorkflow`).

### Compliance access (PLAN-25 slice)

`HrCoreAppealReview` (Admin | HrCore | Compliance) on `RecruitmentAppealsController` and `HrCoreWorkInbox` (HR.Core
read roles + Compliance) on the work inbox; both controllers are exempt from the HR.Core module filter and the gateway
maps their paths (and HR.Core static assets) accordingly. A compliance-only user sees only the appeals assigned to
them; the application summary on the appeal page omits the national ID.

## PLAN-29 — Hire closure, contract terms and official documents (HCM-G)

G-12, G-13, G-22 and the offer-letter part of G-18, decision **D-6** (appointment decision, medical step). Plan:
[PLAN-29](acceptance-plans/PLAN-29-hcm-hire-contract-documents.md); execution:
[PLAN-29-EXECUTION](acceptance-plans/execution/PLAN-29-EXECUTION.md); session 1 handoff:
`reports/plan29-session1-handoff.md`.

### Hire closure safety (session 1, G-12)

- `HirePersonMatchService` finds an existing employee by `NationalIdHash` only (the column is encrypted at rest);
  same normalized name + birth date is a warning, never a block. The Hire form opens in «ربط موظف موجود» when the
  application's national id is on file.
- «إنشاء جديد» with a national id already on file is a form error («الرقم الوطني مسجَّل لموظف قائم — استخدم «ربط موظف
  موجود».»); a national id different from the application's (or from the linked employee's) needs a reason of 10+
  chars, audited `HireNationalIdMismatch` without the ids.
- `HireClosureException` (DuplicatePerson / Concurrency / PositionNotVacant / InvalidState / Legal): a 23505 on the
  national-id index, other unique violations and concurrency conflicts are mapped in the closure; the transaction
  rolls back and the change tracker is cleared, so no outbox row survives. The form never returns a 500 for these.
- Employment type is the domain `EmploymentType` (Permanent «دائم», Administrative «إداري», Temporary «متعاقد مؤقت»);
  legacy codes map (PERMANENT, TEMPORARY, CONTRACT → Temporary), INTERNSHIP is refused. `Recruitment:DefaultEmploymentType`
  pre-selects; another choice needs a reason (audited `HireEmploymentTypeChange`).

### ICandidateAccepted v4 (G-13)

Additive (`SchemaVersion => 4`, default members): `OfferedSalary`, `GradeCode`, `LegalRegimeId`, `PayrollRegimeCode`,
`EmploymentType`, `PositionCode`, `OrgUnitId`, `IsInternalMobility`. External hires publish it with the offer terms;
**internal mobility publishes `IInternalPlacementCompleted` only** (HR.Contracts also ignores `IsInternalMobility`).
HR.Contracts stores the lineage on `ServiceTransaction` columns (`RecruitmentApplicationId`, `RequisitionId`,
`OfferLetterId`, `ExpectedStartDate`; backfilled from the notes marker) and the terms on the pending contract
(`GradeCode`, `AgreedBaseSalary`, `LegalRegimeId`, `EmploymentType`, `SourceOfferLetterId`). The contract type comes
from `ContractTypeRegimeMap` (most specific (regime, type) first); without a match an intervention is opened (no
silent `DEFAULT` unless `Contracts:AllowDefaultContractTypeFallback`). `IContractActivated` carries
`BaseSalary`/`PayGradeCode` from the contract, and Payroll builds the pay profile from them (grade in the scale in force
at the start date; out-of-range salary → profile + review hold).

### Official offer letter (G-18 part)

When an offer is issued the SHA-256 of a canonical JSON of its rendered fields is stored (`DocumentSha256`,
`DocumentGeneratedAt`; migration `HCM029_OfferDocument`). `GET OfferLetters/Document/{id}` (Issued/Accepted) renders
the Arabic PDF on demand with «رمز التحقق» (first 12 hex characters of the stored hash); a later difference is shown
as a warning, never re-hashed. The candidate downloads it on `/careers/my` (PLAN-30); File.Storage archiving is a follow-up.

### Appointment decision and medical step (G-22, D-6)

The hire checklist gains «إصدار قرار التعيين» (`HireChecklist:RequireAppointmentDecision`, default on) and «الفحص
الطبي قبل التعيين» (`HireChecklist:RequireMedicalFitness`, default off); completion and the hire saga require them.
Overdue commencement (`Recruitment:CommencementDeadlineDays` in HR.Contracts, default 30) is flagged and opens an
intervention (detection only). Details: [HIRE_ONBOARDING_GUIDE](HIRE_ONBOARDING_GUIDE.md).

## PLAN-30 — Candidate experience, unified channel and notifications (HCM-H)

Plan: [PLAN-30](acceptance-plans/PLAN-30-hcm-candidate-experience.md); execution: [PLAN-30-EXECUTION](acceptance-plans/execution/PLAN-30-EXECUTION.md).
Addresses G-16, G-17, G-18 and the candidate-data part of G-20 (D-4 self-service; D-8 provider still pending).

### Unified intake (session 1, G-16)

- Every requisition-linked application — public (`PublicCareersController.Apply`) or internal (`EssTalentController.Apply`) —
  creates its `RecruitmentApplication` at `Submitted` immediately (`UnifiedIntakeService`), linked through the existing
  `CitizenApplication`/`InternalApplication.PromotedRecruitmentApplicationId` (now unique, filtered). `IntakeChannel`
  (`HrEntered`/`Public`/`Internal`/`Legacy`) marks the source. HR screens one list per requisition
  (`RecruitmentApplications?RequisitionId=…`); the channel rows' own status is no longer written for linked rows, and the
  old status/COI actions refuse them with «يُدار هذا الطلب من مسار التوظيف الموحد.». Legacy postings without a requisition
  keep the old flow. Cross-channel duplicates on a requisition are refused.
- Backfill of rows submitted before unification: `scripts/hcm030-backfill-unified-intake.ps1` (dry run by default, `-Apply`,
  `-ConnectionString` or `-DockerContainer`), mapping in `UnifiedIntakeStatusMapping` = the SQL; no events, no messages.

### Public status and application events (session 1)

- `RecruitmentApplicationEvent` (append-only, no PII) records `Received`, `StatusChanged`, `Interview*`, `Offer*`,
  `AppealFiled`, `AppealDecided` in the same save. It drives the timeline and the candidate notifications.
- `CandidatePublicStatusProjection` maps internal statuses to the candidate view: «قيد المراجعة», «مدعو للمقابلة»,
  «قيد التقييم النهائي», «عرض بانتظار ردك حتى …», «مقبول — بانتظار استكمال التعيين», «في قائمة الاحتياط حتى …»,
  «العرض منتهٍ/مرفوض», «غير مقبول» (+ appeal deadline), «منسحب». No rank, score, committee, panel, note or salary. A draft or
  pending offer is not shown as an offer (`OfferExtended` is set at draft).

### Profile and eligibility (session 1, G-17)

- Apply form: qualification (MDM codes), specialization, graduation year, years of experience, birth date; requisition
  criteria (`MinQualificationCode`, `MinYearsOfExperience`, `MinAge`/`MaxAge`) shown as «شروط التقديم».
- `RecruitmentEligibilityRules` flags (never rejects): `BelowMinQualification`, `QualificationUnranked`,
  `BelowMinExperience`, `AgeBelowMin`, `AgeAboveMax`, `MissingProfileData`, `MissingRequiredDocuments`. Shortlisting a
  flagged application needs a reason (≥ 10) → `EligibilityOverride` audit. Ranks/age bounds are options
  (`Recruitment:Eligibility:*`).

### Candidate documents (session 2, G-17)

- `CandidateDocument` (CV, certificate, ID copy, other) uploaded on the apply form and relayed by HR.Core to File.Storage
  through the service upload (`api/files/service-upload`, scope `file-storage.service-upload`, profile `CandidateDocument`:
  pdf/jpg/png ≤ 5 MB, quarantine + scan). Limits: 5 files, 9 MiB per post. `RecruitmentRequisition.RequiredDocumentTypes`.
  Any upload failure fails the whole apply («تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.»).
- HR Details «المستندات» card: «سليم» (download through HR.Core), «قيد الفحص», «محجور — غير متاح», «تعذّر الفحص — غير متاح».

### Candidate notifications (session 2, G-18)

- One message per decision event on the candidate's chosen channel (email or SMS) with consent; FileDrop in the lab, provider
  `None` in Production/Staging until D-8. No national id, name or salary. HR Details «الإشعارات» card. Job
  `CandidateNotificationJob` (lock 8675315) and the 48 h offer reminder. Details: [COM_001_NOTIFICATIONS](COM_001_NOTIFICATIONS.md).

### Candidate portal `/careers/my` (session 3, G-18, D-4)

- Sign-in «الدخول إلى طلباتي»: reference number + national id → a 6-digit one-time code sent on the registered contact (neutral
  answer always; 60 s resend window; 10-minute validity; 5 wrong codes → 30-minute lock audited `CandidatePortalLocked`) →
  `careers_session` cookie (Data Protection, absolute 30 minutes, HttpOnly, SameSite=Strict, path `/careers`). The gateway
  forwards that exact cookie only on the `careers-public` route. OTP endpoints use `careers-otp` (shared public window + per-client
  window). Codes are stored only as HMAC (`Recruitment:Portal:OtpHmacKey`, required in Production).
- «طلبي»: status and deadline, timeline, interviews (no panel names), documents (upload missing ones while intake is open),
  offer card with terms and salary, «تنزيل خطاب العرض» (PDF + verification code), accept (acknowledgement + typed full name,
  `AcceptedBy = Candidate`), decline with a reason (`DeclinedBy = Candidate`, same next-candidate behaviour as HR), withdraw
  (in-page confirmation; before `OfferAccepted` only; settles the active offer), appeal within the window
  (`FiledBy = Candidate`) and answers to information requests. Every action loads the application from the session only and
  runs under the session tenant.
- `/careers/track` remains a read-only lookup that shows only the public status label.
- HR on-behalf acceptance (`OfferLetters/Details` «قبول نيابةً عن المرشح»): reason ≥ 20 + signed evidence file, audited
  `AcceptOfferOnBehalf`; policy `HrCoreWrite`. Details show who accepted («المرشح عبر بوابة التوظيف» / «HR نيابة عن المرشح»).

### Candidate retention (session 2/3, G-20 part)

- Pair rule (application + channel row): requisition closed/cancelled + `Recruitment:Retention:Months` (36), not hired, no legal
  hold, no pending appeal. Anonymization clears identity, contact, profile and free text on both rows, releases and deletes
  documents and on-behalf offer evidence, neutralizes messages and deletes OTP challenges. Dry run unless
  `Recruitment:Retention:Apply` (false in Production/Staging). Admin screen «الاحتفاظ ببيانات المرشحين»
  (`CandidateLifecycle/Index`). Details: [GOV_001_RETENTION_RUNBOOK](GOV_001_RETENTION_RUNBOOK.md).

## PLAN-31 — Multi-vacancy competitions and exams (HCM-I)

Plan: [PLAN-31](acceptance-plans/PLAN-31-hcm-competitions-exams.md); execution: [PLAN-31-EXECUTION](acceptance-plans/execution/PLAN-31-EXECUTION.md);
decisions J1–J39: [`reports/plan31-progress.md`](../reports/plan31-progress.md). Addresses G-19 (D-2: legal inputs per competition).

### Competition and lines

- `RecruitmentCompetition` groups `CompetitionLine`s; each line (title, grade, optional job/unit, optional line pass mark)
  attaches approved, unpublished requisitions of its grade — a requisition stays one funded position (decision 2). The legal
  inputs (objection window, reserve validity, max lines per candidate, pass mark, reconcile threshold/rule, tie-break order)
  are entered per competition; Production/Staging hold no defaults (decision 1). Approval RECRUITMENT-COMPETITION-APPROVAL
  (creator excluded); announcement publishes the attached requisitions and one posting (`/careers/competitions/{id}`) and/or an
  internal vacancy. Attached requisitions refuse their own publish/attach/criteria changes while the competition holds them.
- One application per person per competition (`RecruitmentApplication.CompetitionId`, `RequisitionId` null until allocation),
  ordered `CompetitionPreference`s up to the maximum, per-line eligibility flags, the union of the lines' required documents.
  Intake closes with the competition's closing date (`RecruitmentIntakeCloser`).

### Exams and anonymous double marking

- `ExamSession`s per type; «التوزيع على القاعات» gives each admitted applicant one random 8-digit exam number (kept on re-seating);
  admission card PDF with a SHA-256 verification code. Attendance, then «اعتماد انعقاد الجلسة» opens marking.
- Markers are assigned by employee (first, second, reconciler; role `hr-exam-marker`, policy `HrCoreExamMarking`, gateway rule
  `/ui/hr-core/ExamMarking`); they see exam numbers only, never each other's marks. Above the threshold the reconciler marks and
  the competition's rule decides. «إغلاق التصحيح» stores `FinalMark` and locks the marks.

### Results, objections, allocation

- Results: exam points (normalized) + non-exam assessment of the reference criteria, outcome against the pass mark, one overall
  rank; unresolved ties need a recorded decision. RECRUITMENT-RESULTS-APPROVAL (committee then HR director; the person who
  computed is excluded). Publication: public list by exam number (HTML + PDF); the candidate sees score and rank on «طلبي» only.
- Objections (decision 5) reuse `RecruitmentAppeal` (`Kind = ResultObjection`) within the window counted from the first
  publication; the independent compliance reviewer (markers excluded) upholds with a corrected mark → a new result version,
  approved and published; the previous version is superseded.
- «إنهاء المسابقة» (after the window, no pending objection): merit then preference allocation to the lines' requisitions (ranked
  on the requisition — this replaces the PLAN-28 committee gate for attached requisitions, J4), line reserve lists with expiry,
  the rest rejected (kind `CompetitionResult`). Idempotent and serialized (`FOR UPDATE`).
- Bulk offer drafts (salary rule `Recruitment:Competition:DefaultOfferSalary`) into the existing per-offer approval
  (decision 7). Decline, expiry, revocation or withdrawal recalls the first valid reserve of the same line onto the vacated
  requisition (decision 8); non-commencement only leaves a follow-up marker. The hire publishes `ICandidateAccepted` v4 unchanged.

### Candidate side

- `/careers/my`: preferences, exams with «تنزيل بطاقة الامتحان», result after publication, objection form or «انتهت مدة الاعتراض
  في …», objection status, allocation or reserve. Seven messages (`ADMISSION_CARD_READY`, `RESULTS_PUBLISHED`,
  `OBJECTION_DECIDED`, `COMPETITION_ALLOCATED`, `COMPETITION_RESERVE`, `COMPETITION_RESERVE_RECALLED`,
  `COMPETITION_NOT_SUCCESSFUL`) with no score, rank or national id (decision 4). Retention starts at the competition's end;
  DSAR carries papers, marks, results, allocation and reserve.

## PLAN-32 — Recruitment reports and KPIs (HCM-J)

Plan: [PLAN-32](acceptance-plans/PLAN-32-hcm-recruitment-analytics.md); execution: [PLAN-32-EXECUTION](acceptance-plans/execution/PLAN-32-EXECUTION.md);
decisions K1–K23: [`reports/plan32-progress.md`](../reports/plan32-progress.md); KPI definitions:
[`STR_007_KPI_DEFINITIONS.md`](STR_007_KPI_DEFINITIONS.md). Addresses G-21.

- **KPIs** (`recruitment_*`, STR-007): funnel, time to fill (from approval, fallback request — aligned with the frozen
  `hire_cycle_time`), time to hire, stage time, offer acceptance (revoked excluded), decline reasons by category, channel mix,
  appeal rate and objections, merit/exception offers, open vacancies, approval SLA, competitions. Timings come from
  `RecruitmentApplicationEvent`; rows older than the event log use documented evidence fallbacks, and every KPI shows its `n`
  and coverage. Exact counts; median and P90 (nearest rank) by the rules in `KpiStatistics`.
- **`RecruitmentMetricsService`** (read-only, `AsNoTracking`, tenant-filtered) takes the caller's `OrgAccessScope`: a
  requisition's unit is its funded position's; a branch manager sees only its units; an out-of-scope org-unit filter returns
  no data; competition aggregates are for unrestricted users only.
- **«تقارير التوظيف»** (`RecruitmentReports/Index`, `HrCoreReadOnly`; menu under «التوظيف والاستقطاب»): KPI cards with n and
  coverage, the funnel (Chart.js + data table), requisitions with ages, overdue approvals (SLA = step `TimeoutHours`, else
  `Recruitment:Analytics:ApprovalSlaHours:{code}`, else 48 h — an organizational policy value), offers and decline categories,
  channels, appeals/objections, merit overrides, open vacancies, competitions; `RecruitmentReports/Requisition/{id}` per
  requisition. Candidate names only for `HrCoreWrite`, otherwise the application reference.
- **Exports** (GET, audited `BulkExport`, bounded): Excel (seven sheets + competitions, REP-001 provenance block), PDF summary,
  applications CSV (synchronous up to 10,000 rows, else export job `hrcore.recruitment.applications.csv` with the names flag
  captured and re-checked at download). Never a national id.
- **Offer decline category** (`OfferLetter.DeclineReasonCategory`: الراتب، مكان العمل، عرض آخر، أسباب شخصية، طبيعة العمل، أخرى) is
  required on the HR and portal decline forms; withdrawal-driven declines are «أخرى»; older declines «غير مصنف».
- **Dev.Analytics:** `IRecruitmentRequisitionClosed` (aggregates only) on every close path through the HR.Core outbox →
  `RecruitmentRequisitionFact` and the «التوظيف» dashboard card with small cells shown «<5».
- **Deferred:** fairness metrics (no GOV-001 decision; candidate gender not collected), KPI targets (TBD after the first
  frozen quarter), org scope of the recruitment list screens (PLAN-25, finding D10), daily snapshot (not needed at the
  measured scale).

## Acceptance Criteria Mapping

- [x] No announcement without funded position — `FundedPositionId` required on requisition
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
- [x] PLAN-28 G-10 — committee votes, per-candidate COI, decisions, lock, server-side quorum/SoD, minutes; approval gates ranking (D-5)
- [x] PLAN-28 G-09 — one appeal path: independent employee-identified compliance reviewer, explicit outcomes, info requests; legacy reopen removed
- [x] PLAN-28 G-05 (appeal part) — compliance reaches the appeal pages and its inbox items
- [x] PLAN-28 — rejection kind/date recorded on every rejection; appeal window from `RejectedAt`; no hire while an appeal is pending
- [x] PLAN-29 G-12 — no duplicate person on hire, national-id mismatch justified and audited, no 500 on the hire form; employment type is a domain value
- [x] PLAN-29 G-13 — `ICandidateAccepted` v4 offer terms → contract (type by service regime, grade, salary) → `IContractActivated` → pay profile; explicit lineage columns; internal mobility opens no Appointment
- [x] PLAN-29 G-22 — appointment decision step (D-6), optional pre-employment medical, overdue-commencement intervention
- [x] PLAN-29 G-18 (offer letter) — official offer letter PDF with a verification hash fixed at issue
- [x] PLAN-30 G-16 — one intake path: public and internal applications enter `RecruitmentApplication` at `Submitted`; one screening list; backfill of pre-unification rows
- [x] PLAN-30 G-17 — candidate profile, requisition criteria, eligibility flags (never reject), candidate documents scanned in File.Storage
- [x] PLAN-30 G-18 — accurate public status and timeline, PII-free candidate notifications, `/careers/my` with OTP (offer PDF, accept/decline, withdraw, appeal, documents), restricted HR on-behalf acceptance
- [x] PLAN-30 G-20 (candidate data) — pair-aware retention job with legal hold, dry run by default, admin screen
- [x] PLAN-31 G-19 — multi-vacancy competitions with per-competition legal inputs, anonymous double-marked exams, approved and published results with an objection window and versions, allocation by merit and preference, line reserve recall, bulk offers into the per-offer approval (AC-HCM-31a..g Pass live, 2026-10-29)
- [x] PLAN-32 G-21 — recruitment KPIs (STR-007 `recruitment_*`) with n and coverage, «تقارير التوظيف» under org scope, overdue approvals against a documented SLA, exports without national id and names only for HrCoreWrite, Dev.Analytics aggregates with small-cell suppression (AC-HCM-32a..d Pass live, 2026-10-29)

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
| PLAN-28 | Committee decision body and ranking gate, single governed appeal path, rejection kinds, hire block, compliance access slice (G-09, G-10 addressed; G-05 appeal part; D-5) |
| PLAN-29 | Safe hire closure, employment type, `ICandidateAccepted` v4 → contract → payroll, contract-type map with intervention, appointment decision / medical steps, commencement watch, offer letter PDF (G-12, G-13, G-22 addressed; G-18 offer letter; D-6) |
| PLAN-30 | Unified intake and backfill, application events, public status projection, profile and eligibility flags, candidate documents (File.Storage service upload), candidate notifications (channel senders, FileDrop lab), pair-aware retention, `/careers/my` with OTP and the gateway cookie allow-list, HR on-behalf acceptance with evidence (G-16, G-17, G-18 addressed; G-20 candidate-data part; D-4) |
| PLAN-31 | Competitions and lines, intake with line preferences, exam sessions/seating/admission cards, anonymous double marking (`hr-exam-marker`), results approval/publication, objections and result versions, allocation and line reserves, bulk offers, portal/notifications/retention/DSAR for competitions (G-19 addressed; D-2 inputs per competition) |
| PLAN-32 | Recruitment KPIs and «تقارير التوظيف» (funnel, time to fill/hire, stage time, offers and decline categories, channels, appeals, merit overrides, open vacancies, approval SLA, competitions), scoped exports, `IRecruitmentRequisitionClosed` → Dev.Analytics (G-21 addressed) |
| PLAN-33 | Live verification of the recruitment path (AcceptanceGate, deterministic recruitment states, AC-HCM-21/22 and AC-WF-23 on seeded states, requisition-to-hire journey and prod-like variant, G-02 behaviour tests), SharedKernel phantom model removed, documentation aligned with the code (G-24) |

## Artifacts

| File | Purpose |
|------|---------|
| `docs/HCM_001_RECRUITMENT.md` | This document |
| `src/Services/HRCore/HR.Core.Domain/Entities/RecruitmentRequisition.cs`, `RecruitmentApplication.cs`, `OfferLetter.cs` | Runtime recruitment entities (see Domain Model) |
| `src/Services/HRCore/HR.Core.Domain/Services/` `RequisitionWorkflowRules.cs`, `SelectionWorkflowRules.cs`, `OfferWorkflowRules.cs`, `OfferEligibilityRules.cs`, `RecruitmentAppealWorkflowRules.cs`, `SelectionCommitteeRules.cs` | Recruitment rule classes |
| `scripts/hcm001-validate.ps1` | Validates the HCM-001 artifacts in HR.Core.Domain and fails if `SharedKernel/Recruitment` reappears (PLAN-33) |
| `scripts/hcm001-migrate-legacy-applications.ps1` | Backfill RequisitionId / flag legacy apps |
| `src/Services/HRCore/HR.Core.Domain/Services/CandidateApplicationLegalLifecycleService.cs` | Candidate anonymize/purge (Plan-6) |
| `scripts/hcm006-a11y-scan.ps1` | Public careers a11y baseline (Plan-6); PLAN-30 K4: candidate portal, apply form, label/for, error linking, OTP input, focus on errors, nonce CSP |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm06CandidateDsarTests.cs` | Candidate DSAR acceptance (Plan-6) |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs` | Atomic hire closure |
| `src/Tests/HRManagement.UnitTests/Hcm001RecruitmentTests.cs` | Behaviour tests on the HR.Core.Domain recruitment rules (PLAN-33) |
| `scripts/hcm026-validate.ps1` | Validates PLAN-26 artifacts, migrations and `Hcm026` tests |
| `scripts/hcm026-find-duplicate-requisitions.ps1` | Read-only duplicate check before `HCM026_RequisitionIntegrity` |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm26AssessmentInterviewTests.cs` | AC-HCM-26a..i acceptance |
| `scripts/hcm027-validate.ps1` | Validates PLAN-27 artifacts, migrations and `Hcm027` tests |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/ReserveListService.cs` | Reserve list, next candidate |
| `src/Services/Payroll/Payroll.Web/Controllers/PayGradeLookupInternalController.cs` | Payroll grade bounds (S2S) |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm27OfferMeritTests.cs` | AC-HCM-27a..h acceptance |
| `scripts/hcm028-validate.ps1` | Validates PLAN-28 artifacts, migrations, access slice and `Hcm028` tests |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealService.cs` | Appeal filing, reviewer assignment, decision, info request |
| `src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentAppealsController.cs` | Appeal pages (HR and compliance reviewer) |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm28CommitteeAppealTests.cs` | AC-HCM-28a..i acceptance |
| `scripts/hcm029-validate.ps1` | Validates PLAN-29 artifacts, migrations (HR.Core + HR.Contracts), config defaults and `Hcm029`/`Fun029`/`Pay029` tests |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/HirePersonMatchService.cs` | Person matching at hire |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/OfferLetterDocumentService.cs` | Offer letter data and verification hash |
| `src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/ContractTypeResolver.cs` | Contract type by service regime / employment type |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm29HireContractTests.cs` | AC-HCM-29a..i acceptance |
| `scripts/hcm030-validate.ps1` | Validates PLAN-30 artifacts, HR.Core migrations (no File.Storage migration), config per environment, `Hcm030`/`Fs030` tests, a11y scan and ROPA |
| `scripts/hcm030-backfill-unified-intake.ps1` | Links pre-unification citizen/internal rows (dry run by default, `-Apply`, `-DockerContainer`) |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/UnifiedIntakeService.cs` | Unified intake, linking, duplicate check, eligibility criteria |
| `src/Services/HRCore/HR.Core.Domain/Services/CandidatePublicStatusProjection.cs` | Candidate public status and timeline |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateNotificationDispatcher.cs` | Candidate message planning and sending |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateRetentionService.cs` | Pair-aware candidate retention |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateOtpService.cs` | `/careers/my` one-time code |
| `src/Services/HRCore/HR.Core.Web/Services/CandidatePortalService.cs` | `/careers/my` page and candidate actions |
| `src/Services/HRCore/HR.Core.Web/Services/OfferOnBehalfService.cs` | HR on-behalf acceptance/decline with evidence |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm30CandidateExperienceTests.cs` | AC-HCM-30a..j acceptance |
| `src/Tests/HRManagement.AcceptanceTests/AcceptanceGate.cs` | PLAN-33: `Blocked` + Skip on a missing precondition, Fail for `[RequiredAcceptance]` under `ACCEPTANCE_REQUIRE_ALL=true` |
| `src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceRecruitmentStatesPatch.cs`, `testdata/acceptance/recruitment-states.json` | PLAN-33: deterministic recruitment states for AC-HCM-21/22 and AC-WF-23 |
| `src/Tests/HRManagement.E2E/RecruitmentToHireJourneyE2ETests.cs` | PLAN-33: requisition-to-hire journey (AC-HCM-33; `Journey` / `JourneyProdLike`) |
| `src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs` | PLAN-33: G-02 behaviour with `IsLabScale=false` and the policy engine Off |

## Validation

- `scripts/hcm001-validate.ps1`
- `scripts/hcm029-validate.ps1` (PLAN-29)
- `scripts/hcm030-validate.ps1` (PLAN-30)
- `Hcm001RecruitmentTests` — behaviour tests on the HR.Core.Domain rules
- `scripts/hcm026-validate.ps1` — `Hcm026*` unit tests + `Hcm021|Hcm022` regression
- `scripts/hcm027-validate.ps1` — `Hcm027*` unit tests + `Hcm026|Hcm022` regression
- `scripts/hcm028-validate.ps1` — `Hcm028*` unit tests + `Hcm027|Hcm026|Hcm022|Hcm021|Hcm006|Wf023` regression, EF model check
