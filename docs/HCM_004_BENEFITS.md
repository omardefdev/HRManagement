# HCM-004 — Benefits, Pension, and Social Insurance

## Purpose

Model benefit plans, pension enrollments, and social insurance contributions with historical reproducibility, provider reconciliation, and retroactive law handling.

## Scope

- Eligibility rules per legal regime, grade, and service duration
- Benefit plans: health insurance, social insurance, pension, allowances
- Dependents and life events (marriage, birth, death, disability)
- Employee and employer contribution calculations
- Pension service credit and continuity tracking
- Provider exchange: contribution files, reconciliation, rejections
- Retroactive law changes and re-calculation
- Annual enrollment windows and mid-year life events
- Benefit statements with explainable calculations
- Appeals and correction workflows

## Domain Model

### BenefitPlan Entity

Represents a configured benefit/insurance plan with eligibility rules, contribution rates, and effective dates.

```
BenefitPlan
├── Id (Guid)
├── TenantId (Guid)
├── PlanCode (string) — unique plan identifier
├── PlanName (string) — display name
├── PlanType (enum) — Health, Pension, SocialInsurance, Allowance, Other
├── ProviderName (string?) — external provider/institution
├── EmployeeContributionRate (decimal) — percentage
├── EmployerContributionRate (decimal) — percentage
├── CeilingAmount (decimal?) — contribution ceiling
├── EligibilityMinServiceMonths (int) — minimum service for eligibility
├── LegalSourceId (string?) — law/decree reference (SYR-001)
├── EffectiveFrom (DateTime)
├── EffectiveTo (DateTime?)
├── IsActive (bool)
├── CreatedAt / UpdatedAt
```

### PensionEnrollment Entity

Tracks an employee's enrollment in a pension/insurance plan with service credit and contribution history.

```
PensionEnrollment
├── Id (Guid)
├── TenantId (Guid)
├── EmployeeId (Guid) — FK to Employee
├── BenefitPlanId (Guid) — FK to BenefitPlan
├── EnrollmentDate (DateTime)
├── TerminationDate (DateTime?)
├── ServiceCreditMonths (int) — accumulated months
├── ContributionBalance (decimal) — running total
├── BeneficiaryInfo (string?) — JSON: dependents/beneficiaries
├── Status (enum) — Active, Suspended, Terminated, Retired
├── LegalRegimeId (string?) — SYR-003 regime identifier
├── CreatedAt / UpdatedAt
```

## Reconciliation

- Monthly contribution totals reconcile with Payroll (`PAY-002`) and provider statements
- No duplicate coverage across plans for same benefit type
- Every law/configuration change is versioned with effective date
- Benefit statements are explainable: each line traces to a rule and legal source

## Artifacts

| File | Purpose |
|------|---------|
| `docs/HCM_004_BENEFITS.md` | This document |
| `SharedKernel/Benefits/BenefitPlan.cs` | Benefit plan domain entity |
| `SharedKernel/Benefits/PensionEnrollment.cs` | Pension enrollment domain entity |
| `scripts/hcm004-validate.ps1` | Validates HCM-004 artifacts |
| `Hcm004BenefitsTests.cs` | Unit tests for entities, schema, and validation |

## Acceptance Criteria Mapping

- [x] Historical calculation reproducible — versioned plans with effective dates
- [x] Totals reconcile with Payroll and provider — contribution balance tracking
- [x] No duplicate coverage — plan type uniqueness per employee
- [x] Every law/config versioned — `LegalSourceId` + `EffectiveFrom/To`
- [x] Benefit statement explainable — trace to plan, rate, and legal source

## Validation

- `scripts/hcm004-validate.ps1`
- `Hcm004BenefitsTests`
