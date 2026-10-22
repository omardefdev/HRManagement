# مصفوفة صلاحيات معاملة «موظف جديد»

**مرجع:** DFD Level 2 + `usecase-reviewed-table-functional.xlsx` + [usecases.md](./usecases.md)  
**دليل التشغيل والاختبار:** [HIRE_ONBOARDING_GUIDE.md](./HIRE_ONBOARDING_GUIDE.md)

| خطوة DFD | النشاط | دور Keycloak | سياسة ASP.NET |
|----------|--------|--------------|---------------|
| P0.x | طلب شاغر / نشر / اختيار / عرض | `hr-core` | `Module.HrCore`, `HrCore.Write` / `HrCore.Approve` |
| P0.x | بوابة المواطن `/careers` | *(عام)* | بدون تسجيل HR |
| P0.x | لجنة الاختيار — أمين السر (أعضاء، أصوات، تعارض، توصيات، تقديم) | `hr-core` | `HrCore.Write` (PLAN-28) |
| P0.x | لجنة الاختيار — الرئيس/العضو | *(موظف من المنتقي، لا دور خاص)* | يسجّل أمين السر صوته؛ لا يعتمد الخطوة 3 |
| P0.x | اعتماد اللجنة: الخطوتان 1–2 / الخطوة 3 | `hr-core` أو `branch-manager` / `hr-management-approver` | `HrCore.BranchOperation` + فحوص الخادم (النصاب، المنشئ، العضو) |
| P0.x | محضر اللجنة PDF | `hr-core` وقراء HR.Core | `HrCore.ReadOnly` |
| P0.x | تقديم الاستئناف نيابة عن المرشح، تعيين المراجع، استيفاء المعلومات | `hr-core` | `HrCore.Write` (PLAN-28) |
| P0.x | قرار الاستئناف / طلب معلومات (المراجع المستقل المعيّن فقط) | `compliance` (+ مطالبة `employee_id`) | `HrCore.AppealReview` + فحص المراجع في الخدمة |
| P0.x | صندوق عمل HR.Core | قراء HR.Core + `compliance` (استئنافاته فقط) | `HrCore.WorkInbox` |
| P0.x | ترشّح ESS | `hr-core` (Ess) | `Module.Ess` |
| P1.1 | استلام مرشح / معاملة تعيين | `hr-contracts`, `hr-core` | `Module.HrContracts`, `HrContracts.HireExecute` (معالج) |
| P1.2 | تصنيف وعقد ومباشرة | `hr-contracts` | `HrContracts.Approve` (تفعيل عقد) |
| P1.2 | إنشاء العقد المعلّق يدوياً لنوع عقد غير مطابق (PLAN-29) | `hr-contracts`, `hr-core` | `HrContracts.HireExecute` |
| P1.2 | ربط أنواع العقود بأنظمة الخدمة — قراءة / تعديل (PLAN-29) | قراء المعالج / `hr-contracts` | `HrContracts.HireView` / `HrContracts.Approve` |
| D-6 | إصدار قرار التعيين (PLAN-29) | `hr-contracts`, `hr-core` | `HrContracts.HireExecute` |
| — | الفحص الطبي قبل التعيين — تسجيل الدليل (PLAN-29، اختياري) | `hr-contracts`, `hr-core` | `HrContracts.HireExecute` |
| P0.x | خطاب العرض الرسمي PDF (PLAN-29) | قراء HR.Core | `HrCore.ReadOnly` |
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
| `/ui/hr-core/SelectionCommittees` | جلسات لجنة الاختيار والمحضر |
| `/ui/hr-core/RecruitmentAppeals` | استئنافات التوظيف (`HrCore.AppealReview` على البوابة والمتحكم) |
| `/ui/hr-core/WorkInbox` | صندوق العمل (`HrCore.WorkInbox`) |
| `/ui/hr-contracts/HireWizard` | معالج التعيين (قائمة تحقق + إكمال). PLAN-29: `CreatePendingContract`، `IssueAppointmentDecision`، `CompleteMedicalStep` ← `HrContracts.HireExecute` على البوابة |
| `/ui/hr-contracts/ContractTypeRegimeMaps` | ربط أنواع العقود بأنظمة الخدمة (التعديل ← `HrContracts.Approve` على البوابة) |
| `/ui/hr-core/OfferLetters/Document/{id}` | خطاب العرض الرسمي PDF (صادر/مقبول) |

## أدوار تشغيلية مقترحة في Keycloak

| الدور | يُعادل اليوم |
|-------|-------------|
| `hr-service-ops` | `hr-contracts` + `HrContracts.HireExecute` |
| `hr-contracts-ops` | `hr-contracts` + `HrContracts.Approve` |
| `hr-career-auditor` | `hr-core` + `HrCore.Approve` |
| `hr-management-approver` | `hr-management-approver` + `HrManagement.Approve` |
