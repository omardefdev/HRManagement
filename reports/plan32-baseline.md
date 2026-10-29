# PLAN-32 — Baseline (P0)

**Date:** 2026-10-29
**Branch:** `feature/plan-32-recruitment-analytics`, created from `main` @ `bcc582a` (PLAN-26..31 merged).

## Build

`dotnet build HRManagement.slnx` → **0 errors**, 4 existing warnings (NU1903 SSH.NET).

## Unit tests

Filter of the prompt, run as `FullyQualifiedName~` clauses:
`Hcm02|Hcm030|Hcm031|Reports|Str007|Ana001|Prod006|Perf004`

| Run | Total | Passed | Failed | Skipped |
|-----|-------|--------|--------|---------|
| Baseline filter | 1008 | 1008 | 0 | 0 |

No failure in the filter. The known environment-only failures of the full unit suite (ar-SA locale with the Um
Al-Qura calendar, no `pwsh`) are the set recorded in `reports/plan31-baseline.md` and `reports/plan31-final-report.md` §3
(`Str005ProductionBindingTests` ×3, `Str005GovernancePackTests.Exceptions_have_expiry_and_are_not_past_asOf`,
`LearningCompletionRulesTests.Certificate_Number_Is_Deterministic_And_Formatted`,
`Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50`, `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider`,
`Ten002ArchitecturalTests.IgnoreQueryFilters_only_in_sanctioned_bypass`, `San001SanctionsTests.Validate_script_passes`,
`BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard`) plus the occasional
`Hcm029CandidateAcceptedV4Tests` flake, and in integration tests `Hcm028AppealGovernanceMigrationTests` (formats a date
with the current culture, Hijri year). None of them is in the filter above. Any failure outside this list after a
PLAN-32 change is a regression.

## Validators

| Script | Result |
|--------|--------|
| `scripts/str007-value-kpi-validate.ps1` | PASS (10 KPIs) |
| `scripts/ana001-validate.ps1` | PASS |
| `scripts/prod006-semantic-layer-validate.ps1` | PASS |

The validators rewrite `testdata/*/out/*.json`; those files are reverted (`git checkout -- testdata`) before every commit.
