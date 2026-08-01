# MIG-006 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 71 | ADR-MIG-006 | |
| R2 | ≥3 timed rehearsals | REHEARSAL_PLAYBOOK + fixture | |
| R3 | change freeze | CHANGE_FREEZE | |
| R4 | delta no loss / loss FAIL | DELTA_CAPTURE | |
| R5 | surprise rollback exercised | ROLLBACK | |
| R6 | paper rollback FAIL | paper-rollback-fail | |
| R7 | Go/No-Go criteria | GO_NOGO | |
| R8 | legacy read-only | legacy-readonly-pass | |
| R9 | recon T+0/T+1/T+30 | POST_CUTOVER_RECON | |
| R10 | Soft blockers Blocked | agency-extract, live-cutover-perf, live-agency-recon, live-rehearsal-at-scale | |
| R11 | Hard deps Satisfied | MIG-005, OPS-003, REL-001 | |
| R12 | Validate PASS | mig006-cutover-validate.ps1 | |
