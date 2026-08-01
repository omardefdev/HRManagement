# EXT-004 — Reconciliation checklist

| Check | Pass? | Evidence |
|---|---|---|
| Matrix card EXT-004 + 10 journeys | | pki-signature-matrix.json |
| STR-002 / OBS-003 Satisfied | | blockers |
| SOV-003 Satisfied (key-sovereignty binding); live PKI/HSM/TSA/SYD-011 Blocked | | blockers |
| FAIL fixtures (role/revoke/expired/clock/exfil/alg/form) | | testdata/ext004/scenarios |
| PASS fixtures (LTV/TSA outage/archive) | | testdata/ext004/scenarios |
| signature audit PASS | | audit/signature-audit-pass.json |
| ledger livePki=false | | harness/pki-run-ledger.json |
| ext004-pki-signature-validate.ps1 | | PASS |

Sign-off: PKI + Legal + Security.
