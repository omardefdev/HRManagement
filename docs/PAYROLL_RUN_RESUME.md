# تشغيل رواتب ذري قابل للاستئناف (PAY-003)

## المراحل (Job Phase)

```text
Snapshot → Calculate → Validate → (Approve يدوي) → Post → Pay
```

| Phase | المعنى |
|---|---|
| Snapshot | تجميد قائمة الموظفين النشطين + `ProfileVersion` + pin إعدادات PAY-002 |
| Calculate | أقسام (افتراضي 200 موظف) مع checkpoint لكل موظف |
| Validate | تسوية المكوّنات/الصافي مقابل `TotalNetPay` وتجميد النتائج |
| Post | قفل تشغيلي + دفعة `PaymentBatch` بحالة `Pending` (ليست صرفاً) |
| Pay | `ProcessPaymentBatch` → `Processed` + `ProcessedAt` |

حالات `PayrollRun` البشرية تبقى: Draft / Calculated / UnderReview / Approved / Posted / Reversed، مع Calculating / Validating / Cancelled أثناء الـjob.

## Job / Partition / Checkpoint

- `PayrollRunJob` 1:1 مع التشغيل الحي أثناء المعالجة؛ حالات: Pending / Running / Paused / Completed / Failed / Cancelled.
- `LeaseOwner` + `LeaseUntil`: طلب حساب مكرر مع lease ساري يُرفض برسالة عربية؛ lease منتهٍ أو Paused يستأنف نفس الـjob.
- فهرس فريد جزئي: job نشط واحد لكل `PayrollRunId` (`Status` in Pending/Running/Paused).
- `PayrollRunPartition`: نطاق موظفين بحجم قابل للتهيئة؛ بعد كل موظف ناجح يُحدَّث `CursorEmployeeId` و`ProcessedCount`/`LastCheckpointAt`.
- ذرّية الموظف: معاملة واحدة = `SalaryLine` + مكوّنات + ربط استقطاعات/تسويات/غياب. `IX_SalaryLines_Run_Employee` يمنع التكرار عند الاستئناف.

## Pause / Resume / Cancel

- Pause: يضع الـjob في `Paused`؛ الحلقة تخرج عند الفحص التالي.
- Resume: يعيد `Running` ويستأنف من partitions غير المكتملة؛ الأسطر الموجودة تُتخطى.
- Cancel: يمنع Post، يضع التشغيل والـjob في `Cancelled` إن لم يُرحَّل.

## Validate والتجميد

بعد نجاح Validate: `ValidatedAtCalculate` + `ResultsFrozen`؛ `ValidateCanModifyLines` يرفض التعديل. Post يرفض إن لم يمر Validate أو بقيت partitions غير Completed.

## Post مقابل Pay وعكس تعويضي

- **Post** لا يضع `ProcessedAt`.
- **Pay** عبر `ProcessPaymentBatchCommand`.
- **Reverse**: دفعة `BatchKind=Reversal`، تحرير روابط الاستقطاع المطبّقة، الأسطر الأصلية تبقى (append-only)، الحالة `Reversed`.

## حدود PERF-001 / INT-002

- توازي العمال وbulk IO = **PERF-001** — انظر [PAYROLL_BATCH_PERFORMANCE.md](PAYROLL_BATCH_PERFORMANCE.md).
- Inbox موحّد عبر المنصة = INT-002 (مكتمل)؛ PAY-003 يحتفظ بـidempotency محلي للرواتب كدفاع مكمّل.

## اختبارات البوابة

`Pay003ResumablePayrollRunTests` + `Pay003ReconciliationTests`: ذرّية، استئناف، double-start، pause/resume، validate، partial post، تعارض إصدار، عكس تعويضي.
