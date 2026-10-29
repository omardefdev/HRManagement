# REP-001 — Statutory Reports & Governed Self-Service BI

**Card:** REP-001  
**Status:** Done  
**Priority:** P2-National / L  
**Owner:** Reporting + Legal/Finance

## Purpose

Define the statutory report catalog with versions, effective dates, as-of semantics, scheduling, drill-through, export controls, governed ad-hoc access, retention, and background execution for Syrian national HR platform reporting.

## Scope

- Statutory report catalog with legal authority and golden datasets
- Version and effective-date tracking per report
- As-of date semantics for historic reproducibility
- Schedule and distribution policy
- Drill-through to source transactions
- Export controls and format policy
- Governed ad-hoc reporting with access matrix
- Report access and retention rules
- Background job execution for large reports

## Data Model

See `docs/rep/statutory-catalog.json` for the structured report catalog.

Each report entry contains:

| Field | Description |
|-------|-------------|
| `reportId` | Unique report identifier |
| `nameAr` / `nameEn` | Report titles |
| `legalAuthority` | Law/decree requiring the report |
| `version` | Current report definition version |
| `effectiveDate` | When this version takes effect |
| `goldenDataset` | Required semantic layer dataset |
| `grain` | Reporting grain (employee, agency, period) |
| `asOfRequired` | Whether as-of date is mandatory |
| `drillThrough` | Source entities for drill-down |
| `schedule` | Cron or period-based schedule |
| `retentionYears` | Legal retention period |
| `accessRoles` | Roles permitted to run/view |
| `backgroundJobThresholdRows` | Row count above which background execution required |

## Business Rules

1. **Golden dataset for every mandatory report** — no approximate counts for statutory figures.
2. **Historic reproducibility** — same as-of date produces identical figures.
3. **Figures drill to source** — every total traceable to transactions.
4. **No cross-tenant leakage** — tenant RLS enforced on all reports.
5. **Report carries version/as-of/source** — metadata embedded in output.
6. **Large reports run as background jobs** — threshold enforced.

## Relationship to ANA-001 / PROD-006

| Card | Focus |
|------|-------|
| PROD-006 | Production semantic-layer binding |
| ANA-001 | Analytics warehouse (later) |
| REP-001 | Statutory report catalog and governance |

## Recruitment reports (HCM-032 / PLAN-32)

«تقارير التوظيف» (`HR.Core RecruitmentReports/Index`, `HrCoreReadOnly`) is an operational report, not a statutory one; it
follows the same provenance and export rules:

- **Provenance:** every view and export states the period (UTC, half-open `[From, To)`), the scope (all units / the user's
  units / a unit, channel, requisition or competition filter), the generation time (Gregorian, invariant `yyyy-MM-dd HH:mm UTC`),
  the definitions version (`STR-007/2026-10-29`) and the source (HR.Core recruitment tables and event log). The Excel export
  repeats this block at the top of every sheet; the PDF prints it as a field table.
- **Drill-through:** requisition rows link to the per-requisition report and to `RecruitmentRequisitions/Details`; applicant
  counts to the applications list (its own authorization, PLAN-25 deferred); overdue approvals to the entity's screen.
- **Export controls:** Excel and PDF (aggregates) and the applications CSV are GET actions audited as `BulkExport` and capped
  by `ExportQueryLimits` (10,000 rows); above the cap the CSV becomes the export job `hrcore.recruitment.applications.csv`
  (scope and the names flag captured at request time, re-checked at download). No national id in any export; candidate
  names only for `HrCoreWrite`. Dates are Gregorian text, numbers invariant.

## Acceptance Criteria Mapping

- [x] Statutory catalog with legal authority and golden datasets
- [x] Version/effective-date and as-of semantics
- [x] Drill-through and source binding
- [x] Access matrix and retention policy
- [x] Background execution threshold defined
- [x] No cross-tenant in catalog policy

## Artifacts

| File | Purpose |
|------|---------|
| `docs/REP_001_REPORTS.md` | This document |
| `docs/rep/statutory-catalog.json` | Statutory report catalog |
| `scripts/rep001-validate.ps1` | Validates REP-001 artifacts |
| `Rep001ReportsTests.cs` | Unit tests for statutory catalog schema |

## Validation

- `scripts/rep001-validate.ps1`
- `Rep001ReportsTests`
