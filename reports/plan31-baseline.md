# PLAN-31 — Baseline (P0)

**Date:** 2026-10-29
**Branch:** `feature/plan-31-competitions-exams`, created from the tip of `feature/plan-30-candidate-experience`
(`35d4efc`). PLAN-30 is **complete but not merged into `main`** (`main` is at `55fbcdd`), so PLAN-31 branches from the
PLAN-30 tip as the prompt allows.

## PLAN-30 state check

- `reports/plan30-final-report.md` exists (sessions 1–3), plus both session handoffs.
- `powershell -ExecutionPolicy Bypass -File scripts/hcm030-validate.ps1` → **`hcm030-validate: PASS`**
  (unit filter 559/559, a11y scan PASS on 10 views, ROPA OK: 14 services / 19 activities).

## Build

`dotnet build HRManagement.slnx` → **0 errors**, 11 existing warnings (NU1903 SSH.NET, CS0108, CS8604, MVC1004, CS9113,
xUnit2013).

## Unit tests

Filter of the prompt (run as `FullyQualifiedName~` clauses):
`Hcm02|Hcm030|Recruitment|Offer|Appeal|Reserve|Committee|PublicCareers`

| Run | Total | Passed | Failed | Skipped |
|-----|-------|--------|--------|---------|
| Baseline filter | 910 | 909 | 1 | 0 |
| Full unit suite | 3685 | 3675 | 10 | 0 |

The single filter failure is `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50` (expects `Issued`, gets
`PendingApproval`) — pre-existing, recorded in `reports/plan30-baseline.md`.

The ten full-suite failures are exactly the environment-only set recorded in `reports/plan30-final-report.md` §6
(ar-SA locale with the Um Al-Qura calendar, no `pwsh`):

- `Str005ProductionBindingTests` ×3 (`GoLiveDeclared_passes_with_multi_seat_evidence`, `Runtime_validate_script_passes`,
  `Startup_fails_on_expired_declared_exception`)
- `Str005GovernancePackTests.Exceptions_have_expiry_and_are_not_past_asOf`
- `LearningCompletionRulesTests.Certificate_Number_Is_Deterministic_And_Formatted`
- `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50`
- `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider`
- `Ten002ArchitecturalTests.IgnoreQueryFilters_only_in_sanctioned_bypass`
- `San001SanctionsTests.Validate_script_passes`
- `BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard`

Any failure outside this list after a PLAN-31 change is a regression.
