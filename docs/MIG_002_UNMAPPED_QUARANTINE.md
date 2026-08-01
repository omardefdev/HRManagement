# MIG-002 — Unmapped / quarantine

Policy from `unmappedPolicy` in code-map-matrix:

| Rule | Value |
|---|---|
| forbidOther | true |
| otherBucketMaxRate | 2% |
| quarantineQueueId | mig002-quarantine |
| forbiddenCatchAllValues | Other, OTHER, misc, unknown-dump |

## Rules

1. Every distinct legacy value in a covered domain must have disposition Map, Transform, or Quarantine.
2. Dumping unmapped codes into `Other` fails validate when rate exceeds `otherBucketMaxRate` or when catch-all values are used as target.
3. Quarantine rows keep provenance and await human resolve (MIG-003 loads quarantine queue; this pack only declares policy).
4. Fixture `unmapped-no-quarantine-fail.json` → FAIL; `mass-other-fail.json` → FAIL.
