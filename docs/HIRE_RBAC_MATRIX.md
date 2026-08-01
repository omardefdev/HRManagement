# مصفوفة صلاحيات معاملة «موظف جديد»

**مرجع:** DFD Level 2 + `usecase-reviewed-table-functional.xlsx` + [usecases.md](./usecases.md)  
**دليل التشغيل والاختبار:** [HIRE_ONBOARDING_GUIDE.md](./HIRE_ONBOARDING_GUIDE.md)

| خطوة DFD | النشاط | دور Keycloak | سياسة ASP.NET |
|----------|--------|--------------|---------------|
| P1.1 | استلام مرشح / معاملة تعيين | `hr-contracts`, `hr-core` | `Module.HrContracts`, `HrContracts.HireExecute` (معالج) |
| P1.2 | تصنيف وعقد ومباشرة | `hr-contracts` | `HrContracts.Approve` (تفعيل عقد) |
| P2.1 | ملف شخصي | `hr-core` | `HrCore.Write` |
| P6.1 | ربط راتب بعد تفعيل العقد | `payroll` | `Payroll.Process` |
| P5.1 | تدقيق واعتماد السيرة | `hr-core`, `hr-management-approver` | `HrCore.Approve`, `HrManagement.Approve` |
| P8.1 | إصدار هوية | `idcard` | `Module.IdCard` |
| P7.1 | أرشفة | `records-archive` | `Module.RecordsArchive` |
| ديوان | تسجيل مراسلة | `hr-core` | `HrCore.Write` (يدوي) + حدث `ITransactionRegisteredInDiwan` |

## مسارات البوابة

| المسار | الوصف |
|--------|--------|
| `/Home/HireJourney` | دليل مسار `new-hire` |
| `/ui/hr-contracts/HireWizard` | معالج التعيين (قائمة تحقق + إكمال) |

## أدوار تشغيلية مقترحة في Keycloak

| الدور | يُعادل اليوم |
|-------|-------------|
| `hr-service-ops` | `hr-contracts` + `HrContracts.HireExecute` |
| `hr-contracts-ops` | `hr-contracts` + `HrContracts.Approve` |
| `hr-career-auditor` | `hr-core` + `HrCore.Approve` |
| `hr-management-approver` | `hr-management-approver` + `HrManagement.Approve` |
