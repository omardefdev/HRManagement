# Runbook دمج / إلغاء دمج الموظف (GOV-003)

## داخل HR.Core

1. فتح `MasterDataDuplicateCase` عبر `POST /api/master-data/duplicates` عند اشتباه تكرار.
2. مراجعة → `UnderReview`.
3. `POST /EmployeeLifecycle/merge` مع `DuplicateCaseId` اختياري (يعلّم case = Merged).
4. عند الخطأ: `POST /EmployeeLifecycle/unmerge` للمصدر بحالة Merged (يرفض عند LegalHold).

## بعد الدمج (خدمات تابعة — يدوي في v1)

| خدمة | إجراء |
|---|---|
| Payroll | مراجعة `EmployeePayProfile` للمصدر؛ نقل/إغلاق حسب سياسة الجهة |
| HR.Contracts | عقود/إجازات مربوطة بـEmployeeId المصدر |
| Auth.Directory | تعطيل حساب المصدر أو ربطه بالناجي |
| Orphans | تشغيل `scripts/reconcile-employee-orphans.ps1` ثم تقرير `gov003-data-quality-report.py` |

ليس MDM-002 (Person ID وطني) ولا نقل FK آلي عبر كل BC.
