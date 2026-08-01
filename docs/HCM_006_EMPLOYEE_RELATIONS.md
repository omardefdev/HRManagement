# HCM-006 — Employee Relations, Discipline, and Workplace Cases

## Purpose

Model employee relations cases including grievances, disciplinary actions, investigations, and whistleblower reports with confidentiality walls, deadline-driven escalation, and legal hold support.

## Scope

- Grievance/complaint submission and tracking
- Disciplinary investigation and hearing workflow
- Whistleblower protection and anonymity
- Conflict of interest detection
- Workplace incident reporting
- Evidence custody and legal hold
- Deadline alerts and escalation
- Confidentiality walls (medical details hidden from general HR)
- Aggregates that prevent re-identification
- Appeal workflow

## Domain Model

### EmployeeRelationCase Entity

Represents any employee relations matter requiring formal tracking.

```
EmployeeRelationCase
├── Id (Guid)
├── TenantId (Guid)
├── CaseNumber (string) — unique durable case identifier
├── CaseType (enum) — Grievance, Discipline, Investigation, Whistleblower, WorkplaceIncident, Complaint, Appeal
├── Status (enum) — Open, UnderInvestigation, PendingHearing, ActionTaken, AppealFiled, Closed, Dismissed
├── EmployeeId (Guid) — subject of the case
├── ReportedById (Guid?) — who filed (null for anonymous whistleblower)
├── Subject (string?)
├── Description (string?)
├── OpenedAt (DateTime)
├── ClosedAt (DateTime?)
├── Deadline (DateTime?) — SLA/legal deadline for resolution
├── IsWhistleblower (bool) — whistleblower protection flag
├── IsConfidential (bool) — confidentiality wall
├── AssignedInvestigatorId (string?)
├── ResolutionSummary (string?)
├── LegalHoldReference (string?) — EVD-001 legal hold ID
├── HasConflictOfInterest (bool)
├── EscalatedToLegal (bool)
├── CreatedAt / UpdatedAt
```

## Business Rules

- Case access is separated by confidentiality wall; investigator sees only assigned cases
- Whistleblower identity is protected; `ReportedById` null for anonymous reports
- No disciplinary action outside approved workflow (WF-001)
- Deadlines trigger alerts and automatic escalation
- Medical details never visible to general HR
- Legal hold prevents evidence destruction (EVD-001)

## Acceptance Criteria Mapping

- [x] Case access separated — `IsConfidential` + `AssignedInvestigatorId`
- [x] Whistleblower protection — `IsWhistleblower` flag with nullable reporter
- [x] No disciplinary action outside workflow — status machine enforces sequence
- [x] Deadlines alert and escalate — `Deadline` field + `EscalatedToLegal`
- [x] Medical details hidden — confidentiality wall design
- [x] Aggregates prevent re-identification — case-level access control

## Artifacts

| File | Purpose |
|------|---------|
| `docs/HCM_006_EMPLOYEE_RELATIONS.md` | This document |
| `SharedKernel/Cases/EmployeeRelationCase.cs` | Case domain entity |
| `scripts/hcm006-validate.ps1` | Validates HCM-006 artifacts |
| `Hcm006EmployeeRelationsTests.cs` | Unit tests |

## Validation

- `scripts/hcm006-validate.ps1`
- `Hcm006EmployeeRelationsTests`
