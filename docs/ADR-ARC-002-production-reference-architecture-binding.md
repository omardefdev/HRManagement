# ADR-ARC-002 — Production reference-architecture binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | ARC-002 |
| Date | 2026-07-30 |

## Decision

`ArcReference` loads `reference-architecture-matrix.json` at startup. Staging/Production set `EnforceOnStartup=true` and `LiveReferenceClaim=false`. Missing owners, artifact drift, unsupported topology, cross-boundary DB/domain coupling, or `LiveReferenceClaim=true` fail startup/CI.

## Consequences

- ARC-001 criticality blocker `ARC-002` → Satisfied (platform reference binding; live two-site = ARC-002a).
- QA-005 / PHY-001 / SCM-001 / CAP-001 stay open; `LiveCriticalityClaim` / `National24x7Claim` unchanged.
