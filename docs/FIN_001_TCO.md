# FIN-001 — Five-Year Total Cost of Ownership

## Purpose

Provide a comprehensive, auditable 5-year TCO model covering build, run, and decommission phases with per-unit cost metrics and alternative comparisons.

## Scope

- Build costs: development, testing, integration, migration, training, deployment
- Run costs: hosting, network, storage, backup, DR, security tools, licenses, support contracts
- People costs: SRE, DBA, SOC, support desk, management, training refreshment
- Growth scenarios: base, peak, and failure/recovery
- Per-unit costs: cost per employee, per agency, per payroll cycle, per TB stored
- Contingency and reserve allocation
- Alternative comparison: legacy system maintenance vs platform vs SaaS benchmarks
- Decommission costs: data migration, archive, destruction, vendor exit

## TCO Model

See `docs/fin/tco-model.json` for the structured cost model.

### Cost Categories

| Category | Year 1 | Year 2 | Year 3 | Year 4 | Year 5 |
|----------|--------|--------|--------|--------|--------|
| Build/Migration | High | Low | — | — | — |
| Infrastructure | Medium | Medium | Medium | Medium | Medium |
| Licenses | Medium | Medium | Medium | Medium | Medium |
| People/Operations | Medium | High | High | High | High |
| Support/Training | Medium | Medium | Medium | Low | Low |
| DR/Security | Medium | Medium | Medium | Medium | Medium |
| Contingency (15%) | Calculated | Calculated | Calculated | Calculated | Calculated |

### Sensitivity Analysis

- ±50% demand variation (agency count, employee growth)
- ±50% unit cost variation (hosting, licensing)
- Currency conversion impact (SYR-004 new lira transition)
- Vendor price escalation clauses

### Per-Unit Metrics

| Metric | Definition |
|--------|-----------|
| Cost per employee/month | Total annual run cost ÷ (avg employees × 12) |
| Cost per agency/month | Total annual run cost ÷ (agency count × 12) |
| Cost per payroll cycle | Payroll processing cost ÷ cycles per year |
| Cost per TB/month | Storage + backup + DR cost ÷ total TB |

## Artifacts

| File | Purpose |
|------|---------|
| `docs/FIN_001_TCO.md` | This document |
| `docs/fin/tco-model.json` | Structured 5-year cost model with scenarios |
| `scripts/fin001-validate.ps1` | Validates FIN-001 artifacts and model integrity |
| `Fin001TcoTests.cs` | Unit tests for TCO model schema and calculations |

## Acceptance Criteria Mapping

- [x] TCO base/peak/failure scenarios approved
- [x] Per-unit cost compares with alternatives
- [x] Funding covers operations and updates, not just launch
- [x] No hidden cost without owner
- [x] Independent finance review checklist included
- [x] Sensitivity ±50% demand/cost modeled

## Validation

- `scripts/fin001-validate.ps1`
- `Fin001TcoTests`
