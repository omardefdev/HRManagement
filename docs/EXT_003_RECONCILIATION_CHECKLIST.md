# EXT-003 — Reconciliation checklist

| Check | Pass? | Evidence |
|---|---|---|
| Matrix card EXT-003 + 8 journeys | | treasury-payment-matrix.json |
| PAY-003 / IAM-003 / INT-003 Satisfied | | blockers |
| EXT-004 NotStarted; live channels Blocked | | blockers |
| unapproved / duplicate / totals / manual / cutoff FAIL fixtures | | testdata/ext003/scenarios |
| recon / partial-reject / ooo / delay / parallel PASS | | testdata/ext003/scenarios |
| payment-order audit PASS | | audit/payment-order-audit-pass.json |
| ledger liveTreasury=false | | harness/treasury-run-ledger.json |
| ext003-treasury-payment-validate.ps1 | | PASS |

Sign-off: Payroll + Finance Integration.
