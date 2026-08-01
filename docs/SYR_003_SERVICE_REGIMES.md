# SYR-003 — Public Service Legal Regimes (Law 50/2004 & Law 53/2021)

## Purpose

Model the Syrian public service legal regime system that classifies every employment relationship under Law 50/2004 (general public employees), Law 53/2021 (economic sector employees), or special/contractual regimes, with configurable rules per regime and tracked regime transitions.

## Scope

- `legal_regime_id` assignment for every employee (Law50, Law53, Special)
- Category/rank/position/establishment classification per regime
- Employment types: permanent, temporary, daily, seasonal, part-time, production, contractual
- Secondment (انتداب), loan (إعارة), transfer (نقل), delegation (ندب)
- Promotion rules per regime
- End of service rules and triggers
- Regime transition history with decision reference
- Article 159 cases and special regime configurability
- Exception queue for unclassified cases (blocks final payroll)

## Domain Model

### LegalRegime Entity

Represents a configured legal regime with its rules, scope, and effective period.

```
LegalRegime
├── Id (Guid)
├── TenantId (Guid)
├── LegalRegimeId (string) — regime identifier (e.g. "law50", "law53", "special_banking")
├── RegimeName (string) — display name
├── Type (enum) — Law50, Law53, Special
├── LegislationRef (string) — primary law/decree reference
├── AllowedEmploymentTypes (string?) — comma-separated types
├── GradeStructureId (string?) — linked grade/rank structure
├── HasPromotionRules (bool)
├── CoversPension (bool)
├── EffectiveFrom (DateTime)
├── EffectiveTo (DateTime?)
├── TransitionDecisionRef (string?) — decree/decision for regime change
├── IsActive (bool)
├── ConflictNotes (string?) — Article 159 conflict documentation
├── CreatedAt / UpdatedAt
```

## Regime Classification

| Type | Law | Scope |
|------|-----|-------|
| Law50 | Law 50/2004 | General public employees (ministries, governorates) |
| Law53 | Law 53/2021 | Economic sector (public companies, enterprises) |
| Special | Various decrees | Banking, judiciary, military, diplomatic, etc. |

## Employment Types

- Permanent (دائم) — standard indefinite appointment
- Temporary (مؤقت) — fixed-term contract
- Daily (يومي) — daily-wage workers
- Seasonal (موسمي) — seasonal employment
- PartTime (جزئي) — reduced hours
- Production (إنتاج) — production-based compensation
- Contractual (عقدي) — special contract terms

## Business Rules

- No employment relationship without an assigned legal regime and valid legal source
- No combining conflicting entitlements across regimes
- Regime transition preserves full history (never overwrites)
- Article 159 and special regime decisions are configurable (not hard-coded)
- Unclassified cases enter an exception queue and block final payroll
- Decision tables cover all patterns declared in Decree 67/2026

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_003_SERVICE_REGIMES.md` | This document |
| `docs/syr/service-regimes.json` | Regime definitions and decision rules |
| `SharedKernel/Legal/LegalRegime.cs` | Legal regime domain entity |
| `scripts/syr003-validate.ps1` | Validates SYR-003 artifacts |
| `Syr003ServiceRegimeTests.cs` | Unit tests for entities, schema, and validation |

## Acceptance Criteria Mapping

- [x] No employment without regime — LegalRegimeId required, LegislationRef required
- [x] No conflicting entitlements — Type enum prevents dual classification
- [x] Transition preserves history — TransitionDecisionRef + EffectiveFrom/To
- [x] Article 159 configurable — ConflictNotes + Special type
- [x] Unclassified blocks payroll — exception queue design (IsActive + validation)
- [x] Decree 67/2026 patterns covered — decision tables in service-regimes.json

## Validation

- `scripts/syr003-validate.ps1`
- `Syr003ServiceRegimeTests`
