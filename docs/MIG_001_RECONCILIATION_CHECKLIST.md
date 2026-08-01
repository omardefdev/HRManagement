# MIG-001 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 66 | ADR-MIG-001 | |
| R2 | ≥6 sources; universe covered | source-inventory | |
| R3 | requiredTargetFields لها SoT | FIELD_SOT | |
| R4 | Inventoried volumes measured | estimateOnly=false | |
| R5 | accessRisks documented | ACCESS_RISKS | |
| R6 | freeze constraints listed | FREEZE_CONSTRAINTS | |
| R7 | Unsupported + signed exception | UNSUPPORTED + exceptions | |
| R8 | sampling + counts fixtures | testdata/mig001 | |
| R9 | agency-signed profiling | PASS | |
| R10 | Soft blockers Blocked | agency-extract, SYR-002 | |
| R11 | Hard deps Satisfied | STR-001, GOV-003 | |
| R12 | Validate PASS | mig001-source-inventory-validate.ps1 | |

**Soft blockers:** agency-extract-access, SYR-002.
