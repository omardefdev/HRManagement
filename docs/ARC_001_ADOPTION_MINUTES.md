# ARC-001 Adoption Minutes

| Field | Value |
|---|---|
| Card | ARC-001 |
| Decision | Adopt production criticality binding |
| Date | 2026-07-30 |

## Claim gates

| Claim | Requires |
|---|---|
| `LiveCriticalityClaim=true` | ARC-001a + live-architecture-failure-walkthrough Satisfied |
| Controlled shutdown live | ARC-001b + live-controlled-dependency-shutdown |
| Recovery-order drill live | ARC-001c + live-recovery-order-drill |
| Full discovery live | ARC-001d + live-full-dependency-discovery |
| Join PHY-001 | ARC-001e |

ARC-002 is Done separately as production reference-architecture binding (not live two-site). This does **not** authorize live walkthrough/shutdown, raise `National24x7Claim`, or close PHY-001 / SRE-002 / BCP-001 / CAP-001.
