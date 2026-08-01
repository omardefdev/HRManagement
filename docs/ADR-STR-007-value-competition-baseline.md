# ADR-STR-007 — Value KPIs, competition baseline, and measurement gates

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-007 (card 65) |
| Date | 2026-07-30 |
| Dependencies | STR-001, STR-003, GOV-003 Satisfied |

## Context

STR-001/003 define envelope and modules. GOV-003 provides DQ/duplicate sources. There is no national value KPI catalog with definition/source/owner/target/anti-gaming, no pre-pilot baseline freeze, no ban on screen-count benefit metrics, and no dashboard tenant/privacy gate for value reporting. Dev.Analytics KPI CRUD is out of scope for this pack.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/str-value/kpi-matrix.json` |
| Depth | **Value/baseline gate pack** — docs + fixtures + validate; not live BI (PROD-006) |
| KPIs | Hire/separation/payroll cycle, self-service, DQ errors, CSAT, uptime (OBS J1), cost/employee stub, onboarding, adoption |
| Baseline | Required rows with `prePilotFreeze` before REL-002 pilot start |
| Anti-gaming | Per-KPI rules; owner ≠ auditor |
| Forbidden benefits | screenCount, pageCount, widgetCount |
| Privacy | tenantScoped + minCellSize; no cross-tenant rollup without waiver |
| Soft blockers | FIN-001=NotStarted; PROD-003=Blocked (ESS portals) |
| Closure | Done (value/baseline gate pack) + documented blockers |
| Out of scope | KPI.cs/Dashboard runtime changes; FIN-001 TCO; PROD-006 engine; live pilot |

## Blocker register

| Id | Status | Note |
|---|---|---|
| STR-001 | Satisfied | Envelope Done |
| STR-003 | Satisfied | Module catalog Done |
| GOV-003 | Satisfied | Master data / DQ Done |
| FIN-001 | NotStarted | cost_per_employee SoT later |
| PROD-003 | Blocked | Self-service portals; formula+fixture only |

## Consequences

- NAT-005 / UXR-001 / TRN-002 consume these KPI definitions and targets.
- Validate fails on missing fields, owner==auditor, missing baseline freeze, screen-count benefits, or cross-tenant leak fixtures marked PASS.
