# مخزون الـInvariants — DATA-001

جرد القيود الحرجة وربط كل invariant بآلية فرض في PostgreSQL/التطبيق.
فحوص التطبيق تبقى لتجربة المستخدم فقط؛ المصدر الموثوق هو قيد DB أو تحديث ذري أو توكن تزامن.

## آلية التنفيذ

| رمز | المعنى |
|-----|--------|
| UK | UNIQUE index |
| PUK | Partial UNIQUE (شرط WHERE) |
| xmin | `UseXminAsConcurrencyToken()` |
| ATOM | `UPDATE … SET col = col ± n WHERE …` / `ExecuteUpdate` |
| IDEM | رأس HTTP `Idempotency-Key` + جدول تخزين |

## الجرد (مرتّب حسب الأولوية)

| # | Invariant | خدمة / جدول | آلية | أولوية | Story |
|---|-----------|-------------|------|--------|-------|
| 1 | سطر راتب واحد لكل موظف لكل تشغيل | Payroll / `SalaryLines` | UK `(PayrollRunId, EmployeeId)` | P0 | C |
| 2 | ملف دفع نشط واحد لكل موظف | Payroll / `EmployeePayProfiles` | PUK `(EmployeeId) WHERE "IsActive"` | P0 | C |
| 3 | تشغيل حي واحد لكل جهة/فترة/نوع | Payroll / `PayrollRuns` | PUK `(TenantId, PeriodStart, PeriodEnd, RunType) WHERE Status NOT IN (Reversed)` | P0 | C |
| 4 | دفعة بنكية فريدة لكل مرجع ضمن التشغيل | Payroll / `PaymentBatches` | UK `(PayrollRunId, BankTransferReference)` | P0 | C |
| 5 | مرجع ملف فريد لكل حامل | File.Storage / `FileReferences` | UK `(FileId, HolderKey)` — موجود | — | — |
| 6 | عدّاد مراجع متسق | File.Storage / `Files.ReferenceCount` | ATOM ±1 | P0 | D |
| 7 | لا حذف ملف ما دام عليه مراجع | File.Storage / soft-delete | ATOM `WHERE ReferenceCount = 0` | P0 | D |
| 8 | رصيد إجازة لا يُسحب فوق الاستحقاق | Contracts / `LeaveBalances` | ATOM `Used += d WHERE Used + d <= TotalEntitlement` | P0 | E |
| 9 | طلب إجازة لا يُعتمد مرتين | Contracts / `LeaveRequests` | xmin + حالة | P0 | E |
| 10 | طلب نقل لا يُنفَّذ مرتين | Movements / `TransferRequests` | xmin + انتقال حالة شرطي | P0 | F |
| 11 | كمية مخزون لا تصبح سالبة | AdminLogistics / `InventoryItems` | ATOM `WHERE Quantity >= n` + xmin | P1 | G |
| 12 | طلب اعتماد لا يُحدَّث بصمت | HR.Core / `ApprovalRequests` | xmin | P1 | B/C |
| 13 | إعادة POST لنفس العملية | مسارات حرجة | IDEM | P0 | H |

## حالات PayrollRun «حية» (للـPUK)

تُستثنى فقط: `Reversed` (قيمة 5). التشغيلات `Draft` / `Calculated` / `UnderReview` / `Approved` / `Posted` تُعدّ حيّة ويمنع تكرارها لنفس `(TenantId, PeriodStart, PeriodEnd, RunType)`.

## رسائل التعارض (المستخدم)

| مصدر | HTTP | رسالة عربية افتراضية |
|------|------|----------------------|
| `DbUpdateConcurrencyException` | 409 | تم تعديل السجل من مستخدم آخر. أعد التحميل ثم حاول مجدداً. |
| Postgres `23505` | 409 | يتعارض الطلب مع قيد فريد — السجل موجود مسبقاً أو العملية مكررة. |
| Idempotency hash mismatch | 409 | مفتاح Idempotency-Key مستخدم بجسم طلب مختلف. |
| Atomic update rows=0 | 409/400 | الرصيد/الكمية غير كافية أو تعارض تزامن. |

## ترحيل البيانات قبل القيود الجزئية

قبل تطبيق PUK/UK على بيانات موجودة، شغّل اكتشاف المكررات:

```sql
-- SalaryLines duplicates
SELECT "PayrollRunId", "EmployeeId", COUNT(*) FROM "SalaryLines"
GROUP BY 1,2 HAVING COUNT(*) > 1;

-- Active pay profiles
SELECT "EmployeeId", COUNT(*) FROM "EmployeePayProfiles"
WHERE "IsActive" GROUP BY 1 HAVING COUNT(*) > 1;

-- Live payroll runs
SELECT "TenantId", "PeriodStart", "PeriodEnd", "RunType", COUNT(*) FROM "PayrollRuns"
WHERE "Status" <> 5 GROUP BY 1,2,3,4 HAVING COUNT(*) > 1;
```

سكربت مساعد: `scripts/detect-data001-duplicates.sql`.

## خارج النطاق (بطاقات لاحقة)

- Inbox الرسائل الموحّد → INT-002
- نسخة قواعد الراتب غير القابلة للتغيير → PAY-002
- دورة حذف الملفات الأوسع → FILE-001
