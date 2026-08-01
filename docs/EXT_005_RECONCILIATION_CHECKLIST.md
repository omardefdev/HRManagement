# EXT-005 — Reconciliation checklist

| Check | Pass? | Evidence |
|---|---|---|
| Matrix card EXT-005 + 10 journeys | | api-exchange-matrix.json |
| IAM-004 / TEN-003 / INT-002 Satisfied | | blockers |
| PROD-007 Satisfied (platform binding); live bus/registry Blocked | | blockers |
| FAIL fixtures (owner/breaking/shared-db/cdc/purpose/schema/deprecation) | | testdata/ext005/scenarios |
| PASS fixtures (n1/throttle/interop) | | testdata/ext005/scenarios |
| exchange audit PASS | | audit/exchange-audit-pass.json |
| ledger liveBus=false | | harness/api-exchange-run-ledger.json |
| ext005-api-exchange-validate.ps1 | | PASS |

Sign-off: API Governance + Integration Architecture.
