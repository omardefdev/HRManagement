# MIG-003 — Per-record lineage

Required fields (`lineage.perRecordFields`):

| Field | Purpose |
|---|---|
| sourceId | MIG-001 source |
| sourceRecordKey | Legacy natural key (reference) |
| mappingVersion | MIG-002 mappingVersion |
| disposition | Map / Transform / Quarantine |
| loadBatchId | Batch identity |
| loadedAt | Timestamp |
| tenantId | TEN-002 scope |

Harness sample: `testdata/mig003/harness/lineage-sample.json` (≥5 records). Feeds MIG-005 evidence later.
