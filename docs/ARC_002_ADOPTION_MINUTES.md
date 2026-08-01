# ARC-002 Adoption Minutes

| Field | Value |
|---|---|
| Card | ARC-002 |
| Decision | Adopt production reference-architecture binding |
| Date | 2026-07-30 |

## Claim gates

| Claim | Requires |
|---|---|
| `LiveReferenceClaim=true` | ARC-002a + live-two-site-deployment Satisfied |
| Fitness envelope live | ARC-002b + live-architecture-fitness-envelope |
| Coupling scan live | ARC-002c + live-coupling-boundary-scan |
| Reference signoff live | ARC-002d + live-reference-implementation-signoff |
| Join QA-005 / PHY | ARC-002e |

ARC-001 blocker `ARC-002` is Satisfied (platform reference binding). This does **not** authorize live two-site deploy, raise `LiveCriticalityClaim`, or close QA-005 / PHY-001 / CAP-001.
