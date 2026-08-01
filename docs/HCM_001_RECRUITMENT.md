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

## Acceptance Criteria Mapping

- [x] No announcement without funded position — `PositionId` required on requisition
- [x] Assessment criteria versioned and auditable — score/rank tracked per application
- [x] Committee and conflicts audited — `ConflictOfInterestDeclared` field
- [x] Candidate tracks case — status progression with timestamps
- [x] Conversion idempotent — `IsConvertedToEmployee` flag prevents re-entry

## Artifacts

| File | Purpose |
|------|---------|
| `docs/HCM_001_RECRUITMENT.md` | This document |
| `SharedKernel/Recruitment/RecruitmentRequisition.cs` | Requisition domain entity |
| `SharedKernel/Recruitment/CandidateApplication.cs` | Application domain entity |
| `SharedKernel/Recruitment/OfferLetter.cs` | Offer letter domain entity |
| `scripts/hcm001-validate.ps1` | Validates HCM-001 artifacts |
| `Hcm001RecruitmentTests.cs` | Unit tests |

## Validation

- `scripts/hcm001-validate.ps1`
- `Hcm001RecruitmentTests`
