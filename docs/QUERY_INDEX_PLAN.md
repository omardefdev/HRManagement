# خطة فهرسة الاستعلامات (PERF-002)

وثيقة مقترحة/موجودة لفهارس مسارات موجات PERF-002. القياس الكامل لـ p95 تحت حمل موثّق في **NFR-001** ([NFR_CAPACITY_VERIFICATION.md](./NFR_CAPACITY_VERIFICATION.md)، سيناريوهات `perf/k6/`).

## موجة 1

| المسار | نمط الاستعلام | فهرس مقترح / موجود | ملاحظات |
|---|---|---|---|
| HR.Core Employees Index | `SearchTerm` + OrgScope + OrderBy Name + Skip/Take | موجود غالباً على `PrimaryOrgUnitId`؛ مركب `(PrimaryOrgUnitId, FullName)` إن نقص | NormalizePaging + PaginatedList |
| HR.Core OrgUnits Index | hierarchy parent + name | فهرس على `ParentId` / path (PERF-003 يحسّن الشجرة) | |
| Payroll PayrollRuns Index | `OrderByDescending(RunDate)` Take 100 | `(RunDate DESC)` أو `(PeriodStart DESC)` | |
| Payroll SalaryLines by run | `Where(PayrollRunId==)` | `(PayrollRunId, EmployeeId)` | تصدير CSV يرفض إن العدد > MaxExportRows |
| Gateway audit search | time range + keyset `(OccurredAtUtc, Id)` DESC | **موجود:** `IX_gateway_audit_entries_OccurredAt_Id` (migration `PERF002_GatewayAuditOccurredAtIdIndex`) + فهارس CorrelationId/StatusCode | رفض `"all"`؛ افتراضي 24h؛ PageSize ≤200 |

## موجة 2

| المسار | نمط الاستعلام | فهرس مقترح | ملاحظات |
|---|---|---|---|
| Contracts / Leave / Advances lists | Status + CreatedAt | `(Status, CreatedAt DESC)` | Take(MaxPageSize) |
| Compliance complaints/cases | Status + date | `(Status, OpenedAt DESC)` | تصدير capped |
| Movements transfer requests | Status + RequestedAt | `(Status, RequestedAt DESC)` | |
| Health medical records | EmployeeId + date | `(EmployeeId, CreatedAt DESC)` | |
| Training enrollments | CourseId / EmployeeId | `(CourseId, EnrolledAt)` | |
| IDCard issued cards | Status + IssuedAt | `(Status, IssuedAt DESC)` | |
| Archive items | Classification + CreatedAt | `(ClassificationId, CreatedAt DESC)` | |
| Auth users Index | UserName | unique/index on normalized name | LookupMax على الأدوار في النماذج |

## تصدير متزامن

- سقف موحّد: `SearchFilterBase.MaxExportRows = 10000`
- عند التجاوز: HTTP 400 برسالة عربية → توجيه لوظائف التصدير الخلفية (**PERF-004**)
- PERF-002 لا يبني طابور تصدير خلفي

## إعادة توليد الجرد

```powershell
powershell -NoProfile -File scripts/inventory-unbounded-queries.ps1
```

المخرجات: `docs/QUERY_EXPORT_INVENTORY.md` — هدف الإغلاق: صفر `unbounded` على `ToListAsync` في Controllers العامة.
