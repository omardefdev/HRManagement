# EXT-002 — Unavailable mode

| Field | Value |
|---|---|
| degradeBehavior | fail-closed-manual |
| forbidUnverifiedAsTruth | true |

When the registry is down, the system must not invent a Match. Manual/hold path is allowed; treating outage as verified truth → FAIL (`outage-as-truth-fail`). Controlled degrade → PASS (`unavailable-degrade-pass`).
