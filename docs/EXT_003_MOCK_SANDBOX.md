# EXT-003 — Mock / sandbox

| Rule | Value |
|---|---|
| labStub | true |
| liveTreasuryForbidden | true |
| portJoin | IPayrollDisbursementGateway |

Adapters in scope: File/Stub only (`FilePayrollDisbursementGateway`). Live Mode reserved for Level-3 / EXT-003a.

Harness ledger: `testdata/ext003/harness/treasury-run-ledger.json` — every run must have `liveTreasury: false`.

Validate script: `scripts/ext003-treasury-payment-validate.ps1`.
