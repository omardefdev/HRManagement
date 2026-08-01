# ANA-001 — National Analytics Platform and Governed Semantic Layer

## Purpose

Provide a governed national analytics platform with conformed dimensions, event ingestion, data lineage, quality/freshness monitoring, tenant-level row-level security, and a semantic layer with authoritative KPI definitions.

## Scope

- Warehouse/lakehouse architecture for national HR data
- Conformed and slowly-changing dimensions (SCD Type 2)
- Event and source system ingestion pipelines
- Late/out-of-order data handling
- Data lineage and provenance tracking
- Data quality and freshness monitoring
- Tenant-scoped row-level security (RLS)
- Full rebuild capability with reconciliation
- Semantic layer with governed measures and KPIs
- National authority aggregate permissions (separate from operational)
- Privacy controls on analytical data

## Semantic Layer

Defined in `docs/ana/semantic-layer.json`:

- Measures: headcount, FTE, vacancy rate, payroll total, turnover rate, average tenure
- Dimensions: agency, governorate, grade, legal regime, gender, age band, service band
- Grain: monthly snapshot with as-of date support
- KPIs have owner, definition, grain, and as-of semantics
- National aggregates require separate permission grant

## Business Rules

- Headcount/positions/payroll must use exact counts (no approximations)
- Every KPI has a definition, owner, grain, and as-of date
- Totals must reconcile with operational source systems
- Rebuild produces identical results (deterministic)
- National aggregate permission is separate from operational tenant access
- Late data is handled via event timestamps, not arrival time

## Artifacts

| File | Purpose |
|------|---------|
| `docs/ANA_001_ANALYTICS.md` | This document |
| `docs/ana/semantic-layer.json` | Semantic layer definitions |
| `scripts/ana001-validate.ps1` | Validates ANA-001 artifacts |
| `Ana001AnalyticsTests.cs` | Unit tests for semantic layer and validation |

## Acceptance Criteria Mapping

- [x] No approximate counting — exact measures in semantic layer
- [x] KPI definition/owner/grain/as-of — semantic-layer.json structure
- [x] Totals reconcile — reconciliation rules in semantic layer
- [x] Rebuild identical — deterministic pipeline design
- [x] National aggregate separate — permission model in semantic layer

## Validation

- `scripts/ana001-validate.ps1`
- `Ana001AnalyticsTests`
