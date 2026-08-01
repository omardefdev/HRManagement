# ADR-PROD-006 — National semantic layer / statutory reports (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-006 (card 85) |
| Date | 2026-07-30 |
| Dependencies | GOV-003/FUN-006/PERF-004/TEN-003 Satisfied |

## Context

No signed contract for unified KPI definitions, source reconciliation, tenant RLS rollups, privacy thresholds, report provenance (version/as-of/source), freshness/lineage — without claiming live warehouse or BI dashboards.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-semantic/semantic-layer-matrix.json` |
| Depth | Lab semantic-layer gate pack + harness; not live BI |
| Soft blockers | live-warehouse / live-bi-dashboard / live-open-data-feed Blocked; ANA-001/REP-001 NotStarted; PROD-007 Satisfied |
| Closure | Pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| PROD-006a | Live warehouse/lakehouse |
| PROD-006b | Live semantic metrics engine |
| PROD-006c | Live BI dashboard / LiveDashboardClaim |
| PROD-006d | ANA-001 / REP-001 join |
| PROD-006e | Open-data feed / PROD-007 |
