# ADR-PROD-006 — Production semantic-layer binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-006 (card 85) |
| Date | 2026-07-30 |
| Kind | Production binding (not live warehouse / BI) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/prod-semantic/semantic-layer-matrix.json` |
| Runtime | `ProdSemantic` in ServiceDefaults; `AddProdSemantic()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveSemanticLayerClaim=false` until PROD-006a–e |
| Soft blockers | GOV-003/FUN-006/PERF-004/TEN-003 Satisfied; live-warehouse / live-bi-dashboard / live-open-data-feed Blocked; ANA-001/REP-001 NotStarted; PROD-007 Satisfied (extensibility-platform binding) |
| Cross-card | PROD-005 workbox matrix: PROD-006 → Satisfied; STR-007 LiveDashboardClaim retargeted to PROD-006c / live-bi-dashboard |

## Consequences

- Staging/Production fail-fast on incomplete semantic-layer contracts or live claim.
- Live warehouse, live metrics engine, and live BI dashboard remain out of scope.
- ANA-001 / REP-001 stay NotStarted and must not be claimed Done by this binding. PROD-007 Satisfied as platform binding does not enable open-data live feed.
- WorkforcePlanning headcount approximation remains an ANA-001 gap.
