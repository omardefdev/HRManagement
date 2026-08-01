# ADR-FUN-006 — إغلاق الأرشيف والتحليلات والتقارير

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | FUN-006 (البطاقة 55) |
| التاريخ | 2026-07-29 |
| الاعتماديات | GOV-001 Done، PERF-004 Done |

## السياق

Records.Archive وDev.Analytics موجودان كـ scaffold مع احتفاظ خفيف وإتلاف بلا موافقة/LegalHold، وأحداث BI بلا OrgUnit، وتقرير فرع يعتمد `Payload.Contains`. لا حزمة إغلاق FUN-006.

## القرار

| الموضوع | الاختيار |
|---|---|
| مصفوفة الرحلة | `docs/fun-archive/journey-matrix.json` — ArchiveLifecycle / AnalyticsProjection / BranchReportExport |
| Orchestration | consumers + checklist + ExportJob — **لا** Automatonymous saga جديد |
| أرشيف | LegalHold + ArchiveChecklist + disposal Request/Approve (SoD) + custody/hash |
| File plan | ربط تصنيف بـ`ret-archive-docs` / RetentionYears |
| تحليلات | `OrgUnitId` صريح على `AnalyticsEvent` + rebuild حتمي + freshness |
| تقارير | Branch RLS عبر `HrOrgScopeVisibility`؛ لا إجمالي وطني لدور فرع |
| تصدير | توصيل PERF-004 ExportJob لـAnalytics (+ شهادة إتلاف أرشيف) |
| UAT | ReadyForSign-off (`FUN_006_UAT_PACK.md`) |
| خارج النطاق | ANA-001/REP-001؛ DW خارجي؛ خدمة Reporting منفصلة؛ OCR |

## العواقب

- لا إتلاف تحت Hold أو بلا موافقة SoD.
- إسقاط BI يحفظ الجهة؛ rebuild يعيد نفس البصمة.
- تقرير الفرع لا يكشف الإجمالي الوطني.
