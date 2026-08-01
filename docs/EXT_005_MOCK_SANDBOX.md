# EXT-005 — Mock / sandbox

| Rule | Value |
|---|---|
| labStub | true |
| liveBusForbidden | true |
| mockCatalog / mockRegistry | true |

Harness ledger: `testdata/ext005/harness/api-exchange-run-ledger.json` — every run `liveBus: false`.

Validate: `scripts/ext005-api-exchange-validate.ps1`.

Partner CDC certification product = EXT-006 (`partner-cdc-cert` Blocked).
