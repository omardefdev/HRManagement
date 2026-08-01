# STR-005 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 63 | ADR-STR-005 | |
| R2 | Matrix journeys = 4 | governance-matrix.json | |
| R3 | Five boards present | boards[] | |
| R4 | Every critical type has owner ≠ reviewer | decisionRights[] | |
| R5 | Decision log rows link to simulations / gate | decision-log.json | |
| R6 | Risk rows link to decisions | risk-register.json | |
| R7 | Exceptions declared + expiresOn | matrix.exceptions | |
| R8 | Law / incident / delay fixtures exist | testdata/str005/scenarios | |
| R9 | eng-only FAIL; multi-seat PASS | go-live fixtures | |
| R10 | Soft blockers SYD-017 Blocked; PGM-001 NotStarted | matrix.blockers | |
| R11 | Distinct from GOV-002 legal register | DECISION_LOG note | |
| R12 | CODEOWNERS covers str-governance / STR_005 / testdata | .github/CODEOWNERS | |
| R13 | Validate harness PASS | str005-governance-validate.ps1 | |

**Soft blockers (intentional):** SYD-017 (named signatories), PGM-001 (schedule pack later).
