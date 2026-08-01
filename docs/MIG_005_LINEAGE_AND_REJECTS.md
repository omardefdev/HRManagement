# MIG-005 — Lineage and rejects

Consumes MIG-003 contracts:

- Per-record lineage fields — [MIG_003_RECORD_LINEAGE.md](MIG_003_RECORD_LINEAGE.md)
- Quarantine / reject register — pipeline stage `quarantine`
- Pipeline run ledger — checkpoint/idempotent load evidence

Evidence packages must include `lineage-export` and `reject-register` artifacts. Rejected bucket counts must reconcile to quarantine register rows for the wave.
