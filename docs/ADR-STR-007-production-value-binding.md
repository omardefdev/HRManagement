# ADR-STR-007 — Value KPI production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-007 (card 65) |
| Date | 2026-07-30 |
| Supersedes | Value/baseline gate pack as insufficient for production |
| Dependencies | STR-006 production ownership binding |

## Context

The prior pack published the KPI matrix, baselines, privacy rules, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `ProgramValue` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/str-value/kpi-matrix.json` |
| Runtime | `ValueKpiOptions` (`ProgramValue`) + `IValueKpiProvider` + `IValueBenefitGuard` |
| KPIs | Required fields + owner ≠ auditor + anti-gaming |
| Baseline | `baselineRequired` KPIs need `prePilotFreeze` |
| Forbidden benefits | screenCount / pageCount / widgetCount |
| Privacy | tenantScoped + minCellSize + no cross-tenant rollup without waiver |
| Live dashboard | Default `LiveDashboardClaim=false`; claim forbidden until PROD-006c / live-bi-dashboard (PROD-006 binding Done does not enable live BI) |
| Soft blockers | FIN-001=NotStarted; PROD-003=Blocked; STR-001/003/GOV-003=Satisfied |
| Closure | **Done (production value binding)** |

## Consequences

- Staging/Production fail startup on incomplete KPI catalog, missing freeze, dishonest soft blockers, or live-dashboard claim.
- CI runs `str007-value-runtime-validate.ps1`.
- Live BI, FIN-001 TCO SoT, and ESS portals remain out of scope.
