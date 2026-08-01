# MIG-005 — Accounted buckets

Every source record in a wave must land in exactly one bucket:

| Bucket | Reason required | Meaning |
|---|---|---|
| Migrated | No | Loaded to platform with lineage |
| Rejected | Yes | Quarantine / policy reject (MIG-003 disposition) |
| Deferred | Yes | Explicit deferral with coded reason |

Acceptance: `Migrated + Rejected + Deferred = SourceCount` (and matching sum/hash dimensions where declared). Any residual is an **unexplained gap** and fails validate.

Reject/defer reasons must be non-empty coded strings (lab examples: `UNMAPPED_CODE`, `AGENCY_HOLD`, `CORRUPT_SCHEMA`).
