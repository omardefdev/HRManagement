# حزمة UAT — رحلة التعيين (FUN-001-E)

**الحالة:** ReadyForSign-off (منصة) — التوقيع الخارجي = UAT-001 لاحقاً.

| # | السيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | Happy path: مرشّح→موظف→عقد→checklist→saga→IHireCaseCompleted | كل أطراف إلزامية ACK بما فيها Directory | ____ |
| U2 | صلاحية: مستخدم بلا HrContractsApprove | لا Publish | ____ |
| U3 | طرف معطل: غياب AuthUser | Fault + intervention؛ لا اكتمال | ____ |
| U4 | تكرار مرشّح / Appointment موجود | لا معاملة مزدوجة | ____ |

مراجع: [FUN_001_JOURNEY_MATRIX.md](./FUN_001_JOURNEY_MATRIX.md)، [FUN_001_RECONCILIATION_CHECKLIST.md](./FUN_001_RECONCILIATION_CHECKLIST.md).
