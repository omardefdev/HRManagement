# STR-006 — Simulation pack

Card verification triad: bus-factor, staffing/on-call, TCO.

## 1. Bus-factor

| Fixture | expectedValidate |
|---|---|
| `scenarios/bus-factor-pass.json` | PASS |
| `scenarios/bus-factor-fail.json` | FAIL |

## 2. Staffing / on-call

| Fixture | expectedValidate | note |
|---|---|---|
| `scenarios/staffing-oncall.json` | PASS | `national24x7Claim: false`; empty coverageGaps |

## 3. TCO

| Fixture | expectedValidate |
|---|---|
| `scenarios/tco-dev-only.json` | FAIL |
| `scenarios/tco-ops-security-backup.json` | PASS |

## 4. Vendor (supporting)

| Fixture | expectedValidate |
|---|---|
| `vendors/missing-exit-clause.json` | FAIL |

Walkthroughs align with BUS_FACTOR_EXERCISE, STAFFING_ONCALL, TCO_APPROVAL, VENDOR_SLA.
