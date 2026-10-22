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

1. **File** — HR on the candidate's behalf (`FiledBy = HrOnBehalf`; `Candidate` is reserved for PLAN-30), reason of
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
| `UpheldNoRemedy` | stays `Rejected`; audited `AppealUpheldNoRemedy` (compliance alert is PLAN-30) |

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
as a warning, never re-hashed. Candidate download (PLAN-30) and File.Storage archiving are follow-ups.

### Appointment decision and medical step (G-22, D-6)

The hire checklist gains «إصدار قرار التعيين» (`HireChecklist:RequireAppointmentDecision`, default on) and «الفحص
الطبي قبل التعيين» (`HireChecklist:RequireMedicalFitness`, default off); completion and the hire saga require them.
Overdue commencement (`Recruitment:CommencementDeadlineDays` in HR.Contracts, default 30) is flagged and opens an
intervention (detection only). Details: [HIRE_ONBOARDING_GUIDE](HIRE_ONBOARDING_GUIDE.md).

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
- [x] PLAN-28 G-10 — committee votes, per-candidate COI, decisions, lock, server-side quorum/SoD, minutes; approval gates ranking (D-5)
- [x] PLAN-28 G-09 — one appeal path: independent employee-identified compliance reviewer, explicit outcomes, info requests; legacy reopen removed
- [x] PLAN-28 G-05 (appeal part) — compliance reaches the appeal pages and its inbox items
- [x] PLAN-28 — rejection kind/date recorded on every rejection; appeal window from `RejectedAt`; no hire while an appeal is pending
- [x] PLAN-29 G-12 — no duplicate person on hire, national-id mismatch justified and audited, no 500 on the hire form; employment type is a domain value
- [x] PLAN-29 G-13 — `ICandidateAccepted` v4 offer terms → contract (type by service regime, grade, salary) → `IContractActivated` → pay profile; explicit lineage columns; internal mobility opens no Appointment
- [x] PLAN-29 G-22 — appointment decision step (D-6), optional pre-employment medical, overdue-commencement intervention
- [x] PLAN-29 G-18 (offer letter) — official offer letter PDF with a verification hash fixed at issue

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
| `scripts/hcm028-validate.ps1` | Validates PLAN-28 artifacts, migrations, access slice and `Hcm028` tests |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealService.cs` | Appeal filing, reviewer assignment, decision, info request |
| `src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentAppealsController.cs` | Appeal pages (HR and compliance reviewer) |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm28CommitteeAppealTests.cs` | AC-HCM-28a..i acceptance |
| `scripts/hcm029-validate.ps1` | Validates PLAN-29 artifacts, migrations (HR.Core + HR.Contracts), config defaults and `Hcm029`/`Fun029`/`Pay029` tests |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/HirePersonMatchService.cs` | Person matching at hire |
| `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/OfferLetterDocumentService.cs` | Offer letter data and verification hash |
| `src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/ContractTypeResolver.cs` | Contract type by service regime / employment type |
| `src/Tests/HRManagement.AcceptanceTests/AcHcm29HireContractTests.cs` | AC-HCM-29a..i acceptance |

## Validation

- `scripts/hcm001-validate.ps1`
- `scripts/hcm029-validate.ps1` (PLAN-29)
- `Hcm001RecruitmentTests`
- `scripts/hcm026-validate.ps1` — `Hcm026*` unit tests + `Hcm021|Hcm022` regression
- `scripts/hcm027-validate.ps1` — `Hcm027*` unit tests + `Hcm026|Hcm022` regression
- `scripts/hcm028-validate.ps1` — `Hcm028*` unit tests + `Hcm027|Hcm026|Hcm022|Hcm021|Hcm006|Wf023` regression, EF model check
