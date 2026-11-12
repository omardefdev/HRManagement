# ESS-001 — Employee Service Center and Durable Cases

## Purpose

Provide a unified employee self-service center where every submission generates a durable Case ID with full lifecycle tracking, SLA enforcement, and status reconciliation with backend workflows.

## Scope

- Profile corrections and data updates
- Leave, advance, and document requests
- Payslip queries and benefit enquiries
- Learning enrollment and card replacement
- Durable Case ID that persists regardless of backend failures
- Timeline, status, owner, and SLA tracking
- Withdrawal and correction of submitted requests
- Evidence attachment
- Notifications and status updates to employee
- High-volume self-service journeys

## Domain Model

### EmployeeServiceCase Entity

Represents a durable service request with lifecycle tracking.

```
EmployeeServiceCase
├── Id (Guid)
├── TenantId (Guid)
├── CaseId (string) — durable, human-readable case identifier
├── EmployeeId (Guid) — requesting employee
├── CaseType (enum) — ProfileCorrection, LeaveRequest, AdvanceRequest, DocumentRequest, PayslipQuery, BenefitEnquiry, LearningEnrollment, CardReplacement, GeneralInquiry, Other
├── Status (enum) — Submitted, Acknowledged, InProgress, PendingApproval, Resolved, Closed, Withdrawn, Rejected
├── Subject (string?)
├── Description (string?)
├── SubmittedAt (DateTime)
├── AcknowledgedAt (DateTime?)
├── ResolvedAt (DateTime?)
├── ClosedAt (DateTime?)
├── SlaDeadline (DateTime) — computed from case type SLA rules
├── SlaBreached (bool) — true if deadline passed without resolution
├── AssignedTo (string?) — current case owner
├── WorkflowInstanceId (string?) — linked WF-001 instance
├── IsWithdrawn (bool) — employee withdrew the request
├── ResolutionNotes (string?)
├── CreatedAt / UpdatedAt
```

## Business Rules

- Every submit generates a durable Case immediately (before workflow processing)
- Backend failure never shows false success to employee
- Employee can only see own cases (tenant + employee scope)
- Case status reconciles with linked workflow instance
- SLA breach triggers escalation notification
- PLAN-51 (G-IAM-05): through `/ui/ess` (and any path with `/Ess` in it) an employee reaches only the named self-service controllers
  (`Gateway:Ess:AllowedControllers`: `EssPortal`, `EssTalent` on `ess` and `hr-core`; `Ess` on `payroll`), even when he also holds an
  HR.Core read or approval role; anything else under `/ui/ess` answers 404 at the gateway (`Denied:EssNotExposed`). The pages load
  HR.Core's static files (`Gateway.Authenticated`) and call no lookup API: names are rendered on the server, and the directory
  (`HrCore.Lookup`) is for staff and services, not for `hr-employee` alone. HR.Training's `EssLearning` keeps `Module.Ess` in the service;
  exposing it at the gateway is wave 2 (IAM-H), one configuration line.

## Acceptance Criteria Mapping

- [x] Every submit yields durable Case — `CaseId` generated at submission time
- [x] Backend failure does not show final success — status stays `Submitted` until acknowledged
- [x] Employee scope isolation — `EmployeeId` + `TenantId` filtering
- [x] Status reconciles with workflow — `WorkflowInstanceId` link
- [x] SLA enforcement — `SlaDeadline` + `SlaBreached` + `IsSlaMet` computed property

## Artifacts

| File | Purpose |
|------|---------|
| `docs/ESS_001_SERVICE_CENTER.md` | This document |
| `SharedKernel/Ess/EmployeeServiceCase.cs` | Service case domain entity |
| `scripts/ess001-validate.ps1` | Validates ESS-001 artifacts |
| `Ess001ServiceCenterTests.cs` | Unit tests |

## Validation

- `scripts/ess001-validate.ps1`
- `Ess001ServiceCenterTests`
