# HCM-003 — Performance, Objectives, Competencies, and Appeals

## Purpose

Model performance evaluation cycles, individual reviews, rating calibration, and appeal processes with historical version preservation and analytics fairness controls.

## Scope

- Performance cycles with planning/review/calibration phases
- Objectives and competency assessments
- Self, manager, matrix, peer, and committee review types
- Calibration sessions with adjusted ratings
- Appeal/grievance workflows
- Performance Improvement Plans (PIP)
- Confidentiality controls on review data
- Historical version preservation
- Analytics fairness and bias detection

## Domain Model

### PerformanceCycle Entity

Defines a performance evaluation cycle with phases, scale, and template configuration.

```
PerformanceCycle
├── Id (Guid)
├── TenantId (Guid)
├── CycleCode (string) — unique cycle identifier
├── CycleName (string) — display name
├── Year (int) — fiscal/calendar year
├── Status (enum) — Draft, Active, InReview, Calibrating, Closed, Cancelled
├── PlanningStart / PlanningEnd (DateTime)
├── ReviewStart / ReviewEnd (DateTime)
├── CalibrationStart / CalibrationEnd (DateTime?)
├── RatingScaleMax (int) — scale upper bound (default 5)
├── ObjectivesRequired (bool)
├── CompetenciesIncluded (bool)
├── TemplateVersion (string?)
├── CreatedAt / UpdatedAt
```

### PerformanceReview Entity

An individual performance review within a cycle.

```
PerformanceReview
├── Id (Guid)
├── TenantId (Guid)
├── EmployeeId (Guid) — the employee being reviewed
├── PerformanceCycleId (Guid) — FK to PerformanceCycle
├── ReviewerId (Guid) — the person conducting the review
├── Type (enum) — Self, Manager, Matrix, Peer, Committee
├── Status (enum) — NotStarted, InProgress, Submitted, Completed, Acknowledged, Appealed
├── OverallRating (decimal?) — raw rating
├── ObjectivesScore (decimal?) — objectives achievement %
├── CompetenciesScore (decimal?) — competencies %
├── Comments (string?) — reviewer narrative (confidential)
├── SelfAssessment (string?) — employee self-assessment
├── SubmittedAt (DateTime?)
├── AcknowledgedAt (DateTime?)
├── HasAppeal (bool)
├── CalibratedRating (decimal?) — post-calibration adjustment
├── CreatedAt / UpdatedAt
```

## Business Rules

- Manager access is historically scoped (org structure as-of review date)
- Review forms and objectives are preserved as submitted (immutable after submission)
- Calibration and appeal are audited with full trail
- No automatic financial impact without an approved policy rule
- Bias/quality reviews supported via analytics fairness metrics

## Artifacts

| File | Purpose |
|------|---------|
| `docs/HCM_003_PERFORMANCE.md` | This document |
| `SharedKernel/Performance/PerformanceCycle.cs` | Cycle domain entity |
| `SharedKernel/Performance/PerformanceReview.cs` | Review domain entity |
| `scripts/hcm003-validate.ps1` | Validates HCM-003 artifacts |
| `Hcm003PerformanceTests.cs` | Unit tests for entities, schema, and validation |

## Acceptance Criteria Mapping

- [x] Manager historically scoped — ReviewerId + org structure as-of
- [x] Form/objectives preserved — immutable after Submitted status
- [x] Calibration and appeal audited — CalibratedRating + HasAppeal + status trail
- [x] No auto financial impact — no payroll coupling without explicit rule
- [x] Bias/quality reviews — analytics-ready scores with fairness design

## Validation

- `scripts/hcm003-validate.ps1`
- `Hcm003PerformanceTests`
