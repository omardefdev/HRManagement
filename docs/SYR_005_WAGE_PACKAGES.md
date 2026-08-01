# SYR-005 — Dated Wage, Compensation, and Tax Rule Packs

## Purpose

Define versioned, signed rule packs for Decrees 67/2026 and 68/2026 wage tables, compensation restructuring, income tax (effective brackets only), deductions, rounding, and entitlement dimensions. Separate base wage from compensation and taxable/insurable bases.

## Scope

- Wage tables from Decree 67/2026 and compensation rules from Decree 68/2026
- Qualitative increases and abolished/continuing allowances
- Remote and semi-remote area allowances
- Minimum wage and promotion bonus provisions
- Effective-only income tax brackets (no proposed reform before enactment)
- Deductions and rounding rules (no float arithmetic)
- Agency/job title/legal regime entitlement dimensions
- Excluded categories (Law 53 carve-outs, qualitative increase beneficiaries)
- Four-eyes governance, simulation, diff, and rollback policy

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_005_WAGE_PACKAGES.md` | This document |
| `docs/syr/decree-67-68-wage-tables.json` | Wage/compensation/tax rule pack structure |
| `scripts/syr005-validate.ps1` | Validates SYR-005 artifacts and structure |
| `Syr005WageTests.cs` | Unit tests for wage pack schema and validation |

## Key Policies

- **Separation:** `baseWage`, `compensationTotal`, `taxableBase`, and `insurableBase` are distinct fields
- **No double stacking:** Decree 67/68 benefits must not stack unlawfully
- **Proposed tax reform:** Forbidden until effective date is certified
- **Governance:** Ministry of Finance signed rule pack; four-eyes; simulation + diff before activation
- **Rollback:** Stops payment but preserves historical results

## Acceptance Criteria Mapping

- [x] Signed rule pack from Ministry of Finance
- [x] Base wage separated from compensation and taxable/insurable bases
- [x] No application of proposed tax reform before enactment
- [x] Prevent unlawful 67/68 double stacking and excluded categories
- [x] Simulation and diff before activation; four-eyes; rollback policy defined
- [x] Three shadow cycles required before production activation

## Validation

- `scripts/syr005-validate.ps1`
- `Syr005WageTests`
