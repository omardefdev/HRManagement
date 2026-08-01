# EXT-004 — Mock / sandbox

| Rule | Value |
|---|---|
| labStub | true |
| livePkiForbidden | true |
| mockCa / mockTsa / mockHsm | true |

Harness ledger: `testdata/ext004/harness/pki-run-ledger.json` — every run `livePki: false`.

Validate: `scripts/ext004-pki-signature-validate.ps1`.

No live national CA, production HSM, or live TSA in this pack.
