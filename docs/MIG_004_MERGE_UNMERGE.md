# MIG-004 — Merge / unmerge

## Survivor rules

- Prefer newer employment record when conflict.
- Prefer complete NationalId.
- Forbid silent field overwrite without evidenceRef.

## Audit (required)

`mergedAt`, `actor`, `evidenceRef`, `survivorId`, `sourceId`, `reversible=true`.

Every merge must be dated and reversible. Unmerge restores source from Merged status when legal hold allows.

## Interim runtime join

Operational merge/unmerge in HR.Core follows [GOV_003_MERGE_RUNBOOK.md](GOV_003_MERGE_RUNBOOK.md) (`EmployeeLifecycle` merge/unmerge + `MasterDataDuplicateCase`). This pack does **not** rewrite that path and is **not** MDM-002 golden person.
