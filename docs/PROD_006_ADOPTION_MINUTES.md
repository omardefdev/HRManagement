# PROD-006 — Adoption minutes (production semantic-layer binding)

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | PROD-006 |
| Decision | Adopt lab semantic-layer matrix as startup/CI production binding |

## Claims and promotion gates

| Claim | Requires |
|---|---|
| `LiveSemanticLayerClaim=true` | PROD-006a + live-warehouse Satisfied |
| Live semantic metrics engine | PROD-006b |
| `LiveDashboardClaim=true` (STR-007) | PROD-006c + live-bi-dashboard Satisfied |
| ANA-001 / REP-001 | PROD-006d |
| Open-data feed / PROD-007 | PROD-006e |

## Cross-card sync

- PROD-005 workbox matrix: PROD-006 → Satisfied
- STR-007: LiveDashboardClaim forbidden until PROD-006c / live-bi-dashboard (binding Done does not enable live BI)

## Notes

ANA-001 and REP-001 remain NotStarted. PROD-007 is Satisfied as production extensibility-platform binding (open-data live feed remains Blocked). WorkforcePlanning approximate headcount remains an ANA-001 gap, not closed by this binding.
