# MIG-004 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 69 | ADR-MIG-004 | |
| R2 | 4 match strategies | MATCH_STRATEGIES | |
| R3 | thresholds signed | THRESHOLDS + signoff | |
| R4 | national ID exceptions forbid auto | NATIONAL_ID_EXCEPTIONS | |
| R5 | high-risk auto-merge FAIL | scenario | |
| R6 | merge/unmerge roundtrip PASS | MERGE_UNMERGE | |
| R7 | cross-tenant review FAIL | REVIEW_PRIVACY | |
| R8 | labelled ≥20 + P/R ≥ targets | labelled + metrics | |
| R9 | adversarial no auto-merge | twins-name-variants | |
| R10 | Soft blockers set | agency-extract, L10N-002, live-labelled | |
| R11 | Hard deps Satisfied | MIG-002, DATA-002, GOV-001, GOV-003-merge | |
| R12 | Validate PASS | mig004-match-validate.ps1 | |
