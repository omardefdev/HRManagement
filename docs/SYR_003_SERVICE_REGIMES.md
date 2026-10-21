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

## Runtime binding (PLAN-SYR-06)

Persisted in `hr_core_db` and wired to SYR-001 + HCM hire:

| Artifact | Notes |
|----------|-------|
| `LegalRegimes` | global; PK `LegalRegimeId`; `LegislationRef` resolved to `ResolvedLegalSourceId` |
| `BenefitPlans` | tenant-scoped HCM-004 plans with `LegalSourceId` FK |
| `EmploymentRecord.LegalRegimeId` | set at hire from offer legal source (`SYR-LAW-50-2004` → `law50`) |
| `IServiceRegimeProvider` | read provider for UI and validation |
| Seed | `ServiceRegimeSeeder` + `BenefitPlanSeeder` after legislation register |

## Validation

- `scripts/syr003-validate.ps1`
- `Syr003ServiceRegimeTests`
- `Syr006ServiceRegimeBindingTests`

## Exception queue + transitions (PLAN-SYR-07)

Runtime enforcement for SYR-003 exception handling and payroll bridge:

| Artifact | Notes |
|----------|-------|
| `ServiceRegimeExceptions` | `UnclassifiedRegime` queue; idempotent open row per employee+type |
| `EmploymentRegimeTransitions` | append-only; validates `allowed_transitions` from `service-regimes.json` |
| `ServiceRegimeExceptionService` | enqueue on hire/backfill; resolve applies transition + closes exception |
| `ServiceRegimeTransitionService` | requires `TransitionDecisionRef`; updates active employment record |
| `ServiceRegimeToPayrollMapper` | HRCore `law50`/`law53` → Payroll `LAW_50_2004`/`LAW_53_2021` |
| Payroll gate | `blocks_final_payroll` enforced via HRCore S2S blockers + `PayrollRegimeGovernance` |
| UI | exception queue index/resolve; transition history on employee details |

Migrations: `SYR007_RegimeGovernance` (HRCore), `SYR007_EmployeePayProfileRegimeCode` (Payroll).

Tests: `Syr007RegimeGovernanceTests.cs`. Validate: `scripts/syr003-validate.ps1`.

## Movement regime binding (PLAN-SYR-08)

Links FUN-003 movements (transfer/secondment/loan) to SYR-003 service regimes and Article 159 plumbing:

| Artifact | Notes |
|----------|-------|
| `ServiceRegimeMovementRules` | loads `movement_types[]` from `service-regimes.json`; `preserves_regime` / `may_change_regime` |
| `TransferRequest` | `SourceLegalRegimeId`, `TargetLegalRegimeId`, `TransitionDecisionRef`, `ConditionCode`, `ConflictNotes` |
| `MovementRegimeValidationService` | pre-approve/execute validation; cross-regime requires decision ref + allowed transition |
| `IHrCoreActiveRegimeClient` | S2S `POST /api/internal/service-regime/active-regimes` for regime snapshot |
| `TransferExecutedConsumer` | intra-regime copies `LegalRegimeId`; cross-regime calls `ServiceRegimeTransitionService` |
| `SecondmentExecutedConsumer` / `LoanAssignmentExecutedConsumer` | assert regime preservation |
| `ITransferExecuted` v3 | carries regime fields + `PayrollRegimeCode`; Payroll updates `EmployeePayProfile.RegimeCode` |
| UI | transfer create/edit regime fields; transition history + exception resolve show `ConflictNotes` |

Migration: `SYR008_MovementRegimeBinding` (HR.Movements). Tests: `Syr008MovementRegimeTests.cs`, `HRCoreTransferExecutedConsumerTests`.

## Regime lifecycle completion (PLAN-SYR-09)

Completes SYR-003 runtime gaps after movement binding: Article 159 structured workflow, separation/EOS bridge, promotion, and delegation (`ندب`).

| Artifact | Notes |
|----------|-------|
| JSON pack v2 | `promotion_rules[]`, `separation_rules[]`, `article_159_patterns[]` in `service-regimes.json` |
| Rule loaders | `ServiceRegimePromotionRules`, `ServiceRegimeSeparationRules`, `ServiceRegimeArticle159Rules` |
| Article 159 | `ServiceRegimeExceptionType.Article159Conflict`; `LegalDecisionId` + `ConflictingEntitlementsJson`; GOV-002 resolve UI |
| Separation | `SeparationRecord.SourceLegalRegimeId` / `PayrollRegimeCode`; `SeparationRegimeValidationService`; saga carries regime |
| EOS bridge | `ISeparationCompleted` v3 + `IExecuteSeparationParty` v3; `PayrollRegimeSeed.CreateLaw53Configuration`; EOS by `PayrollRegimeCode` |
| Promotion | `PromotionRequest` + `MovementKind.Promotion` + `IPromotionExecuted` + `PromotionRegimeValidationService` |
| Delegation | `DelegationRequest` + `MovementKind.Delegation` + `IDelegationExecuted`; `preserves_regime: true` |
| Naming | `secondment`=انتداب, `loan`=إعارة, `delegation`=ندب |

Migrations: `SYR009_Article159Governance` (HRCore), `SYR009_SeparationRegimeBinding` (Contracts), `SYR009_PromotionDelegationMovement` (Movements).

Tests: `Syr009RegimeLifecycleTests.cs`. Validate: `scripts/syr003-validate.ps1` PLAN-SYR-09 block.
