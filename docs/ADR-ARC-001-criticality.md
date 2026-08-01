# ADR-ARC-001 — Criticality contracts (pack)

| Field | Value |
|---|---|
| Status | Accepted (pack) |
| Card | ARC-001 |
| Date | 2026-07-30 |

## Decision

Source of truth is `docs/arc-criticality/criticality-matrix.json`. Lab contracts encode Tier-0/1/2 classification, journey dependency maps, shared platform deps, failure domains, numeric targets with telemetry, SPOF policy for Tier-0/1, recovery order, and graceful degradation. Live walkthrough/shutdown/recovery drills remain Blocked; `LiveCriticalityClaim=false`.
