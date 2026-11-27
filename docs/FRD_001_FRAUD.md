# FRD-001 — Ghost Employee Prevention & Continuous Anti-Fraud

**Card:** FRD-001
**Status:** Done
**Priority:** P0-National / XL
**Owner:** Internal Audit + Payroll + Data Quality + SOC

## Overview

Establishes continuous anti-fraud controls for the national payroll: physical/biometric headcount verification before each wave, periodic proof-of-life for pensioners and beneficiaries, cross-agency duplicate employment detection at the person level, continuous civil registry death feed integration, and a per-cycle anomaly engine. Fraud alerts create payment holds that require four-eyes release.

## Anomaly Rules

The machine-readable anomaly rule set is at `docs/frd/anomaly-rules.json`.

### Detection Categories

| ID | Category | Description |
|----|----------|-------------|
| FR-01 | Ghost employee | Payroll entry with no attendance or physical verification |
| FR-02 | Deceased beneficiary | Continued payment after civil registry death notification |
| FR-03 | Duplicate employment | Same person on multiple agency payrolls without authorized dual-employment |
| FR-04 | Shared IBAN/account | Single bank account receiving payments for multiple unrelated beneficiaries |
| FR-05 | Anomalous compensation | Compensation amount outside expected range for grade/position |
| FR-06 | Single approver volume | One approver handling abnormally high number of payroll actions |
| FR-07 | Same-day hire-and-pay | Employee created and paid in the same payroll cycle |
| FR-08 | Missing source record | Payroll line with no traceable HR action or decision |

### Proof-of-Life Protocol

| Aspect | Requirement |
|--------|-------------|
| Frequency | Annually for pensioners; semi-annually for disability beneficiaries |
| Methods | In-person, biometric, notarized attestation |
| Grace period | 60 days from notification before automatic payment hold |
| Disability/remote exception | Documented exception with alternate verification method |
| Hold release | Four-eyes approval with evidence |

### Payment Hold Rules

- Every anomaly alert has an assigned owner and required resolution decision
- Hold cannot be released by a single operator
- Hold release requires documented reason and supporting evidence
- No hold can be closed by silent expiry — explicit decision required
- All hold/release actions recorded in EVD-001 evidence chain

## Reconciliation

- Three consecutive cycles with independent auditor anomaly report required
- Synthetic fraud cases injected in pre-production to measure detection rate
- Cross-reconciliation with GOSI, GOIP, and civil registry
- Fraud/waste metrics built from evidence, not parallel manual files

## Dependencies

MDM-002, MIG-004, IAM-003, EVD-001, OBS-003, SYR-006, SYR-009

## Artifacts

- `docs/frd/anomaly-rules.json`
- `scripts/frd001-validate.ps1`
- `Frd001FraudTests`

## PLAN-58: four eyes, and the rules that really run

- **Releases and resolutions are two people's acts.** Releasing a hold (`POST api/payroll/payment-holds/{id}/release`) and resolving a
  critical alert (`POST api/payroll/fraud-alerts/{id}/resolution-requests`) make a request (202, `Payroll.Approve`); another holder of
  `Payroll.Approve` decides it from his own session (`POST api/payroll/dual-control/{id}/decide`, or the inbox «طلبات بانتظار قرارك»).
  The requester never decides his own request (400); the decider is the caller, never a field of a body. A release of a hold resolves its
  linked alert. Requests expire after `Payroll:DualControl:ExpiryHours` (72) with no effect.
- **A hold holds its employee only.** The gate (`PaymentDisbursementGate`) holds the orders of an employee with an active hold of the run
  or with no run (every batch), an open critical alert of his, no pinned account, or a retired pinned account; the others are sent. Only a
  critical alert with no employee stops a batch.
- **The scan reads real data** (`RunPayrollFraudScanCommand`, `FraudScanFacts`): FR-01 from PLAN-57's attendance receipts of the run's
  period (absent days at least the period's working days = no attendance; no receipt = no fact); FR-04 from the accounts the lines
  pinned; FR-05 from the employee's net in the last posted regular run; FR-07 from the employee's first pay profile (`EffectiveFrom`);
  FR-08 from the run job's input snapshots, and a correction's lines carry their source run. **FR-04's action is now `payment-hold`**
  (`docs/frd/anomaly-rules.json`): two employees paid into one account are both held, the others sent.
- The scan runs in the same request as each sending (`process`, `submit-held`); a released fraud hold of a run is not raised again.
