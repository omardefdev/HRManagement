# EXT-002 — Cache and corrections

| Field | Value |
|---|---|
| ttlSeconds | 86400 (lab) |
| forbidStaleAsTruth | true |
| corrections.workflowRequired | true |

Expired cache must not be treated as verified identity (`stale-as-truth-fail`). Corrections require an explicit workflow (`correction-workflow-pass`).
