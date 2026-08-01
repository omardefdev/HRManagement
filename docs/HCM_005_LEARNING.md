# HCM-005 — Learning, Talent, Succession, and Career Development

## Purpose

Model skills taxonomy, learning enrollments, verifiable certificates, career development paths, and talent/succession pools with privacy controls and budget reconciliation.

## Scope

- Skills taxonomy linked to job catalog
- Skill gap analysis (current vs. required proficiency)
- Learning plans, course catalog, and provider management
- Enrollment/attendance/completion tracking
- Verifiable certificates and credential validation
- Training budget allocation and reconciliation
- Career paths and internal opportunity matching
- Talent and succession pool management (restricted access)
- Privacy controls on talent classifications

## Domain Model

### SkillRecord Entity

Tracks an employee's proficiency in a specific skill from the taxonomy.

```
SkillRecord
├── Id (Guid)
├── TenantId (Guid)
├── EmployeeId (Guid)
├── SkillCode (string) — taxonomy code
├── SkillName (string)
├── Category (enum) — Technical, Managerial, Behavioral, Legal, Language, Specialized
├── ProficiencyLevel (int) — current level (1-5)
├── RequiredLevel (int?) — position requirement
├── AssessedDate (DateTime?)
├── AssessmentSource (string?) — self/manager/certification/test
├── CertificateId (string?) — verifiable credential ID
├── CertificateExpiry (DateTime?)
├── JobCatalogEntryId (Guid?) — FK to job requiring this skill
├── CreatedAt / UpdatedAt
```

### LearningEnrollment Entity

Tracks enrollment in a learning/training activity with budget and completion data.

```
LearningEnrollment
├── Id (Guid)
├── TenantId (Guid)
├── EmployeeId (Guid)
├── CourseCode (string) — course identifier
├── CourseName (string)
├── ProviderName (string?) — external provider
├── Type (enum) — Training, Workshop, Conference, Certification, ELearning, OnTheJob, Mentoring
├── Status (enum) — Nominated, Approved, InProgress, Completed, Failed, Cancelled, Withdrawn
├── EnrollmentDate (DateTime)
├── StartDate / CompletionDate (DateTime?)
├── BudgetAmount (decimal?) — allocated budget (SYP)
├── ActualCost (decimal?) — incurred cost
├── Score (decimal?) — completion grade
├── CertificateId (string?) — completion certificate
├── TargetSkillCodes (string?) — skills targeted
├── ApprovedByManagerId (Guid?) — approving manager
├── CreatedAt / UpdatedAt
```

## Business Rules

- Every skill is linked to a job catalog entry via SkillCode
- Certificates must be verifiable (CertificateId traceable to provider)
- Nomination and cost require manager approval (ApprovedByManagerId)
- Succession pool access is restricted to authorized roles only
- Results reconcile with attendance records and training budget

## Artifacts

| File | Purpose |
|------|---------|
| `docs/HCM_005_LEARNING.md` | This document |
| `SharedKernel/Talent/SkillRecord.cs` | Skill record domain entity |
| `SharedKernel/Talent/LearningEnrollment.cs` | Learning enrollment domain entity |
| `scripts/hcm005-validate.ps1` | Validates HCM-005 artifacts |
| `Hcm005LearningTests.cs` | Unit tests for entities, schema, and validation |

## Acceptance Criteria Mapping

- [x] Skill linked to job — JobCatalogEntryId + SkillCode
- [x] Certificate verifiable — CertificateId + expiry validation
- [x] Nomination/cost audited — ApprovedByManagerId + BudgetAmount
- [x] Succession access restricted — TenantId scoping + role-based access
- [x] Results reconcile with attendance and budget — IsWithinBudget check + status tracking

## Validation

- `scripts/hcm005-validate.ps1`
- `Hcm005LearningTests`
