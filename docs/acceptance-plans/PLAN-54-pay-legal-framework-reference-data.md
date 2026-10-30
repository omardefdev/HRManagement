# PLAN-54 — الإطار القانوني للرواتب وبياناته المرجعية في الإنتاج (PAY-A)

**Scope:** PAY — [دراسة الفجوات](../PAY_001_GAP_STUDY.md): G-PAY-01، G-PAY-10 (الجزء غير المغطى في PLAN-53)، بنية G-PAY-12، وجزء الإطار من G-PAY-09  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-37 (توصيل الإنتاج والملف الشبيه بالإنتاج)، PLAN-53 (سياسات كتابة الرواتب ومعاملة تغيير الراتب). ويستهلك PLAN-34 (العميل المزيف لأنظمة الخدمة في مضيف التكامل)  
**Related cards:** PAY-002، GOV-002، SYR-005، SYR-006، STR-002 (كتالوج الضوابط القانونية)، SYD-003/004/005

## Goal

أن تستطيع جهة تعمل **بإعدادات الإنتاج** (`Seed:Profile=None`، بلا بذرة Demo) أن:

- تُنشئ إطاراً قانونياً للرواتب وتستورده من حزمة موقّعة، ثم تعتمده وتصادقه وتنشره بثلاثة أشخاص مختلفين، فيُحسب أول تشغيل.
- تجد كل قيمة تحكم الراتب (الشرائح، الخصومات النظامية، العطل، قواعد أولوية الصرف) داخل نسخة الإطار المثبتة على التشغيل وداخل بصمتها، لا في جداول عامة قابلة للتعديل.
- لا تنشر قيمة «مؤقتة» (Interim) إلا تحت استثناء نافذ في كتالوج STR-002.
- لا تُدخل بدلاً أو خصماً أو تسوية يدوية إلا بمعتمد ثانٍ.

القيم القانونية النهائية نفسها خارج هذه الخطة (D-PAY-1، الموجة 2: PAY-E). هذه الخطة تبني **الوعاء** وتربطه بالحوكمة.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-PAY-1 — القيم القانونية لكل نظام (SYD-003/004/005) | حزمة `law50-interim` تطابق قيم البذرة الحالية حرفياً ([PayrollRegimeSeed.cs:12-19, 75-126](../../src/Services/Payroll/Payroll.Domain/Services/PayrollRegimeSeed.cs)). كل قيمة `legalStatus=Interim` وتستشهد باستثناء نافذ: `EX-STR2-TAX-INTERIM` للضريبة، و`EX-STR2-LAW50-UNCERTIFIED` لغيرها. لا تغيير في نتائج الحساب | لا تغيير في الأرقام في الموجة 1. ينتهي الاستثناءان في 2027-01-31، فيتوقف النشر والحساب بالإطار المؤقت بعده (مقصود) |
| D-PAY-6 — مصدر تغييرات الراتب والمدخلات اليدوية | الراتب عبر معاملة PLAN-53. البدلات والخصومات والتسويات اليدوية تمر بمعتمد ثانٍ (`Payroll:ManualInputs:RequireApproval=true`) | المدخل اليدوي لا يدخل الحساب قبل اعتماده |
| D-PAY-12 — من يصادق قانونياً على نسخة الإطار | حامل `Payroll.Approve` مختلف عن معتمد النسخة ومحررها. يُضبط بـ `Payroll:Legal:AttestorPolicy` (الافتراضي `Payroll.Approve`)، ويُستبدل بدور قانوني لاحقاً دون تغيير الشيفرة | يلزم معتمدا رواتب في كل جهة. تضيف الخطة الشخصية `a-payroll-approver-2` |
| D-PAY-13 — نشر نسخة بأثر رجعي | يُرفض نشر نسخة يقع `EffectiveFrom` لها في فترة فيها تشغيل مرحّل للنظام نفسه. الأثر الرجعي في الموجة 2 (PAY-H) | لا تغيّر نسخة جديدة نتائج فترة مرحّلة |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-54-pay-legal-framework-reference-data` | — |
| 0.2 | البناء | `dotnet build HRManagement.slnx` | 0 أخطاء |
| 0.3 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay0\|FullyQualifiedName~Payroll\|FullyQualifiedName~Gov002"` | أخضر، أو قائمة إخفاقات في `reports/plan54-baseline.md` |
| 0.4 | جرد | سرد كل قراءة لـ `DeductionTypes` و`OfficialHolidays` و`DisbursementPriorityRules` و`GetActiveConfigurationAsync` في Payroll | الجدول في `reports/plan54-baseline.md` |
| 0.5 | التحقق من PLAN-53 | أسماء السياسات التي أدخلها PLAN-53 لكتابة الرواتب، واسم كيان معاملة تغيير الراتب | الأسماء مدونة في `reports/plan54-baseline.md`، وتُستعمل كما هي في هذه الخطة |
| 0.6 | مضيف اختبارات التكامل (تستهلكه هذه الخطة من PLAN-34) | `FakeHrCoreServiceRegimeClient` مسجّل في `PayrollIntegrationTestSetup` ([PayrollIntegrationTestSetup.cs:16-43](../../src/Tests/HRManagement.IntegrationTests/PayrollIntegrationTestSetup.cs)) بـ PLAN-34. لا يُعاد تنفيذه هنا. واختبار مسار الحجب يبقى في PLAN-59 | الاختبارات العشرة على SQLite في G-PAY-05 خضراء على الفرع، وإلا تُوقف الخطة حتى تُدمج PLAN-34 |
| 0.7 | نظام الخدمة في بذرة القبول (نُقل من PLAN-59 لأن كل خطط الرواتب تحسب في القبول) | `AcceptanceDataSeeder` في HR.Core يضبط `LegalRegimeId = "law50"` لسجلات التوظيف الجارية ([AcceptanceDataSeeder.cs:332-351](../../src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs))، لأن حاجز `MISSING_SERVICE_REGIME` يحجب كل تشغيل اعتيادي ([ServiceRegimePayrollBlockService.cs:22-52](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimePayrollBlockService.cs)) | تشغيل اعتيادي في بيئة القبول يتجاوز حاجز النظام |

---

## In scope

### 1. إنشاء الإطار واستيراده في الإنتاج (G-PAY-01)

**المشكلة:**
- لا إجراء `Create` في `PayrollConfigurationsController` ([PayrollConfigurationsController.cs:51-234](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollConfigurationsController.cs)). و`Edit.ApplyFields` لا يلمس الشرائح ولا تاريخ السريان (`:71-92`).
- البذرة في `Demo`/`Acceptance` فقط ([Program.cs:130-151, 179-208](../../src/Services/Payroll/Payroll.Web/Program.cs)؛ [SeedProfile.cs:35](../../src/Shared/HRManagement.ServiceDefaults/Seeding/SeedProfile.cs)). فيرفض الحساب ([PayrollRunJobHandlers.cs:180-184](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
- النسخة المستنسخة ترث `EffectiveFrom` ([PayrollConfigurationVersioning.cs:47](../../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationVersioning.cs))، والنشر يقصّ سابقتها بأثر رجعي (`:121-129`).
- `deploy-organization.ps1` يضبط `HR_LEGAL_REGIME` ولا يقرؤه أحد ([deploy-organization.ps1:11, 21](../../scripts/deploy-organization.ps1)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| مخطط الحزمة `payroll-framework-pack.v1` | `docs/syr/payroll-framework-packs/schema.v1.json` | `code`، `regimeCode`، `nameAr`، `effectiveFrom`، والحقول العددية للإطار. ومصفوفات `taxBrackets[]` و`statutoryDeductions[]` و`holidays[]` و`priorityRules[]` و`decisions[]`. كل قيمة تحمل `legalReference` و`legalStatus` (`Signed`\|`Interim`)، و`interimExceptionId` إن كانت مؤقتة |
| حزمة مؤقتة | `docs/syr/payroll-framework-packs/law50-interim.json` | قيم البذرة الحالية حرفياً (D-PAY-1) |
| `PayrollFrameworkPackImporter` | `Payroll.Application/Features/PayrollConfigurations/PayrollFrameworkPackImporter.cs` | يتحقق من المخطط والقيود (الشرائح تصاعدية بلا فجوات، نسب 0–100، تاريخ سريان إلزامي)، ثم ينشئ نسخة `Draft` جديدة للرمز (`VersionNumber` = الأعلى + 1)، أو يستبدل محتوى مسودة قائمة إن مُرّر `targetDraftId`. ويُدرج سجلات `LegalDecisionRecord` المفقودة أو يحدّثها حسب `DecisionId` |
| `CreatePayrollFrameworkCommand` | ↑ نفس المجلد | إنشاء مسودة من قالب نظام (`regimeCode`) أو استنساخ نسخة (`cloneFromId`)، بتاريخ سريان يحدده المستخدم |
| API | `Payroll.Web/Controllers/PayrollFrameworksApiController.cs` (`api/payroll/frameworks`) | `POST` (إنشاء)، `POST import`، `GET {id}/export` (تصدير المسودة JSON للتعديل)، `PUT {id}/effective-from`. كلها بسياسة `Payroll.MasterData` من PLAN-53 |
| MVC | `PayrollConfigurationsController` + `Views/PayrollConfigurations/Create.cshtml`، `Import.cshtml`؛ و`Details.cshtml` يعرض جداول الشرائح والخصومات النظامية والعطل والأولويات ومرجع كل قيمة | الاستيراد هو طريق تحرير الجداول. ويبقى `Edit` للحقول العددية في المسودة، ويضاف إليه `EffectiveFrom` |
| `TryPublish` | `PayrollConfigurationVersioning.cs` | يستعمل `EffectiveFrom` النسخة. ويرفض أن يقع سريانها عند سريان النسخة المنشورة السابقة أو قبله، أو داخل فترة تشغيل مرحّل للنظام نفسه (D-PAY-13). ويقصّ السابقة إلى `EffectiveFrom − 1` فقط |
| سكربت أول تشغيل | `scripts/pay054-import-legal-pack.ps1` | يستدعي `POST api/payroll/frameworks/import` بالحزمة ويطبع خطوات الاعتماد والمصادقة والنشر |
| `deploy-organization.ps1` | `scripts/` | حذف `HR_LEGAL_REGIME` (`:11`)، وطباعة أمر `pay054-import-legal-pack.ps1` بدل `:21` |
| تشخيص | `DashboardController` + `PayrollRunJobHandlers.ResolveConfigAsync` | لوحة الرواتب تعرض «لا يوجد إطار منشور» مع رابط الاستيراد. ورسالة رفض الحساب تذكر الرابط |

**القواعد:**
1. لا تُنشأ نسخة إطار في الإنتاج إلا بالاستيراد أو بالإنشاء من قالب. ولا تُزرع بذرة إلا في `Demo`/`Acceptance` كما اليوم.
2. الشرائح والخصومات النظامية والعطل والأولويات تتغير في المسودة فقط. النسخة `Approved`/`Published`/`Superseded` غير قابلة للتغيير (`IsImmutable`)، وتعديلها يستنسخ مسودة.
3. الحزمة المرفوضة لا تترك أي صف: الاستيراد في معاملة واحدة.
4. كل `decisions[].decisionId` تشير إليه قيمة في الحزمة يجب أن يوجد بعد الاستيراد.

### 2. الخصومات النظامية والعطل والأولويات داخل النسخة والبصمة (G-PAY-10، بنية G-PAY-12)

**المشكلة:**
- `DeductionTypes` النظامية جدول عام قابل للتعديل، يُقرأ عند كل حساب ([PayrollRunParallelCalculator.cs:87](../../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs))، ويطبَّق على الكل ([PayrollRules.cs:130-151](../../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
- البصمة لا تشمله ([PayrollConfigurationHashService.cs:32-55](../../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationHashService.cs)). وكذلك العطل العامة (`PayrollConfigurationId == null`، [PayrollRunParallelCalculator.cs:82-85](../../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)).
- الشرائح الاحتياطية في الشيفرة ([PayrollEngine.cs:12-19](../../src/Services/Payroll/Payroll.Domain/Services/PayrollEngine.cs)؛ [PayrollRules.cs:265-266](../../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
- نسب التأمين ثابتة في التصدير ([PayrollExternalExportService.cs:19-20, 85-94](../../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `StatutoryDeductionRule` | `Payroll.Domain/Entities/StatutoryDeductionRule.cs` | ابن `PayrollConfiguration`: `Code`، `NameAr`، `EmployeePercent`، `EmployerPercent`، `FixedAmount`، `BaseKind` (`GrossPay`\|`BaseSalary`)، `Ceiling?`، `InsuranceBody?` (GOSI/GOIP)، `LegalReference`، `LegalStatus`، `InterimExceptionId?`، `SortOrder` |
| حقول المرجع على الشرائح والعطل والأولويات | `TaxBracket`، `OfficialHoliday`، `DisbursementPriorityRule` | `LegalReference`، `LegalStatus`، `InterimExceptionId?`. وتُضاف `PayrollConfigurationId` إلى `DisbursementPriorityRule` |
| مراجع الحقول العددية | `PayrollConfigurationFieldSource` (جدول ابن: `FieldName`، `LegalReference`، `LegalStatus`، `InterimExceptionId?`) | يغطي `TaxExemptionAmount` و`TaxableBaseMode` و`DailyRateDivisor` وغيرها |
| `HashSchemaVersion` | `PayrollConfiguration` | `1` للنسخ القائمة (البصمة كما هي)، و`2` لكل نسخة جديدة. البصمة v2 تشمل الخصومات النظامية والعطل والأولويات والمراجع والحالات |
| `PayrollConfigurationHashService.ComputeContentHash` | ↑ | فرع v2. والنسخ v1 لا يُعاد حساب بصمتها، فتبقى التشغيلات المثبتة صالحة (`ResolveConfigAsync` يقارن البصمة المخزنة، [PayrollRunJobHandlers.cs:167-177](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)) |
| `StatutoryDeductionRule` في المحرك | `PayrollRules.cs` + `PayrollCalculationContext` | النسخة v2: القاعدة تقرأ `context.Configuration.StatutoryDeductions` وتطبق `BaseKind` و`Ceiling`. النسخة v1: السلوك القديم من `DeductionTypes` مع تحذير في سجل المهمة. ويُحذف الاحتياط إلى `DefaultBrackets`: نسخة بلا شرائح ترفض النشر |
| العطل | `PayrollRunParallelCalculator` + `CorrectionRunHandlers` | النسخة v2 تقرأ عطلها فقط. والصفوف العامة (`null`) للنسخ v1 وحدها |
| الأولويات | `ExecutePartialDisbursementCommand` | تقرأ قواعد النسخة المثبتة على التشغيل، لا `IsActive` العامة (الاستعمال نفسه يعاد تصميمه في PLAN-58) |
| التصدير | `PayrollExternalExportService` | حصة صاحب العمل وحصة الموظف من قاعدة النسخة (`EmployerPercent`، `Ceiling`)، وتُحذف الثابتتان 7% و14% |

**القواعد:**
1. حساب تشغيل بنسخة v2 لا يقرأ `DeductionTypes.IsStatutory` ولا العطل العامة. واختبار يثبت أن تعديل `DeductionTypes` بعد التثبيت لا يغيّر النتيجة ولا البصمة.
2. `DeductionTypes` يبقى فهرساً لأنواع الخصم غير النظامية فقط. ويُعرض الحقل `IsStatutory` في الواجهة للقراءة مع ملاحظة «انتقل إلى الإطار القانوني».
3. ترحيل EF يضيف الجداول والحقول، ويضبط `HashSchemaVersion=1` لكل صف قائم دون تغيير `ContentHash`.

### 3. حوكمة القيم المؤقتة وفصل واجبات دورة الإطار (بنية G-PAY-12، جزء الإطار من G-PAY-09)

**المشكلة:**
- `Approve` و`LegalAttest` و`Publish` كلها بالسياسة نفسها ([PayrollConfigurationsController.cs:120-210](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollConfigurationsController.cs)). والنشر يقبل المسودة مباشرة ([PayrollConfigurationVersioning.cs:100-112](../../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationVersioning.cs)).
- التصديق لا يتحقق من وجود القرارات ([PayrollLegalGovernance.cs:11-34](../../src/Services/Payroll/Payroll.Domain/Services/PayrollLegalGovernance.cs)).
- سجل القرارات كله «ApprovedInterim» ([LegalDecisionRegisterSeed.cs:14-27](../../src/Services/Payroll/Payroll.Domain/Services/LegalDecisionRegisterSeed.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PayrollFrameworkGovernance` | `Payroll.Domain/Services/PayrollFrameworkGovernance.cs` (يحل محل منطق `PayrollLegalGovernance`) | قواعد الفاعلين: محرر المسودة (`CreatedBy`/آخر مستورد) ≠ المعتمد ≠ المصادق، والناشر ≠ المحرر. والنشر من `Approved` فقط |
| `IPayrollLegalControlGuard.IsExceptionActive(id, asOf)` | `Payroll.Application/Abstractions/IPayrollLegalControlGuard.cs` + `Payroll.Infrastructure/PayrollLegalControlGuard.cs` | يقرأ `ILegalControlCatalogProvider.GetException` ويشترط `Status=Active` و`expiresOn > asOf`. الموجود `IsExceptionActive` لا يفحص الانتهاء ([LegalControlCatalogProvider.cs:49-55](../../src/Shared/HRManagement.ServiceDefaults/LegalControl/LegalControlCatalogProvider.cs)) |
| بوابة القيم المؤقتة | الاعتماد والتصديق والنشر + `StartPayrollRunJobHandler` | أي قيمة `Interim` بلا `interimExceptionId` نافذ ترفض الاعتماد والنشر. وعند بدء الحساب، تُرفض النسخة المثبتة إن صار أحد استثناءاتها غير نافذ: «انتهى الاستثناء القانوني X». هذا رفض مغلق، ويُكتب في سجل المهمة |
| المصادقة | `LegalAttest` | يتحقق من وجود كل `DecisionId` في `LegalDecisionRecords`، ويحفظ قائمة الاستثناءات المستشهد بها في `LegalAttestation` (حقل جديد `InterimExceptionIdsJson`) |
| السياسات | المتحكم | `Approve`: سياسة الاعتماد. `LegalAttest`: `Payroll:Legal:AttestorPolicy` (D-PAY-12). `Publish`/`Activate`: سياسة الاعتماد مع قاعدة الناشر |
| مؤشر في القسيمة والتشغيل | `PayrollRunProvenanceDto` + `Details.cshtml` | «إطار مؤقت — استثناء EX-…» حين تحوي النسخة المثبتة قيماً مؤقتة |

**القواعد:**
1. أربعة أفعال وثلاثة أشخاص على الأقل: يحرر (أو يستورد) أ، ويعتمد ب، ويصادق ج، وينشر ب أو ج. وأي محاولة تخالف ذلك تعيد 400 برسالة عربية ولا تغيّر الحالة.
2. لا نشر من `Draft`. ويُحذف المسار الذي يملأ `ApprovedBy` آلياً في `TryPublish` (`:111-115`).
3. `Activate` لا يتجاوز النشر: يعمل على `Published` فقط.

### 4. المدخلات اليدوية بمعتمد ثانٍ وبلا ربط كيان (G-PAY-10)

**المشكلة:** `AllowanceRecords.Create` و`DeductionRecords.Create` و`PayrollAdjustments.Create` تربط الكيان كله وتحفظه نافذاً فوراً ([PayrollExtendedControllers.cs:45-59, 94-133, 166-205](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs)). ولا يُشترط أي معتمد.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `ManualInputApproval` (حقول) | `AllowanceRecord`، `DeductionRecord`، `PayrollAdjustment` | `ApprovalStatus` (`Approved`\|`PendingApproval`\|`Rejected`)، `Origin` (`System`\|`Manual`)، `ApprovedBy`، `ApprovedAt`، `DecisionNote`. الصفوف القائمة وصفوف الأحداث: `Approved`/`System` |
| DTOs | `Payroll.Web/ViewModels/ManualInputViewModels.cs` | قوائم سماح: الموظف، النوع، المبلغ، التاريخ، السبب. ولا `AppliedInRunId` ولا `PayrollRunId` من العميل |
| أوامر | `Payroll.Application/Features/ManualInputs/` | `CreateManualInputCommand` (→ `PendingApproval`)، و`DecideManualInputCommand` (المعتمد ≠ المنشئ، بسبب عند الرفض). بالنمط نفسه الذي أدخله PLAN-53 لتغيير الراتب |
| المحمّل | [PayrollPartitionInputLoader.cs:121-164](../../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs) | يحمّل `ApprovalStatus=Approved` فقط |
| واجهة | `Views/AllowanceRecords\|DeductionRecords\|PayrollAdjustments/Index.cshtml` | عمود الحالة، وزرّا «اعتماد/رفض» لمن يملك الاعتماد |

**القواعد:**
1. المدخل اليدوي لا يدخل أي تشغيل قبل اعتماده. ومدخل يُعتمد بعد تجميد تشغيل فترته يدخل التشغيل التالي (سلوك المحمّل في PLAN-56).
2. الإعداد `Payroll:ManualInputs:RequireApproval` يمكن إطفاؤه في Development فقط. وفي غيرها يُتجاهل `false` ويُسجَّل تحذير عند الإقلاع.
3. سياسات الإجراءات الجديدة صريحة (`[Authorize(Policy=…)]`) وتدخل في اختبار السقف الذي أدخله PLAN-53.

---

## Out of scope

- السياسات الصريحة لإجراءات POST الستة عشر، واختبار سقف الرواتب، ومعاملة تغيير الراتب الأساسي ← **PLAN-53** (هذه الخطة تستهلكها).
- القيم القانونية النهائية، والحساب بإطار لكل موظف حسب نظامه، وسقف الاقتطاع، وقاسم الأيام ← الموجة 2: **PAY-E** (G-PAY-11، قيم G-PAY-12).
- استعمال قواعد الأولوية في مسار الصرف الموحد ← **PLAN-58**.
- العطل الهجرية والتقويم الرسمي المركزي ← الموجة 2 (HCM-002 / PAY-H).
- التصديق بدور قانوني مستقل في Keycloak ← قرار لاحق (D-PAY-12). الإعداد جاهز.

## Acceptance scenarios

على الملف الشبيه بالإنتاج (PLAN-37)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، بالفئة `AcPay54LegalFrameworkTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-PAY-54")`):

| ID | Positive | Negative |
|----|----------|----------|
| AC-PAY-54a | `payroll-processor` يستورد `law50-interim.json` برمز جديد `LAW_50_2004_AC54` وسريان 2031-01-01 ← مسودة. `payroll-approver` يعتمد، و`a-payroll-approver-2` يصادق، و`payroll-approver` ينشر ← `Published`. ثم تشغيل 2031-01 يُحسب ويثبَّت على النسخة (`provenance.configId`) | حزمة بشريحة نسبتها 120% ← 400 ولا صف جديد |
| AC-PAY-54b | — | المعتمد نفسه يصادق ← 400. المحرر ينشر ← 400. نشر من `Draft` ← 400. الحالة لا تتغير في كل مرة |
| AC-PAY-54c | نسخة v2 من الإطار نفسه تغيّر نسبة خصم نظامي ← بصمة جديدة، والتشغيلات المثبتة على الأولى لا تتغير عند إعادة قراءة `provenance` | تعديل `DeductionTypes` (نسبة نظامية قديمة) بعد التثبيت ← نتيجة إعادة الحساب على لقطة جديدة لنفس النسخة مطابقة |
| AC-PAY-54d | — | حزمة بقيمة `Interim` تستشهد باستثناء غير موجود ← رفض الاعتماد برسالة تسمّي القيمة والاستثناء |
| AC-PAY-54e | `payroll-processor` ينشئ بدلاً يدوياً ← `PendingApproval` ولا يظهر في الحساب. `payroll-approver` يعتمده ← يظهر في التشغيل التالي | المنشئ يعتمد بدله ← 403/400. POST بحقل `appliedInRunId` ← يُتجاهل |
| AC-PAY-54f | — | نشر نسخة تسري داخل فترة تشغيل مرحّل للنظام نفسه ← 400 (D-PAY-13) |

وخارج القبول الحي: اختبار تكامل على PostgreSQL ببيانات فارغة (`Seed:Profile=None`)، يثبت الإنشاء ← الاعتماد ← المصادقة ← النشر ← أول حساب، ومعاملة الاستيراد الذرية.

## Verification

```powershell
dotnet build HRManagement.slnx
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay054|FullyQualifiedName~Pay002|FullyQualifiedName~PayrollEngine|FullyQualifiedName~Gov002"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Pay054"
dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web
# قبول على الملف الشبيه بالإنتاج (أوامر PLAN-37)، ثم:
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --no-deps --force-recreate payroll-web
pwsh scripts/prepare-keycloak-import.ps1   # الشخصية الجديدة a-payroll-approver-2
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcPay54"
pwsh scripts/pay054-validate.ps1
```

## Definition of done

- قاعدة بيانات رواتب فارغة بإعدادات الإنتاج تصل إلى أول حساب عبر الاستيراد والاعتماد والمصادقة والنشر فقط، بلا SQL ولا بذرة (اختبار التكامل + AC-PAY-54a).
- لا يقرأ الحساب بنسخة v2 أي جدول عام قابل للتعديل. البصمة v2 تشمل الخصومات النظامية والعطل والأولويات والمراجع.
- لا نشر ولا حساب بقيمة مؤقتة دون استثناء نافذ.
- لا مدخل يدوي يدخل الحساب دون معتمد ثانٍ.
- AC-PAY-54a..f = Pass في `acceptance-evidence/AC-PAY-54/`، وانحدار `Pay002ImmutableConfigTests` و`PayrollEnginePipelineTests` أخضر.
- أسطر «الحالة» لـ G-PAY-01 وG-PAY-10 وبنية G-PAY-12 محدّثة في الدراسة.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md` |
| Domain | `StatutoryDeductionRule.cs`، `PayrollConfigurationFieldSource.cs`، `PayrollFrameworkGovernance.cs`، حقول جديدة على `TaxBracket`/`OfficialHoliday`/`DisbursementPriorityRule`/`LegalAttestation`/المدخلات اليدوية |
| Application | `PayrollFrameworkPackImporter.cs`، `CreatePayrollFrameworkCommand.cs`، `Features/ManualInputs/*` |
| Web | `PayrollFrameworksApiController.cs`، `Create.cshtml`، `Import.cshtml`، تحديث `Details.cshtml` و`Edit.cshtml` وشاشات المدخلات اليدوية |
| Migration | `PAY054_LegalFrameworkPacks` |
| Packs | `docs/syr/payroll-framework-packs/schema.v1.json`، `law50-interim.json` |
| Scripts | `scripts/pay054-import-legal-pack.ps1`، `scripts/pay054-validate.ps1`، تعديل `scripts/deploy-organization.ps1` |
| Unit | `Pay054FrameworkPackImporterTests.cs`، `Pay054FrameworkGovernanceTests.cs`، `Pay054HashV2Tests.cs`، `Pay054ManualInputApprovalTests.cs` |
| Integration | `Pay054EmptyDatabaseFirstRunPostgreSqlTests.cs` (يستعمل العميل المزيف من PLAN-34) |
| Seed | `HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs` (`LegalRegimeId`، المرحلة 0.7) |
| Acceptance | `AcPay54LegalFrameworkTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، الشخصية `a-payroll-approver-2` في `testdata/acceptance/personas.json` و`AcceptancePersonaCatalog.cs`، وتحديث العدد (+1 على القيمة القائمة وقت التنفيذ، لا 61) في [prepare-keycloak-import.ps1:137-138, 269](../../scripts/prepare-keycloak-import.ps1) أو مولّد realm الذي يغلّفه منذ PLAN-39 |
| Docs | `docs/PAYROLL_CONFIG_VERSIONING.md` (الحزمة، v2، الفاعلون، الاستثناءات)، `docs/PAYROLL_RULES.md`، `docs/PILOT_DEPLOYMENT.md` (الخطوة 3: الاستيراد)، أسطر «الحالة» في `docs/PAY_001_GAP_STUDY.md` |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تغيير المحرك يغيّر نتائج اختبارات قائمة تعتمد `DeductionTypes` العامة | النسخ v1 تحتفظ بالسلوك القديم. واختبارات v2 جديدة منفصلة |
| انتهاء الاستثناءين في 2027-01-31 يوقف الرواتب المؤقتة | مقصود (حوكمة STR-002). يُذكر في `PILOT_DEPLOYMENT.md` وفي لوحة الرواتب قبل 60 يوماً من الانتهاء |
| يلزم معتمدا رواتب في كل جهة | الشخصية `a-payroll-approver-2` في القبول. وفي الإنتاج يُوثّق كشرط تشغيل مع D-PAY-12 |
| تعارض الأسماء مع ما أدخله PLAN-53 | المرحلة 0.5 تدوّن أسماءه، وتستعملها هذه الخطة |
| حجم الواجهة | تحرير الجداول عبر تصدير/استيراد JSON للمسودة، والواجهة للعرض والحقول العددية |

## Effort estimate

9–10 أيام عمل: المرحلة 0 مع بذرة النظام (0.5–1)، الاستيراد والإنشاء والسريان (2.5)، الخصومات والعطل والأولويات في النسخة والبصمة v2 والمحرك (2)، الحوكمة والاستثناءات (1.5)، المدخلات اليدوية (1)، الاختبارات والقبول والوثائق (1.5).
