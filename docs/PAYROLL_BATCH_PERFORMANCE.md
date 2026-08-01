# Batch payroll متوازٍ ومحدود الموارد (PERF-001)

يبني على [PAY-003](PAYROLL_RUN_RESUME.md): نفس Job / Partition / Snapshot / lease، مع **bulk IO** و**pool عمال محدود** داخل العملية.

## الإعدادات (`Payroll` في appsettings)

| مفتاح | افتراضي | المعنى |
|---|---:|---|
| `MaxDegreeOfParallelism` | 4 | أقصى عمال أقسام متزامنين |
| `PartitionQueueCapacity` | 8 | سعة قناة الأقسام (backpressure) |
| `BulkWriteSize` | 50 | موظفون لكل معاملة كتابة |
| `DefaultPartitionSize` | 200 | حجم القسم إن لم يُمرَّر عند البدء |

الذاكرة العملية ∝ `PartitionSize × MaxDegreeOfParallelism` (وليس حجم التشغيل كاملاً).

## التدفق

```text
Snapshot (+ InputFingerprint) → enqueue partitions → bounded Channel workers
  → claim partition → bulk prefetch → build packages → bulk write → checkpoint
→ Validate / freeze
```

- **Claim:** `Pending → Running` مع `WorkerId`؛ فشل الـclaim = عامل آخر يملك القسم.
- **بصمة المدخلات:** SHA-256 لبدلات/استقطاعات/تسويات/غياب عند اللقطة؛ انحرافها يفشل القسم.
- **عدم تكرار الأثر:** `IX_SalaryLines_Run_Employee` + تخطي الموجود عند الاستئناف/إعادة التوزيع.
- **ETA:** `ProcessedEmployeeCount` / `TotalEmployeeCount` / `EstimatedCompletionAt` على الـjob؛ واجهة التفاصيل تعرض النسبة والمعدل.

## مقاييس

Meter `hrm.payroll.run`: `employees_processed`, `employees_failed`, `write_batches`, `partition_duration`, `active_partitions`.

## حدود النطاق

- لا MassTransit consumers للأقسام (مقياس موزّع لاحق تحت NFR/OPS).
- الحجم الوطني (مثلاً 1.25M) خارج CI؛ اختبارات الأداء تغطي 100 موظف آلياً و1k/10k كتشغيل موسّع/ليلي.

## اختبارات

`Perf001PayrollBatchTests` + استمرار `Pay003ResumablePayrollRunTests`.
