# مصفوفة رحلات E2E (QA-004)

مراجع: [ADR-QA-004](./ADR-QA-004-e2e-journeys.md)، [SOD_MATRIX.md](./SOD_MATRIX.md)، [HIRE_RBAC_MATRIX.md](./HIRE_RBAC_MATRIX.md)، [PROCESS_SAGAS.md](./PROCESS_SAGAS.md).

| Journey | Happy | Fail/Auth 1 | Fail/Auth 2 | Fail/Auth 3 | إثبات عميق | Personas | Suite |
|---|---|---|---|---|---|---|---|
| Hire | `HIR-H1` إنشاء/متابعة معالج تعيين مخوّل | `HIR-F1` مجهول → challenge | `HIR-F2` دور بلا HireExecute → رفض | `HIR-F3` عبر-tenant → رفض/عزل | صفحة/API + saga hire مرئية لاحقاً | maker-contracts, checker-mgmt | `HireJourneyE2ETests` |
| Separation | `SEP-H1` فتح معالج فصل مخوّل | `SEP-F1` مجهول | `SEP-F2` بلا صلاحية | `SEP-F3` تجاوز قائمة (DOM) يُرفض | checklist/API | hr-contracts | `SeparationJourneyE2ETests` |
| Payroll | `PAY-H1` فتح تشغيلات مخوّل | `PAY-F1` مجهول | `PAY-F2` processor≠approver (SoD) | `PAY-F3` 5xx≠نجاح على المسار | UI/API + SoD | processor, approver | `PayrollJourneyE2ETests` |
| Leave | `LEV-H1` مسار طلبات إجازة مخوّل | `LEV-F1` مجهول | `LEV-F2` منشئ≠معتمد | `LEV-F3` عبر-نطاق | API/UI | hr-contracts-a/b | `LeaveJourneyE2ETests` |
| Transfer | `TRF-H1` مسار نقل مخوّل | `TRF-F1` مجهول | `TRF-F2` self-approve | `TRF-F3` عبر-فرع | API/UI | movements, approver | `TransferJourneyE2ETests` |
| Files | `FIL-H1` UI ملفات مخوّل | `FIL-F1` مجهول | `FIL-F2` بلا صلاحية تنزيل | `FIL-F3` معرف ملف أجنبي | API | file-ops, outsider | `FilesJourneyE2ETests` |
| Role change | `ROL-H1` دليل الأدوار مخوّل | `ROL-F1` مجهول | `ROL-F2` تصعيد ذاتي مرفوض | `ROL-F3` بلا four-eyes | UI Auth.Directory | auth-directory, hr-admin | `RoleChangeJourneyE2ETests` |

## قواعد مشتركة

- `RUN_PLAYWRIGHT_E2E` off → Skip؛ on + stack down → Fail على فحوصات الاتصال.
- أي استجابة 503/5xx في مسار رحلة = فشل الاختبار.
- Seed: `testdata/e2e/manifest.json` إصدار `1.0.0`.
