# SYR-006 — Social Insurance, Insurance, and Pensions

## Purpose

Map the two independent Syrian institutions — General Organization for Social Insurance (GOSI) and General Organization for Insurance and Pensions (GOIP) — with jurisdiction rules that prevent routing a person to the wrong institution. Define certified rates, ceilings, beneficiary rules, and monthly reconciliation.

## Scope

- GOSI and GOIP as separate institutions with distinct jurisdictions
- Contribution, work injury, disability, survivor benefits (GOSI)
- Pension accrual, retirement, pension payment, beneficiaries (GOIP)
- Decree 135/2026 rates and retirement age provisions
- Jurisdiction routing rules (active → GOSI, retired → GOIP)
- Beneficiary deduplication and actionable rejection/variance states
- Monthly independent reconciliation with census (SYR-002)
- Bank/post payment channel settlement
- Law amendment produces new versioned pack, not manual patch

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_006_INSURANCE_PENSIONS.md` | This document |
| `docs/syr/insurance-jurisdiction-map.json` | Institution map and jurisdiction rules |
| `scripts/syr006-validate.ps1` | Validates SYR-006 artifacts and structure |
| `Syr006InsuranceTests.cs` | Unit tests for insurance map schema and validation |

## Jurisdiction Summary

| Status / Event | Route To | Forbidden |
|----------------|----------|-----------|
| Active employee contribution | GOSI | GOIP |
| Retired pension payment | GOIP | GOSI |
| Retirement referral | GOIP (after GOSI record) | — |
| Death benefit | GOSI (+ GOIP secondary) | — |

## Acceptance Criteria Mapping

- [x] Written jurisdiction map prevents wrong-institution routing
- [x] Rates/ceilings from certified dated text (Decree 135/2026)
- [x] Contribution/pension control totals defined for reconciliation
- [x] No duplicate beneficiary; rejections/variances return actionable states
- [x] Law amendment requires new versioned pack

## Validation

- `scripts/syr006-validate.ps1`
- `Syr006InsuranceTests`
