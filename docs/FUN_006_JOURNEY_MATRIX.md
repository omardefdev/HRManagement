# FUN-006 — مصفوفة رحلات الأرشيف والتحليلات والتقارير

المصدر: [`fun-archive/journey-matrix.json`](fun-archive/journey-matrix.json) — انظر [ADR-FUN-006](ADR-FUN-006-archive-analytics-reporting-closure.md).

## ArchiveLifecycle

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| Classified | نعم | archive | 24 | `DocumentClassification` + `ret-archive-docs` |
| Ingested | نعم | archive | 48 | Create/consumers + checksum |
| Held | لا | archive | 4 | `PlaceLegalHold` |
| Released | لا | archive | 24 | `ReleaseLegalHold` |
| DisposeRequested | نعم | archive | 72 | `RequestDisposal` |
| DisposeApproved | نعم | archive | 48 | `ApproveDisposal` (SoD) |
| Disposed | نعم | archive | 24 | `DisposalRecord` + custody |

## AnalyticsProjection

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| IngestEvent | نعم | analytics | 24 | consumers + `OrgUnitId` |
| Project | نعم | analytics | 48 | ReadModel / ETL |
| LateEvent | لا | analytics | 72 | ترتيب `OccurredAt` |
| Rebuild | نعم | analytics | 24 | `RebuildProjection` |

## BranchReportExport

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| ScopeGate | نعم | analytics | 1 | `HrOrgScopeVisibility` |
| BranchHeadcount | نعم | analytics | 24 | `WorkforcePlanningController` |
| Reconcile | نعم | analytics | 48 | checklist |
| ExportAsync | لا | analytics | 24 | PERF-004 ExportJob |

لا saga Automatonymous جديد — consumers + checklist + ExportJob. اعتماديات: GOV-001، PERF-004.
