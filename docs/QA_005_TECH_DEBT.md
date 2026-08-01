# QA-005 — Technical Debt Baseline and Maintainability

## Purpose

Establish an automated, CI-enforced technical debt baseline covering cyclomatic complexity, file/method size, duplication, dead code, blocking warnings, and circular dependencies. Prevent metric degradation on diff without signed waiver.

## Scope

- Measured baseline (not manual reports): complexity, file size, method size, duplication, dead code, warnings, circular dependencies
- Debt budget: zero new debt points per PR without signed time-limited waiver
- SharedKernel/ServiceDefaults consolidation policy for duplicated business logic
- Architecture fitness tests run continuously in CI (not one-time at adoption)
- Debt register with owner and dates
- New developer onboarding exercise on critical path (target: 4 hours)

## Artifacts

| File | Purpose |
|------|---------|
| `docs/QA_005_TECH_DEBT.md` | This document |
| `docs/qa/debt-baseline.json` | Debt baseline metrics and thresholds |
| `scripts/qa005-validate.ps1` | Validates QA-005 artifacts and structure |
| `Qa005TechDebtTests.cs` | Unit tests for debt baseline schema and validation |

## Metrics Summary

| Metric | Threshold | Enforce on Diff |
|--------|-----------|-----------------|
| Cyclomatic complexity (method) | ≤ 15 | Yes |
| File length | ≤ 500 lines | Yes |
| Method length | ≤ 60 lines | Yes |
| Duplication | ≤ 5% | Yes |
| Blocking warnings | 0 | Yes (protected branches) |
| Circular dependencies | 0 | Yes |

## Acceptance Criteria Mapping

- [x] Baseline published and measured automatically in CI
- [x] No debt increase on diff without signed time-limited exception
- [x] Zero blocking-classified warnings on protected branches
- [x] No duplicated business logic without documented ownership decision
- [x] Zero circular dependencies between layers and services
- [x] New developer critical-path exercise target defined

## Validation

- `scripts/qa005-validate.ps1`
- `Qa005TechDebtTests`

## Dependencies

- QA-001, QA-002, ARC-002 (Satisfied — production reference binding), SCM-001
