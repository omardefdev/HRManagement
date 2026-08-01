# ADR-SOV-005 — Threat modeling contracts (pack)

| Field | Value |
|---|---|
| Status | Accepted (pack) |
| Card | SOV-005 |
| Date | 2026-07-30 |

## Decision

Source of truth is `docs/sov-threat/threat-model-matrix.json`. Lab contracts encode STRIDE methodology, trust boundaries, assets/actors, national threat categories, high-threat control/test/owner triad, change-trigger updates, misuse backlog linkage, and signed residual risk. Live independent TM review / attack-tree / misuse sync / residual signoff remain Blocked; `LiveThreatModelClaim=false`.
