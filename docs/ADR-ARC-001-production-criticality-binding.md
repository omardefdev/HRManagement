# ADR-ARC-001 — Production criticality binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | ARC-001 |
| Date | 2026-07-30 |

## Decision

`ArcCriticality` loads `criticality-matrix.json` at startup. Staging/Production set `EnforceOnStartup=true` and `LiveCriticalityClaim=false`. Incomplete classification, Tier-0/1 SPOF, targets without telemetry, missing recovery order, or `LiveCriticalityClaim=true` fail startup/CI.

## Consequences

- Downstream cards SRE-002 / BCP-001 / PHY-001 may treat ARC-001 as Satisfied (criticality binding; live drills later).
- ARC-002 Done separately as production reference-architecture binding (not live two-site). PHY-001 / SRE-002 / BCP-001 / CAP-001 stay open; `National24x7Claim` / `LiveVendorClaim` / `LiveCriticalityClaim` unchanged.
