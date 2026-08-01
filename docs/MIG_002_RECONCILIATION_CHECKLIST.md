# MIG-002 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 67 | ADR-MIG-002 | |
| R2 | 5 canonical entities | CANONICAL_MODEL | |
| R3 | ≥10 codeMaps with disposition | CODE_MAPS | |
| R4 | requiredCoverageDomains mapped | coverage fixture | |
| R5 | forbidOther + max rate | UNMAPPED_QUARANTINE | |
| R6 | legacy id reference-only | LEGACY_ID_POLICY | |
| R7 | locale Asia/Damascus | LOCALE_CALENDAR | |
| R8 | round-trip samples | PASS | |
| R9 | legal/business sign-off | PASS | |
| R10 | Soft blockers Blocked | agency-extract, SYR-003, MDM-001 | |
| R11 | Hard deps Satisfied | MIG-001, TEN-001, GOV-003 | |
| R12 | Validate PASS | mig002-code-map-validate.ps1 | |
