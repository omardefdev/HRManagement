# SYR-001 — Syrian Legislation Repository & Legal Source Binding

**Card:** SYR-001  
**Status:** Done  
**Priority:** P0-National / XL  
**Owner:** Legal Product Owner + Ministry Legal Teams

## Overview

Establishes a structured, version-controlled repository of Syrian legislation that
governs HR/payroll/leave/pension/tax decisions. Every rule, calculation, and policy
in the system must trace back to a specific legal source (law, decree, regulation)
with article reference, effective date, and certified text.

## Scope

### Primary Legislation

| Law/Decree | Subject | Status |
|------------|---------|--------|
| **Law 50/2004** | Basic Employment Law (قانون العاملين الأساسي) | Active with amendments |
| **Law 53/2021** | Labor Code for private/mixed sector | Active within scope |
| **Decree 67/2026** | Wage reform tables | Active |
| **Decree 68/2026** | Compensation reform | Active |
| **Decree 135/2026** | Social insurance amendments | Active |
| **Decree 188/2025** | Fixed public holidays | Active |
| **Law 12/2024** | Personal Data Protection | Active |

### Supporting Instruments

- Executive instructions (تعليمات تنفيذية)
- Ministerial decisions
- Official Gazette (الجريدة الرسمية) publications
- Special regime regulations (أنظمة خاصة)

## Data Model

### Legislation Register

See `docs/syr/legislation-register.json` for the structured register with hierarchy,
effective dates, and source binding.

Each legislation entry contains:

| Field | Description |
|-------|-------------|
| `legalSourceId` | Unique identifier for the legal source |
| `type` | law / decree / regulation / instruction / decision |
| `number` | Official number (e.g. "50/2004") |
| `titleAr` | Arabic title |
| `titleEn` | English title (optional) |
| `issueDate` | Date of issuance |
| `effectiveDate` | Date of effect |
| `publicationDate` | Official Gazette publication date |
| `status` | active / amended / repealed / superseded |
| `amendedBy` | List of amending instruments |
| `repeals` | List of repealed provisions |
| `articles` | Structured article references |
| `certifiedTextHash` | SHA-256 hash of the certified text |
| `owner` | Legal custodian |

### Source Binding

See `docs/syr/legal-source-binding.md` — every system rule must reference a
`legal_source_id` with article and effective date.

## Business Rules

1. **No rule without source** — every HR/payroll/leave/pension/tax rule must have a `legal_source_id`, article reference, effective date, and legal owner.
2. **No press-source rules** — rules cannot be based solely on press reports; certified text required.
3. **Conflict visibility** — conflicting provisions are surfaced, not auto-resolved by code.
4. **Change notification** — legislative changes produce impact reports before taking effect.
5. **Immutable history** — past legal versions are never modified; only new versions appended.
6. **Hash verification** — certified text packages are hash-verified for integrity.

## Verification

- Independent sample tracing: hire, promotion, leave, salary, dismissal, pension → legal text
- Retroactive amendment and repeal scenarios
- Conflict/overlap between two laws
- Hash and signature verification of legal package

## Dependencies

STR-002, GOV-002, LEG-001

## Files

- `docs/syr/legislation-register.json` — structured legislation register (seed pack for PLAN-SYR-02)
- `docs/syr/legal-source-binding.md` — binding protocol
- `scripts/syr001-validate.ps1` — artifact validation
- `Syr001LegislationTests.cs` — xUnit tests (JSON artifacts)
- `Syr001LegislationSchemaTests.cs` — xUnit tests (DB schema, PLAN-SYR-01)

### Runtime database (PLAN-SYR-01)

National legislation is persisted in **`hr_core_db`** as platform-global tables (no `TenantId`):

| Table | Role |
|-------|------|
| `LegislationSources` | PK `LegalSourceId` (e.g. `SYR-LAW-50-2004`) |
| `LegislationKeyArticles` | Key articles per source |
| `LegislationInstrumentLinks` | `AmendedBy` / `Repeals` links |
| `LegislationSourceAliases` | Alternate ids (e.g. SYR-003 `law50_2004`) |

FK (nullable, `Restrict`): `RecruitmentRequisitions.LegalSourceId`, `OfferLetters.LegalSourceId` → `LegislationSources`.

Migration: `SYR001_LegislationRegister`.

### Runtime seed (PLAN-SYR-02)

National legislation rows are loaded at startup from the canonical JSON pack:

| Item | Location |
|------|----------|
| Source JSON | `docs/syr/legislation-register.json` |
| Deployed pack | `seed-packs/syr001-legislation-v1/register.json` (linked in `HR.Core.Infrastructure.csproj`) |
| Seeder | `LegislationRegisterSeeder.SeedAsync` |
| Orchestrator | `NationalSeedOrchestrator` (mandatory + incremental paths) |
| Acceptance | `AcceptanceDataSeeder` seeds legislation before agency fixtures; Br1 requisition/offers use `SYR-LAW-50-2004` |

SYR-003 aliases seeded: `law50_2004` → `SYR-LAW-50-2004`, `law53_2021` → `SYR-LAW-53-2021`.

Orphan instrument links (targets not in register, e.g. `SYR-LAW-17-2010`) are skipped to preserve FK integrity.

Tests: `Syr001LegislationSeedTests.cs`. Validate: `scripts/syr001-validate.ps1`.

### Runtime provider (PLAN-SYR-03)

Read-only access to seeded legislation via EF (no JSON reload at runtime):

| Item | Location |
|------|----------|
| Interface | `ILegislationRegisterProvider` (`SharedKernel/Legal`) |
| DTOs | `LegislationSourceSummaryDto`, `LegislationSourceDetailDto`, `LegislationKeyArticleDto`, `LegislationInstrumentLinkDto` |
| Implementation | `LegislationRegisterProvider` (`HR.Core.Infrastructure/Services/Legal`) |
| DI | `AddHrLegislationRegister()` in `Program.cs` (Scoped) |

Key methods: `ListActiveAsync`, `GetByIdAsync`, `ResolveAliasAsync`, `GetDetailAsync`, `GetKeyArticlesAsync`, `ExistsAsync`.

Consumers (PLAN-SYR-04): recruitment dropdown, submit validation, display labels on requisitions/offers.

Tests: `Syr001LegislationProviderTests.cs`, `Syr001ProductionBindingTests.cs`.

### Runtime HCM binding (PLAN-SYR-04)

Recruitment requisition legal source UX and validation:

| Item | Behavior |
|------|----------|
| Create/Edit UI | `<select>` populated from `ILegislationRegisterProvider.ListActiveAsync()` |
| Draft save | `LegalSourceId` optional; if set must exist and be `active` |
| Submit | `RecruitmentLegalSourceValidator.ValidateRequiredAsync` — required + active |
| Publish | defensive guard if `LegalSourceId` missing |
| Details | shows `DisplayLabel` (`50/2004 — title`) not raw PK |
| Offer Details | read-only `DisplayLabel` when `LegalSourceId` present |

Validator: `RecruitmentLegalSourceValidator`. Rules/messages: `RecruitmentLegalSourceRules`.

Tests: `Syr001RecruitmentLegalSourceTests.cs`, `RecruitmentLegalSourceRulesTests.cs`.

### Runtime offer + hire binding (PLAN-SYR-05)

Offer letter legal source lineage through hire closure:

| Item | Behavior |
|------|----------|
| Create offer | copies `LegalSourceId` from requisition; blocks if requisition has no legal source |
| Offer submit | `ValidateRequiredAsync` with `RequiredOnOfferSubmitMessage` — required + active |
| Hire | required on `OfferLetter`; `LineageMatches` with requisition when both set |
| Application Details | read-only inherited label; warning when requisition has no legal source |
| Offer Details | read-only `DisplayLabel` with inheritance note |

Tests: `Syr001OfferLegalSourceTests.cs`, updated `RecruitmentHireClosureIntegrationTests.cs`.

### Runtime service regimes + benefits binding (PLAN-SYR-06)

SYR-003 runtime tables in `hr_core_db` bound to SYR-001 and HCM hire lineage:

| Item | Behavior |
|------|----------|
| `LegalRegimes` | global register seeded from `service-regimes.json`; `ResolvedLegalSourceId` via alias resolution |
| `BenefitPlans` | tenant-scoped; `LegalSourceId` FK → `LegislationSources` |
| Hire | `OfferLetter.LegalSourceId` → `EmploymentRecord.LegalRegimeId` (`law50` / `law53`) |
| Provider | `IServiceRegimeProvider` + `ServiceRegimeValidator` |
| UI | read-only regime label on employee/employment records; benefit plans index |

Tests: `Syr006ServiceRegimeBindingTests.cs`, updated hire integration tests.

### Exception queue + payroll bridge (PLAN-SYR-07)

SYR-003 governance runtime for unclassified regimes, transition history, and payroll blocking:

| Item | Behavior |
|------|----------|
| `ServiceRegimeExceptions` | tenant queue for unclassified/missing regime (`Open` → `Resolved`/`Dismissed`) |
| `EmploymentRegimeTransitions` | append-only history with required `TransitionDecisionRef` |
| Hire/backfill | enqueue when `LegalRegimeId` is null after mapping |
| Payroll bridge | `law50`→`LAW_50_2004`, `law53`→`LAW_53_2021` on `IEmployeeCreated` v6 + `EmployeePayProfile.RegimeCode` |
| Payroll block | S2S `POST /api/internal/service-regime/payroll-blockers`; gate on `Regular`/`FinalSettlement` runs |

Migrations: `SYR007_RegimeGovernance`, `SYR007_EmployeePayProfileRegimeCode`.

Tests: `Syr007RegimeGovernanceTests.cs`, updated hire integration tests. Validate: `scripts/syr001-validate.ps1`.

### Movement regime binding (PLAN-SYR-08)

Cross-service lineage for FUN-003 movements and SYR-003 regimes:

| Item | Behavior |
|------|----------|
| Active regime S2S | HRCore `POST /api/internal/service-regime/active-regimes` used by HR.Movements validation |
| Transfer cross-regime | requires `TransitionDecisionRef` + allowed transition from `service-regimes.json` |
| Regime continuity | `TransferExecutedConsumer` copies or transitions regime; no active record without `LegalRegimeId` |
| Payroll bridge on transfer | `ITransferExecuted` v3 `PayrollRegimeCode` → `EmployeePayProfile.RegimeCode` |
| Article 159 | optional `ConflictNotes` on cross-regime transfer + `EmploymentRegimeTransition` + exception resolve |

Migration: `SYR008_MovementRegimeBinding`. Tests: `Syr008MovementRegimeTests.cs`.

### Regime lifecycle completion (PLAN-SYR-09)

Article 159 structured workflow and separation/EOS lineage tied to GOV-002 legal decisions:

| Item | Behavior |
|------|----------|
| Article 159 exception | `ServiceRegimeExceptionType.Article159Conflict`; auto-enqueue on cross-regime to special regime |
| GOV-002 resolve | `ResolveArticle159Async` requires `LegalDecisionId` from legal-decisions register |
| Separation regime | `SeparationRegimeValidationService` validates type per `separation_rules[]`; snapshots regime on publish |
| EOS config | `ISeparationCompleted` / `IExecuteSeparationParty` v3 carry `PayrollRegimeCode`; Payroll selects Law53/Law50 config |
| Promotion / delegation | regime-aware promotion validation; delegation preserves service regime (`ندب`) |

Migrations: `SYR009_Article159Governance`, `SYR009_SeparationRegimeBinding`, `SYR009_PromotionDelegationMovement`. Tests: `Syr009RegimeLifecycleTests.cs`.

