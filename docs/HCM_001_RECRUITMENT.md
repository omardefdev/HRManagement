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

## Validation

- `scripts/hcm001-validate.ps1`
- `Hcm001RecruitmentTests`
