# MIG-003 — Idempotent load

| Rule | Value |
|---|---|
| batchKeyFields | tenantId, pipelineId, batchId |
| naturalKeyFields | tenantId, sourceId, sourceRecordKey |
| reloadPolicy | skip-identical |
| duplicateOnReloadForbidden | true |

## Behavior

1. First load of a batch records ledger entry Completed.
2. Reload of the same batch keys with identical content → skip (no duplicate platform rows).
3. Reload with drifted content → fail-on-drift (documented for later worker; harness asserts no silent duplicate).
4. Fixture: `testdata/mig003/scenarios/same-batch-reload.json` → PASS when `duplicatesCreated: 0`.
