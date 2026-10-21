# مصفوفة صلاحيات معاملة «موظف جديد»

**مرجع:** DFD Level 2 + `usecase-reviewed-table-functional.xlsx` + [usecases.md](./usecases.md)  
**دليل التشغيل والاختبار:** [HIRE_ONBOARDING_GUIDE.md](./HIRE_ONBOARDING_GUIDE.md)

| خطوة DFD | النشاط | دور Keycloak | سياسة ASP.NET |
|----------|--------|--------------|---------------|
| P0.x | طلب شاغر / نشر / اختيار / عرض | `hr-core` | `Module.HrCore`, `HrCore.Write` / `HrCore.Approve` |
| P0.x | بوابة المواطن `/careers` | *(عام)* | بدون تسجيل HR |
| P0.x | ترشّح ESS | `hr-core` (Ess) | `Module.Ess` |
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
| `/Home/JourneyDetails/new-hire` | مسار التوظيف الكامل (مرحلتان: HCM-001 + FUN-001) |
| `/careers` | بوابة المواطن — تقديم على إعلانات منشورة (عام) |
| `/ui/hr-core/RecruitmentRequisitions` | طلبات الشاغر ونشر الإعلان |
| `/ui/hr-core/RecruitmentApplications` | طلبات HR، اختيار، تعيين (OfferAccepted) |
| `/ui/hr-core/PublicJobPostings` | PromoteFromCitizen |
| `/ui/hr-core/TalentMarketplace` | PromoteFromInternal (HR — اختياري) |
| `/ui/hr-core/EssTalent` | ترشّح ESS (ModuleEss — اختياري) |
| `/ui/hr-core/OfferLetters` | خطابات العرض |
| `/ui/hr-contracts/HireWizard` | معالج التعيين (قائمة تحقق + إكمال) |

## أدوار تشغيلية مقترحة في Keycloak

| الدور | يُعادل اليوم |
|-------|-------------|
| `hr-service-ops` | `hr-contracts` + `HrContracts.HireExecute` |
| `hr-contracts-ops` | `hr-contracts` + `HrContracts.Approve` |
| `hr-career-auditor` | `hr-core` + `HrCore.Approve` |
| `hr-management-approver` | `hr-management-approver` + `HrManagement.Approve` |
