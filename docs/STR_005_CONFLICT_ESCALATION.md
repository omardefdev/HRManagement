# STR-005 — Conflict escalation

Aligns with `conflictEscalation` in [`str-governance/governance-matrix.json`](str-governance/governance-matrix.json).

## Levels

| Level | Owner | When |
|---|---|---|
| 1 | Board chair of owning board | Disagreement within Architecture / Security / Data / Change / Program |
| 2 | Program board | Cross-board deadlock; **SAN-001** supply/legal classification escalates here |
| 3 | Sponsor | Unresolved after Program; seats named when **SYD-017** / file 14 close |

## Rules

- Escalation must create or update a row in `decision-log.json` with type matching the conflict (often `ExceptionGrant` or `ArchitectureVariance`).
- Linked risk row required in `risk-register.json`.
- No silent override by engineering alone.
- File 14 Syria Go-Live decisions remain authoritative for national wave seats; this pack defines role quorum until names exist.

## Out of scope

Live ticketing workflow; PGM-001 schedule arbitration beyond role RACI.
