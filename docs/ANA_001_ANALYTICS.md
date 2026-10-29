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

## Recruitment (HCM-032 / PLAN-32)

- **Measures** `recruitment_funnel`, `recruitment_time_to_fill`, `recruitment_time_to_hire`, `recruitment_offer_acceptance`,
  `recruitment_appeal_rate`, `recruitment_open_vacancies` are registered in `docs/ana/semantic-layer.json` with owner
  (product-hr), grain, as-of semantics (event time of the hire / outcome, report date for vacancies) and the privacy threshold
  (minimum group 5); their full definitions are the STR-007 KPIs of the same name (`docs/STR_007_KPI_DEFINITIONS.md`).
- **Exact counts** inside the tenant: the HR.Core report («تقارير التوظيف») computes every KPI live from the source tables
  (no sampling, no approximation); at 50,000 applications / 250,000 events the full report takes ≈ 1.2 s (scale test
  `Hcm032RecruitmentAnalyticsScaleTests`), so no snapshot table is kept.
- **Late data** by event time: timings use `RecruitmentApplicationEvent.OccurredAt`; rows older than the event log use
  documented evidence fallbacks and are counted separately (coverage shown with every KPI).
- **Publication to Dev.Analytics** (`IRecruitmentRequisitionClosed`, aggregates only, outbox): one
  `RecruitmentRequisitionFact` per closed or cancelled requisition (idempotent per requisition). The «التوظيف» card shows
  counts 1–4 as «<5» (small-cell suppression) and suppresses the median of fewer than five filled requisitions. National
  aggregation across tenants is not part of this card (operational tenant only).
- **Reconciliation:** the fact counts reconcile with the HR.Core report for the same requisition (applicants, hires, time to
  fill, channel and offer counts are computed by the same rules — `RecruitmentRequisitionClosedEvent`).

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
