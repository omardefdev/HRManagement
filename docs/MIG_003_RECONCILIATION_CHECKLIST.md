# MIG-003 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 68 | ADR-MIG-003 | |
| R2 | stages extract→…→purge | pipeline-matrix.json | |
| R3 | ≥3 encrypted tenant-scoped zones | STAGING_ZONES | |
| R4 | idempotent reload policy | IDEMPOTENT_LOAD + same-batch-reload | |
| R5 | checkpoint / mid-fail resume | CHECKPOINT_RESUME + fixture | |
| R6 | per-record lineage ≥5 | RECORD_LINEAGE + lineage-sample | |
| R7 | no PII in log sample | PII_LOGGING + log-redaction | |
| R8 | cutover lab budget fits window | throughput + cutover-window-budget | |
| R9 | secrets + purge | SECRETS_AND_PURGE | |
| R10 | Soft blockers Blocked | agency-extract-access, live-cutover-perf | |
| R11 | Hard deps Satisfied | MIG-002, TEN-002, OPS-005 | |
| R12 | Validate PASS | mig003-factory-validate.ps1 | |
