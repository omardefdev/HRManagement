# MIG-005 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 70 | ADR-MIG-005 | |
| R2 | 3 accounted buckets + reasons | ACCOUNTED_BUCKETS | |
| R3 | full accounted PASS / gap FAIL | scenarios | |
| R4 | financial unexplained FAIL | FINANCIAL_CONTROLS | |
| R5 | dual control enforced | DUAL_CONTROL | |
| R6 | evidence immutable + artifacts | EVIDENCE_PACKAGE | |
| R7 | checksum recon PASS | checksum-recon | |
| R8 | business + statistical samples | SAMPLING | |
| R9 | agency wave sign-off lab | AGENCY_WAVE_SIGNOFF | |
| R10 | Soft blockers Blocked | agency-extract, live-agency-recon, live-independent-sql | |
| R11 | Hard deps Satisfied | MIG-003, OBS-003 | |
| R12 | Validate PASS | mig005-evidence-validate.ps1 | |
