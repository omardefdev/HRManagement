# دراسة رحلة إنهاء الخدمة (SEP) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** رحلة إنهاء الخدمة من طرفها إلى طرفها (FUN-002):
- **البدء:** أنواع الإنهاء (استقالة، تقاعد بالسن أو بالمدة، صرف تأديبي، وفاة، انتهاء عقد، عدم لياقة صحية، نقل إلى جهة أخرى)، ومن يبدأ (الموظف عبر ESS، أو HR، أو النظام عند سن التقاعد)، ومهل الإشعار لكل نظام خدمة.
- **في HR.Contracts:** `SeparationRecords`، `SeparationWizard`، و`ServiceTransactions`/`AdministrativeDecisions` المرتبطة، وحالة السجل والاعتماد والوثائق.
- **قائمة الإخلاء عبر الخدمات وsaga الفصل:** Admin.Logistics، IT.Infra، Auth.Directory/Keycloak، IDCard، Records.Archive، HR.Health، السلف والقروض.
- **التسوية النهائية:** `EndOfService` في Payroll، وبدل الإجازات، والخصومات، وإيقاف الراتب الأخير، والتسليم إلى الرواتب. حساب الراتب الدوري نفسه يخص دراسة PAY.
- **آثار HR.Core:** حالة الموظف، وإخلاء المنصب الممول، ونطاق الوحدة، وأهلية إعادة التعيين، والاحتفاظ بالبيانات وDSAR بعد الفصل.
- **ما حولها:** الأحداث، والـsagas، ونماذج القراءة، وواجهة ESS للموظف المنتهية خدمته، والتقارير.

**المنهج:** قراءة ثابتة للشيفرة والاختبارات والإعدادات والوثائق في `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق. تتبّعنا كل أمر وحدث من المنتج إلى المستهلك عبر HR.Contracts وHR.Core وPayroll وAuth.Directory وIDCard وAdmin.Logistics وIT.Infra وRecords.Archive وHR.Health وCompliance وDev.Analytics، ثم فحصنا إعدادات الإنتاج (`docker-compose.production.yml`، `deploy/k8s`، `appsettings.Production.json`).  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات، لعدم وجود .NET SDK على الجهاز. النسخة «lite» بلا `wwwroot/lib` ولا `*.Designer.cs` ولا ModelSnapshot. كل فجوة مدعومة بمرجع ملف/سطر تحققنا منه بالعرض. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [ADR-FUN-002](ADR-FUN-002-separation-journey-closure.md) · [FUN_002_JOURNEY_MATRIX.md](FUN_002_JOURNEY_MATRIX.md) · [fun-separation/journey-matrix.json](fun-separation/journey-matrix.json) · [FUN_002_EXCEPTION_PATHS.md](FUN_002_EXCEPTION_PATHS.md) · [FUN_002_RECONCILIATION_CHECKLIST.md](FUN_002_RECONCILIATION_CHECKLIST.md) · [FUN_002_UAT_PACK.md](FUN_002_UAT_PACK.md) · [FUN_002_VERIFICATION.md](FUN_002_VERIFICATION.md) · [PROCESS_SAGAS.md](PROCESS_SAGAS.md) · [completion-backlog/01 (DOM-001)](completion-backlog/01-P0-RELEASE-BLOCKERS.md) · [completion-backlog/02 (INT-003، DATA-002)](completion-backlog/02-P1-PLATFORM-INTEGRITY.md) · [completion-backlog/04 (FUN-002)](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) · [completion-backlog/05-TRACEABILITY.md](completion-backlog/05-TRACEABILITY.md) · [completion-backlog/12](completion-backlog/12-SYRIA-BASELINE-AND-SOURCES.md) · [13](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md) · [14](completion-backlog/14-SYRIA-GO-LIVE-DECISIONS.md) · [SYR_003_SERVICE_REGIMES.md](SYR_003_SERVICE_REGIMES.md) · [syr/legal-source-binding.md](syr/legal-source-binding.md) · [syr/service-regimes.json](syr/service-regimes.json) · [syr/records-retention-matrix.json](syr/records-retention-matrix.json) · [syr/insurance-jurisdiction-map.json](syr/insurance-jurisdiction-map.json) · ودراسة [HCM_006 (ER)](HCM_006_EMPLOYEE_RELATIONS_GAP_STUDY.md) الموازية لنقاط التماس مع التأديب والقضايا.

---

## 1. الخلاصة

1. **الهيكل مبني، لكن الرحلة لا تكتمل.** توجد كل اللبنات: سجل فصل مع قائمة إخلاء من 6 خطوات بمالك وSLA، ومعالج من 4 خطوات، وsaga في MassTransit بسبعة أطراف ومهلة وتدخل بشري، وحساب مستحقات نهاية الخدمة، ومستهلكون في 8 خدمات. لكن بإعدادات الإنتاج وبالواجهة الحالية **لا يمكن إكمال فصل واحد**. وعبارات «Done» و«ReadyForSign-off» في FUN-002 وDOM-001 وINT-003 لا يدعمها أي دليل قبول حي.
2. **خمسة عوائق P0:**
   - **(أ)** لا توجد في الواجهة أي أداة لإكمال خطوات قائمة الإخلاء، والنشر يرفض ما دامت الخطوات الست غير مكتملة. فلا يُنفَّذ أي فصل من الواجهة (G-SEP-01).
   - **(ب)** حزمة قواعد أنظمة الخدمة لا تُشحن مع HR.Contracts، وإعداد الإنتاج لا يعطي HR.Contracts عنوان HR.Core، فيفشل كل نشر في الحاويات. وحتى مع الحزمة، يرفض نظام `law50` أنواع الإنهاء الطبية والتأديبية والقضائية وانتهاء العقد، ولا يوجد نوع «وفاة» (G-SEP-02).
   - **(ج)** حساب الموظف المنتهية خدمته يبقى فعّالاً في Keycloak: طرف الدليل يعطّل علماً محلياً فقط، فيتخطى المستهلكان اللاحقان استدعاء Keycloak، والمزامنة معطلة في الإنتاج أصلاً (G-SEP-03).
   - **(د)** لا اعتماد ولا قرار إداري: تعريف `SEPARATION-WIZARD` وقاعدة `R-SEP-START` يتيمان، ومستخدم `hr-contracts` واحد ينشئ الفصل ويعتمده (`ApprovedBy` = المنشئ) وينفّذه، وتنفيذ المعالج بلا سياسة على مستوى الخدمة (البوابة وحدها تحصره بـ `HrContractsApprove`) (G-SEP-04).
   - **(هـ)** المنصب الممول لا يُخلى بعد الفصل، فيبقى `Occupied` بموظف منتهية خدمته، ويُرفض طلب الشاغر البديل (G-SEP-05).
3. **ثغرات سلامة في القرار والمال (P1):**
   - قائمة الإخلاء إقرار ذاتي من HR، والعهدة «تُعاد» آلياً دون تسليم.
   - مسار الإلغاء الموثق لا يمكن الوصول إليه، والتعويض يمحو سجلات لا تخص الفصل، ولا يعيد ملف الأجر.
   - التسوية تُحسب من تاريخ آخر عقد لا من تاريخ التعيين، وتتجاهل نوع الإنهاء، وتحسم السلف مرتين.
   - التسوية تُنشأ بربط كيان مباشر، وتُعلَّم «مصروفة» بلا أمر دفع.
   - خمسة مداخل متوازية لإنهاء الخدمة بآثار متناقضة.
   - لا تحقق من الموظف والعقد ولا منع للتكرار.
   - بوابة الحجز القانوني مفتوحة دائماً في الإنتاج.
   - التأمين الصحي خارج الـsaga.
   - إعادة التعيين متعذرة كما وُثّقت.
4. **فجوات قدرات (P2):** لا استقالة ذاتية، ولا مهلة إشعار، ولا إحالة آلية عند سن التقاعد، ولا وفاة مع ورثة. ولا شهادة خدمة ولا براءة ذمة ولا قرار PDF. ولا واجهة ESS للموظف المنتهية خدمته، ولا تقارير. وDSAR لا يغطي العقود، والاحتفاظ 10 سنوات بدل 75.
5. **فجوة تحقق:** لا سيناريو قبول لإنهاء الخدمة ضمن AC-01..37، ولا مجلد أدلة. واختبار E2E الوحيد يفتح صفحة المعالج فقط. واختبارات التكامل تستخدم طرفاً وهمياً واحداً، وتفرض حالات لا يصل إليها التطبيق، وترسل `SchemaVersion = 1` الذي لا يرسله المنتج.

**التقدير الإجمالي:** نحو 55–70 يوم عمل لمطوّر واحد، منها 29–37 يوماً للخطط الحاملة لعوائق P0 (SEP-A إلى SEP-D).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | البدء: الأنواع والقنوات | 7 أنواع في `SeparationType` بلا «وفاة» ولا «نقل خارجي». قناة HR فقط (المعالج و`SeparationRecords/Create`). قنوات واردة من Health وCompliance بلا ضبط. لا ESS ولا مهمة سن التقاعد | ❌ (G-SEP-02، G-SEP-10، G-SEP-15) |
| 2 | التحقق من الموظف والعقد | لا تحقق من وجود الموظف أو حالته أو انتماء العقد له، ولا منع لسجل ثانٍ | ❌ (G-SEP-11) |
| 3 | الاعتماد والقرار الإداري | لا مسار ولا قرار. `ApprovedBy` = المنشئ. المعاملة تُدفع إلى `Approved` بلا معتمد | ❌ (G-SEP-04) |
| 4 | قائمة الإخلاء (DOM-001) | 6 خطوات إلزامية بمالك وSLA. لا واجهة لإكمالها، ولا فحص لدور المالك، والدليل نص حر | ❌ (G-SEP-01، G-SEP-06) |
| 5 | النشر وبدء الـsaga | فحص تاريخ النفاذ ثم القائمة ثم الحجز القانوني ثم نظام الخدمة، ثم `IStartSeparationSaga` عبر Outbox | ❌ في الإنتاج (G-SEP-02، G-SEP-12) |
| 6 | طرف Payroll | مسودة `EndOfServiceSettlement` وتعطيل ملف الأجر واسترداد السلف | ⚠️ أساس الحساب خاطئ (G-SEP-08) |
| 7 | طرف Auth.Directory | `AuthUser.IsActive=false` محلياً فقط | ❌ (G-SEP-03) |
| 8 | طرف IDCard | إلغاء البطاقات النشطة والمفقودة | ✅ التنفيذ، ⚠️ التعويض (G-SEP-07) |
| 9 | طرف Admin.Logistics | إرجاع آلي لكل عهدة مفتوحة دون دليل | ❌ (G-SEP-06) |
| 10 | طرف IT.Infra | سحب الصلاحيات وفك ربط الأجهزة | ⚠️ (G-SEP-06، G-SEP-07) |
| 11 | طرف Records.Archive | قائمة أرشفة مرحلية، وعنصر أرشيف بعد `ISeparationCompleted` فقط | ✅ مع احتفاظ خاطئ (G-SEP-17) |
| 12 | HR.Health (التأمين) | المستهلك موجود لكنه ليس طرفاً في الـsaga | ❌ (G-SEP-13) |
| 13 | HR.Core | الطرف إقرار فارغ. الحالة تتغير بعد `ISeparationCompleted`. لا إخلاء للمنصب | ❌ (G-SEP-05، G-SEP-13) |
| 14 | الإلغاء والتعويض والتدخل | إلغاء ناعم قبل النشر. الإلغاء بعد النشر غير قابل للوصول. تعويض غير محصور | ❌ (G-SEP-07) |
| 15 | اعتماد التسوية وصرفها | `Approve` و`MarkPaid` بفصل معتمد/صارف، و`Create` يربط الكيان مباشرة، ولا أمر دفع | ⚠️ (G-SEP-09) |
| 16 | ما بعد الفصل | لا ESS ولا وثائق ولا تقارير، وDSAR جزئي، والاحتفاظ 10 سنوات | ❌ (G-SEP-16، G-SEP-17) |
| 17 | إعادة التعيين | الحالة النهائية لا تعود إلى `Active`، وحساب الدليل معطّل، والخصومات القديمة تُطبَّق | ❌ (G-SEP-14) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **نموذج بيانات سليم في أساسه:** `SeparationRecord` بإلغاء ناعم (`IsCancelled` وسبب وتاريخ)، و`SeparationChecklist` بمالك وSLA ومُنفّذ ودليل ومصدر، مع فهرس فريد `(SeparationRecordId, Step)` ([ContractsDbContext.cs:248](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs)). والحذف يمر بالإلغاء الناعم ([SeparationRecordHandlers.cs:111-121](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)).
- **DOM-001 أزال الإكمال الآلي:** النشر لا يكمل `ContractTerminated` ولا أي خطوة أخرى، وإنهاء العقد إجراء صريح عند إكمال تلك الخطوة (`:149-150`، `:243-245`).
- **بوابات النشر:** تاريخ النفاذ، واكتمال الخطوات، والحجز القانوني (من حيث المبدأ)، ولقطة نظام الخدمة (`SourceLegalRegimeId`/`PayrollRegimeCode`) التي تصل إلى الرواتب.
- **saga حقيقية:** `SeparationStateMachine` بمستودع EF وتزامن تشاؤمي، وOutbox في HR.Contracts ([Program.cs:65-80](../src/Services/HRContracts/HR.Contracts.Web/Program.cs))، ومهلة تفتح `ProcessIntervention`، وواجهة استئناف وإلغاء، ومقاييس `saga_*`. ولا يُعلَن الاكتمال قبل إقرار الأطراف السبعة.
- **أطراف قابلة لإعادة الإرسال في التنفيذ:** كل طرف يرشّح بالحالة (`IsActive`، `ReturnedAt == null`، بطاقة `Active`)، والرواتب idempotent بـ`SeparationRecordId` مع فهرس فريد مشروط ([PayrollDbContext.cs:312-314](../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)).
- **أرشفة بعد النجاح فقط:** المعاملة لا تُؤرشف عند النشر بل بعد `ISeparationSagaFinalized` ([SagaSupportConsumers.cs:56-94](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs))، وعنصر الأرشيف النهائي بعد `ISeparationCompleted` فقط.
- **HR.Core:** آلة حالات للموظف بحالات نهائية وسجل تاريخ و`IEmployeeStatusChanged` ([EmploymentStatusRules.cs:7-55](../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs))، ومنع الحذف الصلب (DATA-002).
- **نموذج تعويض جيد في HR.Health:** يحصر التعويض بعلامة `separation-saga:{id}` ([HR.Health PartyAckConsumers.cs:25-129](../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/PartyAckConsumers.cs)). وهو النمط المطلوب لبقية الأطراف (G-SEP-07)، رغم أن الطرف نفسه غير موصول (G-SEP-13).
- **التسوية:** مبالغ `decimal` وتقريب، واختيار الإعداد حسب `PayrollRegimeCode`، وفصل المعتمد عن الصارف في `MarkPaid` ([EndOfServiceHandlers.cs:81-82](../src/Services/Payroll/Payroll.Application/Features/EndOfService/EndOfServiceHandlers.cs)).

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-SEP-01 — لا يمكن إكمال قائمة الإخلاء من الواجهة، فلا يُنشر أي فصل
- **الدليل:**
  - [Step2.cshtml:55-59](../src/Services/HRContracts/HR.Contracts.Web/Views/SeparationWizard/Step2.cshtml): النموذج الوحيد في الخطوة 2 يرسل إلى `Step2Confirm`. القائمة تُعرض شارات فقط، بلا زر إكمال لأي بند.
  - الإجراءان `Step2CompleteStep` ([SeparationWizardController.cs:105-122](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs)) و`OverrideChecklistStep` ([SeparationRecordsController.cs:178-200](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)) **بلا أي مرجع** في `*.cshtml` أو `*.js`. ولا واجهة لإجراء `Cancel` (`:162-176`).
  - كل سجل جديد يبذر 6 خطوات إلزامية ([SeparationChecklistDefaults.cs:8-9](../src/Services/HRContracts/HR.Contracts.Domain/Services/SeparationChecklistDefaults.cs)).
  - `Step2Confirm` يرفض ما دامت خطوة واحدة ناقصة ([SeparationWizardController.cs:124-143](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs))، و`PublishSeparationHandler` يرفض للسبب نفسه ([SeparationRecordHandlers.cs:152-161](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)).
  - صفحة التفاصيل لا تعرض القائمة أصلاً، وتعرض زر نشر سيفشل دائماً ([Details.cshtml:21-27](../src/Services/HRContracts/HR.Contracts.Web/Views/SeparationRecords/Details.cshtml)).
- **الأثر:** يستطيع HR إنشاء سجل فصل وتعديله فقط. لا يُنفَّذ أي فصل إلا بطلب POST مصنوع يدوياً. معيار DOM-001 «لا ينشر مع عنصر مطلوب غير مكتمل» متحقق لأن **لا شيء** يُنشر.
- **الإصلاح المقترح:**
  - واجهة لكل بند في الخطوة 2 وفي التفاصيل: إكمال بدليل (مرجع سجل في خدمة المصدر أو ملف مرفوع)، ونموذج استثناء بمعتمد ثانٍ يُختار من منتقي المستخدمين ويؤكد بنفسه.
  - إعادة توزيع البنود بين ما يملكه HR قبل النشر وما تُكمله الأطراف بعده (انظر G-SEP-06).
  - زر إلغاء بسبب إلزامي.
- **الخطة:** [PLAN-60](acceptance-plans/PLAN-60-sep-unblock-decision-governance.md)
- **التحقق المطلوب:** موجب: سجل استقالة ← إكمال كل بند يملكه HR بدليل من الواجهة ← `Step2Confirm` ينجح. سالب: بند بلا دليل يُرفض. ومستخدم لا يملك دور مالك البند يُرفض في الخادم.

#### G-SEP-02 — النشر يفشل في بيئة الإنتاج، وأربعة أنواع إنهاء مسدودة قانونياً
- **الحالة:** ⚠️ عولج جزء الشحن والتوصيل في [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) — الحزمة في صورة HR.Contracts وعنوان HR.Core في إعدادات الإنتاج، وبوابة الحجز القانوني تُسجَّل من المفتاح المعتمد (مصادقتها ← PLAN-42)؛ إنشاء فصل استقالة لموظف law50 ينجح على الملف الشبيه بالإنتاج (AC-INT-37e). كتالوج أنواع الإنهاء لكل نظام (الجزء القانوني) ← PLAN-61.
- **الدليل:**
  - النشر يستدعي `SeparationRegimeValidationService` (`:163-178`)، الذي يستدعي `ServiceRegimeSeparationRules.ValidateSeparationType`. وهذه تعيد `RegimeRulesNotFoundMessage` إذا لم تُحمَّل الحزمة ([ServiceRegimeSeparationRules.cs:62-75](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeSeparationRules.cs)، و`LoadRules` `:91-95` يعيد قاموساً فارغاً).
  - مواضع البحث عن الحزمة ([ServiceRegimeJsonPackPaths.cs:6-20](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeJsonPackPaths.cs)): `seed-packs/syr003-service-regimes-v1/regimes.json` بجوار التجميع، أو `docs/syr/service-regimes.json` صعوداً في الشجرة.
  - المشروع الوحيد الذي ينسخ الملف إلى المخرجات هو [HR.Core.Infrastructure.csproj:16](../src/Services/HRCore/HR.Core.Infrastructure/HR.Core.Infrastructure.csproj). مشاريع HR.Contracts لا تفعل ذلك. و[DotnetService.Dockerfile](../docker/DotnetService.Dockerfile) لا ينسخ إلا `/app/publish`. **يحتاج تحقق** تشغيلاً، لكن المتوقع أن كل نشر في الحاوية يُرفض برسالة «لا توجد قواعد فصل مهيأة لهذا النظام».
  - إعداد الإنتاج لـ `hr-contracts-web` ([docker-compose.production.yml:279-292](../docker-compose.production.yml)) بلا `Integration__HrCoreBaseUrl`، ونطاقه `file-storage.references` فقط بلا `hr-core.lookup`. وكذلك `hr-app-config` في `deploy/k8s/base`. فيقع العميل على `http://localhost:5010` ([HrCoreActiveRegimeClient.cs:33, 45](../src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreActiveRegimeClient.cs)) ويرمي استثناءً، فالنتيجة خطأ 500.
  - حتى مع الحزمة: [service-regimes.json:100-103](syr/service-regimes.json) يسمح لـ `law50` بـ `resignation/retirement/termination` فقط، ولـ `law53` بـ `resignation/contract_end/termination`. لا نظام يسمح بـ `medical` أو `judicial` أو `disciplinary` (`ToSeparationTypeCode` `:78-89`).
  - لا نوع «وفاة» في [SeparationType.cs:5-21](../src/Services/HRContracts/HR.Contracts.Domain/Enums/SeparationType.cs)، رغم أن HR.Core يعرف `death/deceased` ([EmploymentStatusRules.cs:86](../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs)).
  - فحص النظام يأتي **بعد** فحص القائمة (`:152-161` ثم `:163-178`). وخطوة `ContractTerminated` أنهت العقد سابقاً (`:243-245`). فالنوع المرفوض يترك عقداً منتهياً وسجلاً عالقاً.
- **الأثر:** في الإنتاج لا يكتمل أي فصل. والحالات الأشد حساسية (الصرف التأديبي، التقاعد الصحي، الحكم القضائي، الوفاة) بلا مسار حتى في المختبر.
- **الإصلاح المقترح:**
  - تضمين الحزمة كمورد مضمَّن في `SharedKernel`، أو قراءتها من HR.Core بنسخة وhash، مع فشل صريح عند الإقلاع إن غابت.
  - فحص النوع مقابل النظام عند **الإنشاء** لا بعد الإخلاء.
  - كتالوج أنواع كامل بقرار D-SEP-1.
  - إعداد S2S لـ HR.Contracts في الإنتاج (→ OPS).
- **الخطة:** [PLAN-61](acceptance-plans/PLAN-61-sep-legal-types-single-entry.md) (كتالوج الأنواع لكل نظام ونوع الوفاة والفحص عند الإنشاء) + [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) (شحن الحزمة وعنوان HR.Core ونطاقه)
- **التحقق المطلوب:** موجب: على ملف الإنتاج الشبيه (حاويات مبنية)، نشر استقالة لموظف `law50` ينجح. سالب: نوع غير مسموح يُرفض عند الإنشاء برسالة واضحة، والعقد يبقى `Active`.

#### G-SEP-03 — حساب الموظف المنتهية خدمته يبقى فعّالاً في Keycloak
- **الدليل:**
  - طرف الدليل في الـsaga يضبط `IsActive=false` محلياً فقط، ولا يستدعي Keycloak ([Auth PartyAckConsumers.cs:12-22](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)).
  - بعد ذلك يتخطى [SeparationCompletedConsumer.cs:87-89](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/SeparationCompletedConsumer.cs) العمل: `if (!user.IsActive) return;`، فلا يصل إلى `TryDisableUserAsync` (`:103`).
  - وكذلك [EmployeeStatusChangedConsumer.cs:95-97](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/EmployeeStatusChangedConsumer.cs): `if (user.IsActive == !inactive) return;`، فلا يصل إلى `:113`.
  - المزامنة معطلة افتراضياً: `DirectorySync.Enabled=false` ([appsettings.json:24](../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json))، ولا يفعّلها `appsettings.Production.json` ولا `docker-compose.production.yml` (يضبط السر فقط، `:524`). فيعيد [KeycloakUserProvisioningService.cs:289-294](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs) `Disabled` بصمت.
  - لا فحص لـ `AuthUser.IsActive` عند الطلب: `OnTokenValidated` يربط الأدوار فقط ([KeycloakExtensions.cs:99-106](../src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs)).
  - الاختبار يتحقق من العلم المحلي فقط ([PeripheralConsumerIntegrationTests.cs:86](../src/Tests/HRManagement.IntegrationTests/PeripheralConsumerIntegrationTests.cs)).
- **الأثر:** الموظف المنتهية خدمته يحتفظ بدخول SSO وبأدواره. يستطيع فتح الوحدات وESS، وقد يعتمد خطوات سير عمل إن كان معتمداً. هذا يناقض U1 «لا وصول» في [FUN_002_UAT_PACK.md:5](FUN_002_UAT_PACK.md).
- **الإصلاح المقترح:**
  - الطرف نفسه يعطّل المستخدم في Keycloak ويلغي جلساته، ويعيد `Faulted` إن فشل الاستدعاء أو كانت المزامنة معطلة. لا نجاح صامت.
  - تفعيل المزامنة في الإنتاج (→ OPS).
  - اختيارياً: رفض الرمز لمستخدم معطّل محلياً.
  - التعويض يعيد تفعيل الحساب الذي عطّله هذا الفصل فقط (G-SEP-07).
- **الخطة:** [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md)
- **التحقق المطلوب:** موجب: بعد اكتمال الفصل، دخول الشخصية مرفوض في Keycloak، والجلسة القائمة ملغاة. سالب: Keycloak غير متاح ← الطرف `Faulted` ← تدخل بشري، ولا `ISeparationCompleted`.

#### G-SEP-04 — لا اعتماد ولا قرار إداري: مستخدم واحد يُنهي خدمة أي موظف
- **الدليل:**
  - التعريف `SEPARATION-WIZARD` (خطوتان: `hr-core` ثم `compliance`، [WorkflowPackSeeder.cs:32-34](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)) والقاعدة `R-SEP-START` ([PolicyEngineDomainSeeds.cs:47-52](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)) يتيمان. لا يستدعي مسار الفصل في HR.Contracts محرك السياسات ولا سير العمل؛ الخدمة تستدعي المحرك للإجازات فقط ([LeaveRequestHandlers.cs:49](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - حارس PLAN-25 يستثني `SeparationRecord` بسبب غير صحيح: «HR.Contracts publishes the separation» ([WorkflowDefinitionsHaveHandlersTests.cs:18](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs)).
  - `ApprovedBy` هو المنشئ: [SeparationWizardController.cs:68](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs) و[SeparationRecordsController.cs:91, 209-210](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs).
  - سياسة `HrContractsApprove` = `Admin` أو `HrContracts` ([HrManagementAuthorizationExtensions.cs:126-128](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، وهو الدور نفسه الذي ينشئ. المنشئ هو المعتمد.
  - `Step3Execute` في المعالج (`:153-165`) بلا سياسة على مستوى الخدمة، ويكفيه `ModuleHrContracts` الذي يشمل `hr-management-approver` ([HrManagementAuthorizationExtensions.cs:28-31](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، بخلاف `PublishSeparation` ([SeparationRecordsController.cs:148](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)). لكن البوابة تفرض `HrContractsApprove` على كل POST إلى `hr-contracts` خارج `HireWizard` ([GatewayPathAuthorizationMiddleware.cs:222-226](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs))، والخدمة بلا منافذ في الإنتاج ([docker-compose.production.yml:281](../docker-compose.production.yml)). فلا يصل `hr-management-approver` إلى التنفيذ عملياً، والنقص دفاع في العمق على مستوى الخدمة فقط.
  - المعاملة تُدفع آلياً من `Draft` إلى `Executing` عبر `UnderReview` و`PendingApproval` و`Approved` بمستخدم `null` ([SeparationRecordHandlers.cs:200-211](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)). هذا تاريخ اعتماد مُصطنع.
  - لا آلة حالات للسجل. علمان فقط: `SeparationEventPublished` و`IsCancelled` ([SeparationRecord.cs:36, 47](../src/Services/HRContracts/HR.Contracts.Domain/Entities/SeparationRecord.cs)). لا تميز الواجهة بين «قيد التنفيذ» و«مكتمل» و«عالق في تدخل».
  - لا قرار إداري: `Documents` نص حر (`:33`)، ولا ربط بـ `AdministrativeDecision`، بخلاف التعيين الذي يملك `AppointmentDecisionId` منذ PLAN-29.
  - الاستثناء على بند يأخذ المعتمد الثاني GUID من النموذج (`approvedBy`، [SeparationRecordsController.cs:181](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)). لا فحص إلا أنه يختلف عن المنفّذ ([SeparationRecordHandlers.cs:274-277](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)). هذا معتمد مزوَّر.
- **الأثر:** بعد إصلاح G-SEP-01 يستطيع مستخدم `hr-contracts` واحد إنهاء خدمة أي موظف، وإيقاف راتبه وتعطيل حسابه، دون مراجعة ولا قرار موقّع. وهذا نمط «التطبيق عند التقديم» نفسه الذي وجده PLAN-25 في الملاك. *(صُحّح بعد التحقق المستقل: البوابة تحصر كل POST على `hr-contracts` بـ `HrContractsApprove`، فـ `hr-management-approver` لا ينفّذ عبر المعالج في الإنتاج؛ وHR.Contracts يستدعي محرك السياسات للإجازات لا للفصل)*
- **الإصلاح المقترح:**
  - آلة حالات: `Draft → Submitted → UnderReview → Approved → Scheduled → Executing → Completed | Cancelled`.
  - اعتماد عبر `SEPARATION-WIZARD` بفحص دور الخطوة في الخادم (→ WF لآلية الربط)، ومنع المنشئ من الاعتماد، وسلسلة الاعتماد حسب النوع (D-SEP-4).
  - قرار إداري إلزامي (رقم، تاريخ، صاحب الصلاحية، PDF) قبل التنفيذ.
  - معاملة مربوطة بـ `SeparationRecordId` تتحرك بفاعلين حقيقيين.
  - سياسات صريحة على كل إجراءات POST في المعالج والسجلات.
- **الخطة:** [PLAN-60](acceptance-plans/PLAN-60-sep-unblock-decision-governance.md)
- **التحقق المطلوب:** موجب: اعتماد خطوتين بمستخدمين مختلفين ثم قرار ثم تنفيذ، بإعدادات الإنتاج. سالب: المنشئ يحاول الاعتماد فيُرفض في الخادم. معتمد بدور خاطئ يُرفض. `hr-management-approver` لا ينفّذ عبر المعالج. لا نشر بلا قرار.

#### G-SEP-05 — المنصب الممول لا يُخلى بعد الفصل
- **الدليل:**
  - طرف HR.Core في الـsaga إقرار فارغ ([ProcessPartyAckConsumers.cs:36-52](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/ProcessPartyAckConsumers.cs)).
  - [SeparationCompletedConsumer.cs:29-86](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/SeparationCompletedConsumer.cs) يغيّر الحالة ويضيف `EmploymentRecord`، ولا يستدعي `VacatePosition`.
  - `EstablishmentService.VacatePosition` ([EstablishmentService.cs:36-44](../src/Services/HRCore/HR.Core.Domain/Services/EstablishmentService.cs)) لا يُستدعى إلا في تعويض التعيين ([ProcessPartyAckConsumers.cs:145](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/ProcessPartyAckConsumers.cs)) وفي التنقل الداخلي ([RecruitmentHireClosureService.cs:257](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs)).
  - لا إخلاء يدوي في واجهة HR.Core (بحث «Vacat» في المتحكمات والصفحات: صفر)، ولا عملية إخلاء في `EstablishmentChangeApplier`.
  - طلب الشاغر يشترط منصباً `Vacant` ([RequisitionWorkflowRules.cs:77-78](../src/Services/HRCore/HR.Core.Domain/Services/RequisitionWorkflowRules.cs)).
- **الأثر:** بعد كل فصل يبقى المنصب `Occupied` بموظف منتهية خدمته. مؤشرات الشواغر خاطئة. ولا يمكن فتح طلب شاغر بديل، فتتوقف رحلة HCM-001 على هذا المنصب.
- **الإصلاح المقترح:**
  - طرف HR.Core (أو مستهلك الاكتمال) يُخلي المنصب في تاريخ النفاذ، ويسجل نسخة `FundedPositionVersion` بعملية `Vacated`.
  - التعويض يعيد الإشغال إن بقي المنصب شاغراً، وإلا يفتح تدخلاً.
  - تنبيه على `OrgUnit.HeadEmployeeId` والتفويضات النشطة للموظف (G-SEP-17).
- **الخطة:** [PLAN-63](acceptance-plans/PLAN-63-sep-saga-compensation-core-effects.md)
- **التحقق المطلوب:** موجب: بعد الفصل المنصب `Vacant` وله نسخة `Vacated`، وطلب الشاغر البديل يُنشأ. سالب: إلغاء الفصل يعيد الإشغال. وإن أُسند المنصب لغيره قبل الإلغاء، يُفتح تدخل ولا يُكتب فوقه.

### 4.2 ثغرات سلامة القرار والمال (P1)

#### G-SEP-06 — قائمة الإخلاء إقرار ذاتي، والعهدة «تُعاد» آلياً دون تسليم
- **الدليل:**
  - `CompleteSeparationChecklistStepHandler` لا يقارن دور المستخدم بـ `OwnerRole` ([SeparationRecordHandlers.cs:218-265](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)). والمعالج يمرر `SourceSystem: "HRContracts"` ويجعل الملاحظة الحرة دليلاً ([SeparationWizardController.cs:108-114](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs)). ويقبل `CompletedBy = null` لأن الفحص يرفض `Guid.Empty` فقط (`:240`).
  - القائمة تشترط `PayrollSettled` و`CardRevoked` و`AccountDisabled` و`AssetsReturned` و`Archived` **قبل** النشر (`:152-161`)، بينما هذه الأفعال تنفذها الأطراف **بعد** النشر. فيشهد موظف HR على أحداث لم تقع. و`SeparationPartyCompletedChecklistConsumer` يختم دليل الرواتب فقط ولا يضبط `IsCompleted` ([SagaSupportConsumers.cs:97-119](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs)).
  - طرف Admin.Logistics يضبط `ReturnedAt = now` لكل عهدة مفتوحة، ويحوّل الأصل إلى `InStorage` دون أي دليل ([Admin.Logistics PartyAckConsumers.cs:13-32](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)). بينما الإرجاع اليدوي للأصل المميز يشترط دليل تسليم ([AssetsController.cs:509](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)).
  - ولا يضبط `Status = Returned`، فيبقى السجل `Active` مع `ReturnedAt`. ثم لا يجد [SeparationCompletedConsumer.cs:63](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/SeparationCompletedConsumer.cs) شيئاً لأنه يبحث عن `ReturnedAt == null`.
  - طرف IT.Infra يفك ربط الأجهزة (`AssignedTo = null`) دون معرفة مكانها ([IT PartyAckConsumers.cs:22-26](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs)).
  - لا بلاغ فقد ولا تحميل للعهدة غير المعادة.
  - DOM-001 يدّعي «كل عنصر مكتمل يملك actor/time/evidence/source service» ([01-P0-RELEASE-BLOCKERS.md:133](completion-backlog/01-P0-RELEASE-BLOCKERS.md)).
- **الأثر:** يغادر الموظف ومعه الحاسوب أو السيارة، والنظام يقول إنها أُعيدت. لا مسؤولية ولا خصم ولا أثر للأصل.
- **الإصلاح المقترح:**
  - الأطراف تُبلغ عن البنود المفتوحة (حالة انتظار أو `Faulted` بقائمة) بدل إغلاقها.
  - الإرجاع بالمسار القائم `ReturnAssignment` مع الدليل.
  - العهدة غير المعادة تفتح `AssetLossReport` وقرار تحميل (D-SEP-5).
  - بنود الأطراف تُكمَل بإقرار الطرف لا بيد HR، وبنود HR تُكمَل بدور المالك.
- **الخطة:** [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md)
- **التحقق المطلوب:** موجب: موظف بحاسوب مميز ← الـsaga تنتظر ← إرجاع بدليل ← اكتمال. سالب: HR لا يستطيع تعليم `AssetsReturned`، والأصل لا يصبح `InStorage` دون تسليم.

#### G-SEP-07 — الإلغاء والتعويض والتدخل: مسار موثق لا يمكن الوصول إليه، وتعويض يفسد بيانات أخرى
- **الدليل:**
  - **مسار غير قابل للوصول:** النشر يشترط `EffectiveDate <= today` ([SeparationRecordHandlers.cs:136-137](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)). والإلغاء بعد النشر يشترط `EffectiveDate > today` ([SeparationWorkflowRules.cs:15-20](../src/Services/HRContracts/HR.Contracts.Domain/Services/SeparationWorkflowRules.cs)). والتعديل بعد النشر ممنوع (`:96`). فلا يصل أي طلب إلى `CancelSeparationSagaAsync` (`:317-323`). والاختبار يفرض الحالة المستحيلة يدوياً ([Fun002SeparationRecordIntegrationTests.cs:98](../src/Tests/HRManagement.IntegrationTests/Fun002SeparationRecordIntegrationTests.cs)). سيناريو U4 وسطر [FUN_002_EXCEPTION_PATHS.md:7](FUN_002_EXCEPTION_PATHS.md) لا يتحققان.
  - **الإلغاء الوحيد بعد النشر** هو `ProcessInterventionsController.Cancel` ([ProcessInterventionsController.cs:91-127](../src/Services/HRContracts/HR.Contracts.Web/Controllers/ProcessInterventionsController.cs)). لا يفحص السجل، ولا يلغيه إلغاءً ناعماً، ولا يلغي المعاملة، فتبقى `Executing`.
  - **تعويض غير محصور:**
    - Admin.Logistics يعيد فتح **كل** عهدة أُعيدت للموظف في أي وقت ([PartyAckConsumers.cs:34-53](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)، الاستعلام `:38`).
    - IT.Infra يعيد **كل** صلاحية سُحبت ولا يعيد الأجهزة ([PartyAckConsumers.cs:34-49](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs)).
    - Auth.Directory يعيد تفعيل **كل** حساب معطّل للموظف، ولو عُطّل لسبب أمني ([PartyAckConsumers.cs:24-34](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)).
    - IDCard يُلغي البطاقة «المفقودة» عند التنفيذ ثم يعيدها `Active` عند التعويض ([IDCard PartyAckConsumers.cs:14-54](../src/Services/IDCard/IDCard.Infrastructure/Consumers/PartyAckConsumers.cs)).
  - **الرواتب:** التعويض يلغي المسودة فقط ([PayrollEventConsumers.cs:93-104](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)). ملف الأجر الذي عُطّل ([InboundEventHandlers.cs:212-213](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) يبقى معطلاً، والخصم `AdvanceFinalRecovery` يبقى، والتسوية المعتمدة تبقى.
  - **الـsaga:** في `Compensating` يقود `PartyFaulted` إلى `AwaitingIntervention` ([SeparationStateMachine.cs:197-204](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs)). و`Resume` هناك يعيد إرسال الأطراف المعلقة بـ `Compensate = false` (`:163-176`، `:239-258`). فتُنفَّذ الأطراف تنفيذاً أمامياً، وإن أقرّت كلها يُنشر `ISeparationCompleted` لفصل ملغى (`:125-131`). الإقرارات المتأخرة في `AwaitingIntervention` تُهمل (`:183`)، والحالة `Faulted` غير مستخدمة.
  - **الإلغاء قبل النشر** لا يعيد العقد الذي أُنهي عند إكمال الخطوة (`:243-245` مقابل `:303-335`).
- **الأثر:** الفصل الملغى يترك موظفاً بلا راتب، وعقداً منتهياً، وعهداً وصلاحيات قديمة مفتوحة من جديد، وحساباً معطلاً أمنياً أُعيد تفعيله. أو يكتمل فصل ملغى.
- **الإصلاح المقترح:**
  - تعويض محصور بعلامة `separation-saga:{id}` في كل طرف، على نمط HR.Health.
  - الرواتب تعيد ملف الأجر وتلغي خصم الاسترداد النهائي.
  - علم «تعويض» محفوظ في حالة الـsaga يحكم `Resume`.
  - الإلغاء أمر على السجل يحدّث السجل والمعاملة والـsaga معاً، بنافذة إلغاء يحددها D-SEP-6.
  - إعادة العقد عند الإلغاء.
- **الخطة:** [PLAN-63](acceptance-plans/PLAN-63-sep-saga-compensation-core-effects.md)
- **التحقق المطلوب:** موجب: تنفيذ ثم إلغاء يعيد كل طرف إلى حالته السابقة حرفياً، والسجلات التاريخية لا تُمس، والموظف يظهر في التشغيل التالي. سالب: فشل طرف أثناء التعويض ثم `Resume` يكمل التعويض، ولا يُنشر `ISeparationCompleted`.

#### G-SEP-08 — التسوية النهائية تُحسب على أساس خاطئ
- **الدليل:**
  - **مدة الخدمة** تبدأ من `profile.EffectiveFrom` ([InboundEventHandlers.cs:225](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). وهذا التاريخ يعاد ضبطه عند كل تفعيل عقد (`:58`، `:77`). فالمتعاقد المجدَّد سنوياً يحصل على مدة سنة واحدة. والقسمة على 365.25 ([EndOfServiceCalculation.cs:21](../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs)).
  - **نوع الإنهاء لا يؤثر في المبلغ:** المصروف تأديبياً يحصل على مكافأة المتقاعد نفسها. النوع يظهر في النص فقط.
  - **الذمة تختفي:** `NetSettlement = Math.Max(0, netSettlement)` ([EndOfServiceCalculation.cs:153](../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs)). ما يزيد على الاستحقاق من دين يضيع.
  - **السلف تُحسم مرتين:** الرصيد يُطرح من التسوية (`:215-219`، و[EndOfServiceCalculation.cs:127](../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs))، ويُنشأ أيضاً خصم `AdvanceFinalRecovery` (`:235-243`). وتبقى أقساط `AdvanceRecovery` الأصلية غير مطبقة. والمحمّل يلتقط كل خصم غير مطبق بالموظف ([PayrollPartitionInputLoader.cs:127-129](../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs)). فعند إعادة التعيين تُسترد السلفة مرتين إضافيتين.
  - **الشهر الأخير:** التشغيل يختار الملفات النشطة فقط ([PayrollRunJobHandlers.cs:90-91](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs))، والفصل يعطّل الملف. والتسوية بلا مكوّن «راتب الأيام الأخيرة». فإن سبق النشرُ التشغيلَ لا تُدفع أيام العمل الأخيرة. وإن سبق التشغيلُ النشرَ (والنشر يأتي بعد النفاذ بأيام أو أسابيع) يُدفع الشهر كاملاً بلا استرداد. محرك التناسب نفسه يخص PAY.
  - **غياب الملف النشط** (مثلاً بعد التعطيل التأديبي، انظر G-ER-02) يتخطى إنشاء التسوية كلها (`:205-206`). ومع ذلك يُقر الطرف بـ `payroll:settled` ([PayrollEventConsumers.cs:65](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)) وتكتمل الـsaga بلا تسوية.
  - **الإجازات:** رصيد سنة تاريخ النفاذ فقط، عند لحظة النشر، بلا سقف ([SeparationRecordSupport.cs:31-45](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordSupport.cs)).
  - **قيم صلبة:** 0.5 و5 و1.0 و30 كقيم احتياطية ([EndOfServiceCalculation.cs:41-45, 89](../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs)). والرجوع الصامت إلى الإعداد النشط عند غياب إعداد النظام ([InboundEventHandlers.cs:258-269](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). والمعادلة «ApprovedInterim» ([LegalDecisionRegisterSeed.cs:17](../src/Services/Payroll/Payroll.Domain/Services/LegalDecisionRegisterSeed.cs)).
- **الأثر:** مبالغ خاطئة في الاتجاهين لكل موظف تقريباً، وذمم تضيع، وراتب أخير مفقود أو مكرر.
- **الإصلاح المقترح:**
  - سجل خدمة من HR.Core (تاريخ التعيين الأول والانقطاعات).
  - جدول قواعد حسب النظام والنوع (D-SEP-2).
  - مكوّن راتب نهائي متناسب حتى تاريخ النفاذ، واسترداد ما دُفع زيادة.
  - دفتر سلف واحد يُغلق عند التسوية.
  - صافٍ سالب يتحول إلى ذمة.
  - سقف الإجازات (D-SEP-3).
  - رفض بلا إعداد مصدَّق، و`Faulted` إن لم يوجد ملف أجر.
- **الخطة:** [PLAN-64](acceptance-plans/PLAN-64-sep-final-settlement.md)
- **التحقق المطلوب:** حالات ذهبية يعتمدها القانوني والرواتب لكل نظام ونوع: متعاقد مجدَّد 4 مرات، ومصروف تأديبياً، وسلفة نصف مسددة ثم إعادة تعيين، ونفاذ في منتصف شهر قبل التشغيل وبعده، وذمة أكبر من الاستحقاق.

#### G-SEP-09 — التسوية تُنشأ وتُعتمد وتُصرف خارج الضبط
- **الدليل:**
  - `EndOfServiceController.Create(EndOfServiceSettlement model)` يربط الكيان مباشرة ([EndOfServiceController.cs:69-80](../src/Services/Payroll/Payroll.Web/Controllers/EndOfServiceController.cs)). يمكن إدخال أي مبالغ وأي موظف وأي `SeparationRecordId`، بلا حساب.
  - لا `CreatedBy` في الكيان ([BaseEntity.cs:5-16](../src/Shared/HRManagement.SharedKernel/Entities/BaseEntity.cs)). وفحص فصل المهام في الاعتماد يقارن بـ `ApprovedBy` الفارغ في المسودة، فينجح دائماً ([EndOfServiceHandlers.cs:58](../src/Services/Payroll/Payroll.Application/Features/EndOfService/EndOfServiceHandlers.cs)). من يحسب يستطيع أن يعتمد.
  - `MarkPaid` يغيّر الحالة فقط (`:84`). لا `PaymentOrder` ولا قناة خزينة، و`PaymentOrderService` يختار الملفات النشطة فقط ([PaymentOrderService.cs:24](../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderService.cs)). فالتسوية لا تصل أبداً إلى الدفع الفعلي.
  - لا ربط بحالة الفصل: يمكن اعتماد تسوية فصل ملغى.
- **الأثر:** مال يُعتمد دون حساب قابل للتفسير ولا فصل مهام ولا أثر دفع. وهذا يناقض معيار FUN-002 «final settlement قابلة للتفسير».
- **الإصلاح المقترح:** إنشاء التسوية بأمر من الفصل فقط، واليدوي «تصحيح» بسبب. حقل `CreatedBy` وفصل المهام. حالة التسوية مرتبطة بحالة الفصل. الصرف عبر أمر دفع من مسار الرواتب.
- **الخطة:** [PLAN-64](acceptance-plans/PLAN-64-sep-final-settlement.md)
- **التحقق المطلوب:** سالب: POST يدوي بمبلغ اعتباطي يُرفض. الحاسب لا يعتمد. لا صرف دون أمر دفع. موجب: تسوية من فصل معتمد ← اعتماد ← أمر دفع ← `Paid`.

#### G-SEP-10 — خمسة مداخل متوازية لإنهاء الخدمة بآثار متناقضة
- **الدليل:**
  - **تعديل الموظف في HR.Core** يغيّر الحالة إلى `Terminated/Retired/Resigned/Deceased` مباشرة ([EmployeesController.cs:389-416](../src/Services/HRCore/HR.Core.Web/Controllers/EmployeesController.cs)، بسياسة `HrCoreWrite`). المستهلك الوحيد لـ `IEmployeeStatusChanged` هو Auth.Directory، فيستمر الراتب ويبقى المنصب مشغولاً ولا تسوية ولا إخلاء.
  - **الصرف التأديبي** في Compliance يعطّل ملف الأجر مباشرة في Payroll ([InboundEventHandlers.cs:610-614](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) دون سجل فصل. تملكه دراسة ER (G-ER-02). أثره هنا: الفصل اللاحق لا يجد ملفاً نشطاً فلا تسوية (G-SEP-08).
  - **القرار الطبي:** توصية `LimitedDuties` تنشر `IMedicalDecisionIssued` مثل `Retirement` ([MedicalDecisionWorkflow.cs:15-17](../src/Services/HRHealth/HR.Health.Domain/Services/MedicalDecisionWorkflow.cs)، [HealthExtendedControllers.cs:85](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)). فتنشئ HR.Contracts سجل فصل «طبي» حتى لمن خُففت مهامه ([InboundEventConsumers.cs:139-150](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs)). وفي التقاعد الطبي تجعل HR.Core الموظف `Retired` فوراً ([MedicalEmploymentEffectRules.cs:21-28](../src/Services/HRCore/HR.Core.Domain/Services/MedicalEmploymentEffectRules.cs))، بينما لا يمكن نشر النوع `Medical` أصلاً (G-SEP-02).
  - **إغلاق أي قضية** ينشر حكماً لـ `Guid.Empty`، فيُنشأ سجل فصل «قضائي» لموظف غير موجود ([ComplianceExtendedControllers.cs:118-126](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)). تملكه دراسة ER (G-ER-03).
  - **`IResignationSubmitted`** له مستهلك ([InboundEventConsumers.cs:153-164](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs)) ولا منتج في أي خدمة.
- **الأثر:** حالة الموظف والراتب والحساب والمنصب تتعارض بين الخدمات حسب المدخل المستخدم. وسجل الفصل يمتلئ بطلبات زائفة.
- **الإصلاح المقترح:**
  - مدخل واحد: كل مصدر ينشئ «طلب فصل» `Draft` يمر بالاعتماد (G-SEP-04).
  - منع الحالات النهائية في تعديل الموظف، فلا تأتي إلا من `ISeparationCompleted`.
  - Payroll لا يعطّل الملف عند الحدث التأديبي (مع ER-B).
  - `LimitedDuties` ليس فصلاً.
  - منتج ESS لـ `IResignationSubmitted` (G-SEP-15).
- **الخطة:** [PLAN-61](acceptance-plans/PLAN-61-sep-legal-types-single-entry.md)
- **التحقق المطلوب:** سالب: تعديل الحالة إلى `Terminated` من شاشة الموظف مرفوض. قرار `LimitedDuties` لا ينشئ سجل فصل. موجب: قرار تقاعد طبي ← طلب فصل `Draft` ← اعتماد ← saga ← حالة `Retired` من مصدر واحد.

#### G-SEP-11 — سلامة سجل الفصل: لا تحقق من الموظف والعقد، وتكرار، وتعديل بعد الإكمال
- **الدليل:**
  - الإنشاء لا يتحقق من وجود الموظف أو حالته، ولا من سجل فصل مفتوح آخر ([SeparationRecordHandlers.cs:56-88](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)، والمعالج [SeparationWizardController.cs:54-87](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs)). ولا فهرس فريد مشروط ([ContractsDbContext.cs:107-120](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs)). وidempotency الرواتب لكل سجل، فسجلان يعطيان تسويتين.
  - `ContractId` لا يُتحقق من انتمائه للموظف ([SeparationRecordSupport.cs:21-29](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordSupport.cs)). وقائمة العقود تعرض آخر 500 عقد لكل الموظفين بمعرّف GUID ([ContractLookupHandlers.cs:12-19](../src/Services/HRContracts/HR.Contracts.Application/Features/Lookup/ContractLookupHandlers.cs)). فإكمال `ContractTerminated` قد يُنهي عقد موظف آخر. والعقد اختياري، فقد يبقى عقد المفصول `Active`.
  - `Terminate` لا يضبط تاريخ نهاية العقد ([ContractWorkflowRules.cs:33-38](../src/Services/HRContracts/HR.Contracts.Domain/Services/ContractWorkflowRules.cs))، ويُنفَّذ لحظة إكمال الخطوة، ولو قبل النفاذ بأسابيع.
  - التعديل يسمح بتغيير الموظف والعقد والنوع والتاريخ بعد إكمال خطوات القائمة ([SeparationRecordHandlers.cs:90-109](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)). فتصبح أدلة الإخلاء لموظف آخر.
  - المعاملة تُلتقط بأنها «آخر معاملة فصل للموظف» (`:200-202`، `:325-328`، [SagaSupportConsumers.cs:75-80](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs)). لا حقل `SeparationRecordId` فيها ([ServiceTransaction.cs:26-54](../src/Services/HRContracts/HR.Contracts.Domain/Entities/ServiceTransaction.cs)). و`ServiceTransactionsController.Advance` يحرّكها يدوياً بمعزل عن الـsaga ([ServiceTransactionsController.cs:58-89](../src/Services/HRContracts/HR.Contracts.Web/Controllers/ServiceTransactionsController.cs)).
  - لا رمز تزامن على `SeparationRecord`، بخلاف `LeaveRequest` ([ContractsDbContext.cs:135](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs)).
- **الأثر:** فصل مكرر بتسويتين، وعقد موظف بريء يُنهى، وأدلة لا تخص صاحب السجل، ومعاملة تقول «مكتملة» وsaga عالقة.
- **الإصلاح المقترح:** تحقق S2S من الموظف وحالته، وعقد مملوك له وإلزامي إن وُجد عقد نشط. فهرس فريد لسجل مفتوح واحد لكل موظف. قفل الحقول الجوهرية بعد أول بند مكتمل. ربط المعاملة بالسجل. رمز تزامن. ضبط تاريخ نهاية العقد بتاريخ النفاذ وتنفيذه عنده.
- **الخطة:** [PLAN-60](acceptance-plans/PLAN-60-sep-unblock-decision-governance.md)
- **التحقق المطلوب:** سالب: سجل ثانٍ للموظف نفسه مرفوض. عقد لموظف آخر مرفوض. تغيير الموظف بعد إكمال بند مرفوض. موجب: العقد ينتهي بتاريخ النفاذ.

#### G-SEP-12 — بوابة الحجز القانوني مفتوحة دائماً في الإنتاج
- **الدليل:**
  - البوابة الحقيقية لا تُسجَّل إلا إذا ضُبط `Services:HRCore:BaseUrl` أو `HrCore:BaseUrl` ([DependencyInjection.cs:36-45](../src/Services/HRContracts/HR.Contracts.Infrastructure/DependencyInjection.cs)). الإنتاج يستخدم `Integration__HrCoreBaseUrl`، والمفتاح المطلوب موجود فقط في [docker-compose.dev-contracts.yml:18](../docker-compose.dev-contracts.yml). فيعمل `PassThroughEmployeeLifecycleGate` ([EmployeeLifecycleGate.cs:9-13](../src/Services/HRContracts/HR.Contracts.Infrastructure/Services/EmployeeLifecycleGate.cs)).
  - وحتى عند ضبطه: `HttpClient` عادي بلا هوية خدمة (`:30`) إلى نقطة محمية بسياسة S2S ([EmployeeLifecycleInternalController.cs:12](../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeLifecycleInternalController.cs)). النتيجة 401، ثم «allowing publish» (`:33-38`، `:47-51`). فتح عند الفشل.
  - اختبار `Publish_LegalHold_Rejected` يحقن بوابة وهمية ([Fun002SeparationRecordIntegrationTests.cs:77](../src/Tests/HRManagement.IntegrationTests/Fun002SeparationRecordIntegrationTests.cs)). وADR-FUN-002 (`:23`) وU6 يدّعيان الرفض.
- **الأثر:** تُنهى خدمة موظف تحت حجز قانوني (تحقيق أو قضية)، فتُعطَّل حساباته ويُؤرشف ملفه أثناء نزاع قائم.
- **الإصلاح المقترح:** عميل مصادَق عليه بنطاق `hr-core.lookup` ومفتاح الإعداد الموحد `Integration:HrCoreBaseUrl`، ورفض عند الفشل (fail-closed) مع رسالة. والحجز يمنع أيضاً الإنشاء والاعتماد، لا النشر وحده.
- **الخطة:** [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md)
- **التحقق المطلوب:** موجب: موظف تحت حجز ← رفض في الإنشاء والنشر. سالب: HR.Core غير متاح ← رفض لا سماح.

#### G-SEP-13 — أطراف وآثار ناقصة، وعقد حدث بإصدار يتخطاه المستهلكون
- **الدليل:**
  - `MandatoryParties` السبعة لا تشمل `HR.Health` ([SeparationStateMachine.cs:39-49](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs)). ولا يوجد قسم `Sagas` في أي `appsettings` أو compose. فمستهلك التأمين ([HR.Health PartyAckConsumers.cs:25](../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/PartyAckConsumers.cs)) لا يُستدعى، وتبقى بطاقات التأمين `Active`.
  - لا مستهلك للفصل في HR.Contracts للإجازات المعتمدة المستقبلية أو السلف المعلقة، ولا في HR.Training. ولا مفهوم «التزام خدمة مقابل التدريب».
  - HR.Core يسجل تحذيراً فقط إن رُفض انتقال الحالة، مثلاً لموظف صار `Retired` بالمسار الطبي ([SeparationCompletedConsumer.cs:63-68](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/SeparationCompletedConsumer.cs)). لا `Faulted` ولا تدخل. ونوع التوظيف في سجل «إنهاء خدمة» ثابت `Permanent` (`:49`).
  - **عقد الحدث:** `ISeparationCompleted.SchemaVersion => 3` ([ISeparationCompleted.cs:22](../src/Shared/HRManagement.MessageContracts/Events/ISeparationCompleted.cs)). الـsaga لا تضبطه ([SeparationStateMachine.cs:263-276](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs)). وأربعة مستهلكين يتخطون ما فوق 2: [Admin.Logistics:51](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/SeparationCompletedConsumer.cs)، [IDCard:17](../src/Services/IDCard/IDCard.Infrastructure/Consumers/SeparationCompletedConsumer.cs)، [IT.Infra:27](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/SeparationCompletedConsumer.cs)، [Auth.Directory:59](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/SeparationCompletedConsumer.cs). والاختبارات ترسل `SchemaVersion = 1` ([PeripheralConsumerIntegrationTests.cs:81, 117, 151, 177](../src/Tests/HRManagement.IntegrationTests/PeripheralConsumerIntegrationTests.cs)). **يحتاج تحقق:** هل يحمل الكائن المولَّد في MassTransit القيمة 3 أم 0. إن كانت 3 فالمستهلكون الأربعة لا يعملون أبداً، ومنهم تذكرة «الوصول المتبقي» في IT.
  - `ISeparationStepCompleted` يُنشر ([MassTransitEventPublisher.cs:265-280](../src/Services/HRContracts/HR.Contracts.Infrastructure/Messaging/MassTransitEventPublisher.cs)) بلا مستهلك. و`ContractsSeparationPartyConsumer` ميت لأن HR.Contracts ليس طرفاً ([SagaSupportConsumers.cs:122-163](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs)).
- **الأثر:** تأمين صحي يُدفع لمن غادر، وإجازات وسلف معلقة تُعتمد بعد الفصل، وفشل صامت في HR.Core، ومستهلكون قد لا يعملون إطلاقاً.
- **الإصلاح المقترح:** إضافة HR.Health طرفاً إلزامياً، وطرف للعقود يغلق الطلبات المعلقة. ضبط `SchemaVersion` صراحة في المنتج ورفع `supportedMax`، مع اختبار يرسل الكائن كما ترسله الـsaga. HR.Core يعيد `Faulted` عند رفض الانتقال. حذف الأحداث والمستهلكين الميتين أو توصيلهم.
- **الخطة:** [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md) (طرف HR.Health وإصدار الحدث) + [PLAN-63](acceptance-plans/PLAN-63-sep-saga-compensation-core-effects.md) (بقية البنود)
- **التحقق المطلوب:** موجب: بعد الفصل بطاقة التأمين ملغاة، والطلبات المعلقة مغلقة بسبب، والمستهلكون الأربعة يعالجون الحدث المنشور من الـsaga الحقيقية. سالب: موظف `Retired` مسبقاً ← تدخل لا تحذير.

#### G-SEP-14 — إعادة التعيين بعد الفصل متعذرة كما وُثّقت
- **الدليل:**
  - الحالات `Terminated/Retired/Resigned/Deceased` لا تنتقل إلا إلى `Anonymized` ([EmploymentStatusRules.cs:42-45](../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs)).
  - ربط موظف قائم عند التعيين لا يغيّر حالته ([RecruitmentHireClosureService.cs:224-229](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs)). و`PendingActivation` للسجل الجديد فقط (`:68`).
  - طرف الدليل في saga التعيين يشترط حساباً نشطاً ([Auth PartyAckConsumers.cs:42-51](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs))، والفصل عطّله، فتتعثر saga التعيين.
  - الخصومات القديمة تُطبَّق من جديد (G-SEP-08).
  - [FUN_002_EXCEPTION_PATHS.md:12](FUN_002_EXCEPTION_PATHS.md) وU7 يدّعيان أن إعادة التعيين تعمل.
- **الأثر:** لا يمكن إعادة تعيين موظف سابق دون تلاعب يدوي بقاعدة البيانات. وإن حدث، يُسترد منه دين سُوّي سابقاً.
- **الإصلاح المقترح:** انتقال «إعادة تعيين» صريح بحلقة توظيف جديدة، وإعادة تفعيل الحساب، وإغلاق خصومات الحلقة السابقة عند التسوية، وأهلية حسب D-SEP-7.
- **الخطة:** [PLAN-63](acceptance-plans/PLAN-63-sep-saga-compensation-core-effects.md)
- **التحقق المطلوب:** موجب: موظف مستقيل يُعاد تعيينه ← حالة نشطة، وحساب مفعّل، ولا خصومات قديمة في أول تشغيل. سالب: مصروف تأديبياً ضمن مدة المنع ← رفض.

### 4.3 قدرات ناقصة (P2)

#### G-SEP-15 — قنوات البدء ومهل الإشعار والإحالة على التقاعد
- **الدليل:**
  - ESS بلا استقالة: إجراءات `EssPortalController` هي الإجازة والسلفة والبطاقة فقط ([EssPortalController.cs:32-175](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs)). و`IResignationSubmitted` بلا منتج.
  - مهلة الإشعار `NoticeDays` تُحمَّل ولا تُستخدم ([ServiceRegimeSeparationRules.cs:15, 33](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeSeparationRules.cs)). وتاريخ النفاذ الافتراضي هو اليوم ([SeparationRecordViewModels.cs:22](../src/Services/HRContracts/HR.Contracts.Web/ViewModels/SeparationRecordViewModels.cs)).
  - لا مهمة لسن التقاعد: `normalRetirementAge: 60` موجود في [insurance-jurisdiction-map.json:71](syr/insurance-jurisdiction-map.json) فقط. والمهمة الخلفية الوحيدة في HR.Contracts خاصة بالتعيين ([Program.cs:57](../src/Services/HRContracts/HR.Contracts.Web/Program.cs)).
  - لا نوع «وفاة» ولا صرف للورثة، ولا «نقل إلى جهة أخرى».
  - `PensionHandoff` (GOSI/GOIP) يُحمَّل ولا يُستخدم ([ServiceRegimeSeparationRules.cs:50](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeSeparationRules.cs)). لا إحالة معاش.
- **الأثر:** الاستقالة ورقية، والموظف يبقى على الراتب بعد سن الإحالة ما لم ينتبه أحد، ولا ملف معاش يُرسل.
- **الإصلاح المقترح:** استقالة من ESS مع سحب ضمن مهلة، ومهلة إشعار لكل نظام ونوع، ومهمة يومية تفتح طلب إحالة قبل 90 يوماً من السن (D-SEP-8)، ونوعا وفاة ونقل خارجي، وحزمة إحالة معاش.
- **التحقق المطلوب:** موظف يبلغ 60 بعد 90 يوماً ← طلب إحالة `Draft` تلقائياً. استقالة بمهلة أقصر من النظام ← رفض أو استثناء معتمد.

#### G-SEP-16 — وثائق إنهاء الخدمة
- **الدليل:** لا شهادة خدمة ولا براءة ذمة ولا قرار PDF في أي خدمة (بحث: صفر). `QuestPDF` غير مستخدم في HR.Contracts. `Documents` نص حر ([SeparationRecord.cs:33](../src/Services/HRContracts/HR.Contracts.Domain/Entities/SeparationRecord.cs)).
- **الأثر:** الموظف لا يحصل على وثيقة رسمية لمدة خدمته ولا على إثبات براءة ذمته. وهما مطلوبان للتعيين في جهة أخرى وللمعاش.
- **الإصلاح المقترح:** قوالب QuestPDF عربية بترقيم رسمي وتوقيع: قرار إنهاء الخدمة، وبراءة الذمة (بعد إغلاق بنود الإخلاء)، وشهادة الخدمة (المدد والدرجات من HR.Core).
- **التحقق المطلوب:** بعد الاكتمال تُولَّد الوثائق الثلاث برقم وتاريخ. براءة الذمة ترفض ما دام بند مفتوحاً.

#### G-SEP-17 — ما بعد الفصل: ESS والتقارير وDSAR والاحتفاظ
- **الدليل:**
  - لا وصول للموظف المنتهية خدمته إلى قسائمه أو تسويته أو وثائقه، ولا عرض للتسوية في ESS.
  - التقارير عدّاد واحد في اللوحة ([DashboardController.cs:20](../src/Services/HRContracts/HR.Contracts.Web/Controllers/DashboardController.cs)) بلا تصدير. والتحليلات تسجل `OrgUnitId = null` ([Dev.Analytics SeparationCompletedConsumer.cs:52](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Consumers/SeparationCompletedConsumer.cs))، فلا دوران وظيفي حسب الوحدة.
  - مصادر DSAR هي HR.Core وPayroll وFileStorage فقط ([HttpDsarSubjectSources.cs:158-160](../src/Services/Compliance/Compliance.Infrastructure/Privacy/HttpDsarSubjectSources.cs)). لا عقود ولا سجلات فصل ولا IDCard ولا Admin.Logistics ولا Auth.Directory.
  - عنصر الأرشيف يُنشأ باحتفاظ افتراضي 10 سنوات ([ArchiveReferenceFactory.cs:30](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ArchiveReferenceFactory.cs)، [SeparationCompletedConsumer.cs:29](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/SeparationCompletedConsumer.cs)). بينما ملف العامل 75 سنة من تاريخ الفصل ([records-retention-matrix.json:14-21](syr/records-retention-matrix.json)). والتكرار يُمنع بالعنوان (`:23`)، فالفصل الثاني لموظف أُعيد تعيينه بالنوع نفسه لا يُؤرشف.
  - لا علم «أهلية إعادة التعيين». و`OrgUnit.HeadEmployeeId` ([OrgUnit.cs:27](../src/Services/HRCore/HR.Core.Domain/Entities/OrgUnit.cs)) والتفويضات تبقى للمفصول.
- **الأثر:** خدمات ما بعد الفصل يدوية. وطلب DSAR ناقص قانونياً (القانون 12/2024). وقد يُتلف ملف العامل قبل مدته القانونية بـ 65 سنة.
- **الإصلاح المقترح:** دور ESS محدود للموظف السابق (القسائم والوثائق) بمدة. تقارير الفصل حسب النوع والوحدة والمدة. شرائح DSAR للخدمات الناقصة. احتفاظ من المصفوفة. علم الأهلية. إنهاء التفويضات ورئاسة الوحدة.
- **التحقق المطلوب:** DSAR لموظف مفصول يعيد شريحة العقود والإخلاء. عنصر الأرشيف بـ 75 سنة. تقرير الدوران حسب الوحدة يطابق عدد الفصول.

### 4.4 تحسينات (P3)

#### G-SEP-18 — التوقيت والمهل
- **الدليل:**
  - تاريخ «اليوم» بتوقيت UTC ([SeparationWorkflowRules.cs:25](../src/Services/HRContracts/HR.Contracts.Domain/Services/SeparationWorkflowRules.cs))، ودمشق UTC+3. بين منتصف الليل والثالثة فجراً يُرفض نفاذ صحيح.
  - مهلة واحدة 30 دقيقة لكل الأطراف ([SeparationStateMachine.cs:49, 108-112](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs)) مقابل SLA من 24 إلى 72 ساعة في المصفوفة. و`OwnerRole`/`SlaHours` مخزّنان بلا متابعة ولا إشعار للمالك.
- **الإصلاح المقترح:** تقويم دمشق (SYR-014). مهل لكل طرف من المصفوفة، ومتابعة SLA مع إشعار المالك والتصعيد.

### 4.5 فجوة التحقق والتوثيق

#### G-SEP-19 — ادعاءات «Done» و«ReadyForSign-off» بلا دليل قبول (P1)
- **الدليل:**
  - [FUN_002_VERIFICATION.md:17](FUN_002_VERIFICATION.md) «FUN-002 = Done». و[FUN_002_UAT_PACK.md:13](FUN_002_UAT_PACK.md) «ReadyForSign-off». وDOM-001 «Done» ([01-P0-RELEASE-BLOCKERS.md:138](completion-backlog/01-P0-RELEASE-BLOCKERS.md)). وINT-003 وFUN-002 «Done» ([02-P1-PLATFORM-INTEGRITY.md:161](completion-backlog/02-P1-PLATFORM-INTEGRITY.md)، [04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md:167](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md)).
  - لا سيناريو فصل ضمن AC-01..37، ولا مجلد في `acceptance-evidence/`.
  - E2E الوحيد يتحقق أن صفحة المعالج تُفتح بحالة دون 500 ([HireSeparationJourneyE2ETests.cs:176-199](../src/Tests/HRManagement.E2E/HireSeparationJourneyE2ETests.cs)، `:193`).
  - مسار النجاح في التكامل بطرف وهمي واحد ([Int003ProcessSagaTests.cs:58-60, 223](../src/Tests/HRManagement.IntegrationTests/Int003ProcessSagaTests.cs)). واختبار الاستئناف ينجح دائماً لأن شرطه `Consumed || Published` (`:145-146`).
  - اختبار الإلغاء يفرض حالة مستحيلة (`Fun002SeparationRecordIntegrationTests.cs:98`). واختبار الحجز يحقن بوابة وهمية (`:77`). واختبارات المستهلكين ترسل `SchemaVersion = 1`.
  - اختبارات `Fun002*` الوحدوية تفحص ملف JSON وأسماء ثوابت ([Fun002SeparationJourneyTests.cs:10-82](../src/Tests/HRManagement.UnitTests/Fun002SeparationJourneyTests.cs)).
  - المصفوفة تذكر خطوتين `SeparationDecision` و`ITAccessRevoked` ([FUN_002_JOURNEY_MATRIX.md:7, 13](FUN_002_JOURNEY_MATRIX.md)) لا كيان لهما، ولا تذكر HR.Health.
- **الأثر:** «Done» في أربع بطاقات لا يقابلها فصل واحد مكتمل في أي بيئة. والاختبارات الخضراء لا تكشف G-SEP-01..05.
- **الإصلاح المقترح:** سيناريوهات `AC-SEP-*` بأدلة حية على ملف الإنتاج الشبيه، ورحلة E2E من طلب الفصل حتى إخلاء المنصب، واختبارات تستخدم المستهلكين الحقيقيين والرسالة كما تنشرها الـsaga، وتصحيح الوثائق.
- **الخطة:** [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md)

### 4.6 تقاطعات مع الدراسات الأخرى
- → IAM: لا نطاق وحدة على قائمة الفصل وتفاصيله ([SeparationRecordHandlers.cs:36-53](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs))، ولا على المعالج، ولا على `EndOfService` ([EndOfServiceController.cs:39-65](../src/Services/Payroll/Payroll.Web/Controllers/EndOfServiceController.cs))، ولا على `ProcessInterventions` ([ProcessInterventionsController.cs:21-54](../src/Services/HRContracts/HR.Contracts.Web/Controllers/ProcessInterventionsController.cs)). بينما الإجازات والسلف في الخدمة نفسها لها نطاق.
- → IAM: إجراءات POST بلا سياسة صريحة في `SeparationRecords` (`Create`، `Edit`، `Delete`: [SeparationRecordsController.cs:85-144](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)) وفي كل المعالج. و`ModuleHrContracts` يشمل `hr-management-approver` ([HrManagementAuthorizationExtensions.cs:28-31](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- → IAM: `ProcessInterventionsController` بـ `[ApiController]` وPOST للاستئناف والإلغاء بلا antiforgery، وبسياسة الوحدة فقط ([ProcessInterventionsController.cs:14-17, 56, 91](../src/Services/HRContracts/HR.Contracts.Web/Controllers/ProcessInterventionsController.cs)).
- → WF: تعريف `SEPARATION-WIZARD` يتيم، والاستثناء في الحارس بسبب غير صحيح ([WorkflowDefinitionsHaveHandlersTests.cs:18](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs)). و`SeparationRecord` خارج `Workflow:EnforceStepRoleEntityTypes`.
- → POL: النطاق `Contracts.Separation` بحالة `Static` ([PolicyTemplatesController.cs:228](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)). والقاعدة `R-SEP-START` لا تُقيَّم، فـ HR.Contracts لا يستدعي المحرك.
- → OPS: `service-regimes.json` يُربط بمخرجات HR.Core وحدها ([HR.Core.Infrastructure.csproj:16](../src/Services/HRCore/HR.Core.Infrastructure/HR.Core.Infrastructure.csproj)). والخطر نفسه في قواعد الترفيع التي تقرؤها HR.Movements من الحزمة نفسها.
- → OPS: `hr-contracts-web` في الإنتاج بلا `Integration__HrCoreBaseUrl` ولا نطاق `hr-core.lookup` ([docker-compose.production.yml:279-292](../docker-compose.production.yml))، وكذلك `hr-app-config` في k8s. و`Keycloak:DirectorySync:Enabled` غير مفعّل في الإنتاج. ولا قسم `Sagas:*` في أي إعداد.
- → QA: E2E الفصل يفحص قابلية الوصول فقط. وشرط اختبار الاستئناف ينجح دائماً. والاختبارات ترسل `SchemaVersion` لا يرسله المنتج، وتفرض حالات لا يصل إليها التطبيق (التفاصيل في G-SEP-19).
- → ER (HCM_006): الصرف التأديبي يعطّل ملف الأجر في Payroll دون فصل (G-ER-02)، وإغلاق القضية ينشئ فصلاً قضائياً لـ `Guid.Empty` (G-ER-03). أثرهما على SEP في G-SEP-08 وG-SEP-10.
- → PAY: التشغيل الدوري يستبعد الملفات المعطلة، فلا يتناسب الشهر الأخير ([PayrollRunJobHandlers.cs:90-91](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)). جانب التسليم في G-SEP-08، والمحرك لدراسة PAY.

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-SEP-1 | كتالوج أنواع الإنهاء المسموحة لكل نظام خدمة (`law50`، `law53`، الخاصة): الوفاة، الإحالة بالسن، عدم اللياقة الصحية، الصرف التأديبي، الحكم القضائي، انتهاء العقد، النقل إلى جهة أخرى، ومهلة الإشعار لكل نوع | قائمة موحدة بمرجع `legal_source_id` لكل نوع ومهلة. حتى التصديق: الأنواع السبعة لـ `law50` بحالة `ApprovedInterim`، ومهلة 30 يوماً للاستقالة فقط | SEP-B، SEP-F |
| D-SEP-2 | معادلة مستحقات نهاية الخدمة لمن يُحال إلى المعاش (`law50`) مقابل المتعاقدين | لا «مكافأة» لمن يستحق معاشاً. التسوية = راتب الأيام الأخيرة + بدل الإجازات ضمن السقف − الذمم، مع إحالة المعاش. المكافأة فقط حيث ينص النظام، وتختلف حسب النوع | SEP-E |
| D-SEP-3 | سقف بدل الإجازات المتراكمة، وهل تُحتسب الأرصدة المرحّلة | سقف لكل نظام بمرجع قانوني، والأرصدة المرحّلة ضمنه | SEP-E |
| D-SEP-4 | سلسلة الاعتماد لكل نوع، ومن يصدر القرار | خطوتان: مراجعة HR (`hr-core`) ثم صاحب الصلاحية (`hr-management-approver`)، وخطوة `compliance` للتأديبي والقضائي. المنشئ لا يعتمد | SEP-A |
| D-SEP-5 | هل يكتمل الفصل مع عهدة غير معادة أو ذمة مالية؟ | لا، إلا بقرار شطب أو تحميل بموافقة مزدوجة. والخصم من التسوية فقط بسند قانوني | SEP-C، SEP-E |
| D-SEP-6 | توقيت التنفيذ ونافذة الإلغاء | الاعتماد والإخلاء قبل النفاذ. قطع الوصول وإيقاف الراتب آلياً عند نهاية يوم النفاذ بتوقيت دمشق. الإلغاء مسموح حتى التنفيذ، وبعده بقرار تصحيحي | SEP-A، SEP-D |
| D-SEP-7 | إعادة التعيين: الأهلية حسب نوع الإنهاء، والسجل نفسه أم سجل جديد | السجل الشخصي نفسه مع حلقة توظيف جديدة. منع المصروف تأديبياً مدة يحددها القانون | SEP-D، SEP-G |
| D-SEP-8 | سن الإحالة على التقاعد وتمديد الخدمة | 60 (عادي) و55 (مبكر) كما في `insurance-jurisdiction-map.json` بعد تصديق قانوني، وتنبيه قبل 90 يوماً | SEP-G |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 1 (P0)   SEP-A (فتح المسار + حوكمة القرار)  ║  SEP-B (الربط القانوني + بيئة الإنتاج)
الموجة 2 (P0)   SEP-C (قطع الوصول + إخلاء حقيقي)    ║  SEP-D (saga + إلغاء + تعويض + آثار HR.Core)
الموجة 3 (P1)   SEP-E (التسوية النهائية)            ║  SEP-F (مدخل واحد لإنهاء الخدمة)
الموجة 4 (P2)   SEP-G (قنوات ووثائق وما بعد الفصل)
مستمر           SEP-H (القبول الحي + E2E + تصحيح الوثائق) — يُغلق مع كل خطة
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) | خطة التنفيذ |
|-------|---------|---------|----------|-----------|-------------------|-------------|
| SEP-A | فتح المسار وحوكمة القرار | G-SEP-01، G-SEP-04، G-SEP-11 | P0 | D-SEP-4، → WF | 8–10 | [PLAN-60](acceptance-plans/PLAN-60-sep-unblock-decision-governance.md) (الاعتماد عبر PLAN-46) |
| SEP-B | الربط القانوني وأنواع الإنهاء وبيئة الإنتاج | G-SEP-02، G-SEP-12 | P0 | D-SEP-1، → OPS | 5–7 | [PLAN-61](acceptance-plans/PLAN-61-sep-legal-types-single-entry.md) (الجزء القانوني من G-SEP-02)؛ الشحن والعنوان ← [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md)؛ G-SEP-12 ← [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md) |
| SEP-C | قطع الوصول والإخلاء الحقيقي | G-SEP-03، G-SEP-06، جزء G-SEP-13 | P0/P1 | SEP-A، D-SEP-5 | 8–10 | [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md) (+ G-IDA-01 التعطيل والتزويد، وجزء G-IAM-11) |
| SEP-D | الـsaga والإلغاء والتعويض وآثار HR.Core | G-SEP-05، G-SEP-07، بقية G-SEP-13، G-SEP-14 | P0/P1 | SEP-A، D-SEP-6، D-SEP-7 | 8–10 | [PLAN-63](acceptance-plans/PLAN-63-sep-saga-compensation-core-effects.md) |
| SEP-E | التسوية النهائية والتسليم للرواتب | G-SEP-08، G-SEP-09 | P1 | SEP-D، D-SEP-2، D-SEP-3 | 8–10 | [PLAN-64](acceptance-plans/PLAN-64-sep-final-settlement.md) (+ حساب نهاية الخدمة من G-PAY-14) |
| SEP-F | مدخل واحد لإنهاء الخدمة | G-SEP-10 | P1 | SEP-A، SEP-B، ER-B | 5–7 | مدموجة في [PLAN-61](acceptance-plans/PLAN-61-sep-legal-types-single-entry.md) (مع احتواء G-ER-02 وG-ER-03) |
| SEP-G | قنوات البدء والوثائق وما بعد الفصل | G-SEP-15، G-SEP-16، G-SEP-17، G-SEP-18 | P2/P3 | SEP-A..E، D-SEP-8 | 8–10 | الموجة 2 |
| SEP-H | التحقق والتوثيق | G-SEP-19 | P1 ← مستمر | كل الخطط | 5–6 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) (الإغلاق المشترك لتحقق الموجة 1)، وكل خطة تحمل قبولها الحي |
| **المجموع** | | | | | **≈ 55–70** | |

**ملاحظة الربط بخطط التنفيذ (2026-10-30):** خطط الموجة 1 المرقّمة هي PLAN-60..64 لهذه الدراسة، وتعتمد على PLAN-37 (الشحن والعنوان) وPLAN-42 (الحجز القانوني) وPLAN-46 (الاعتماد العابر للخدمات). دُمجت SEP-F في PLAN-61. أما SEP-G فتبقى برمزها في «الموجة 2». والتقديرات في الخطط المرقّمة: PLAN-60 (9–10)، PLAN-61 (7–8)، PLAN-62 (9–10)، PLAN-63 (9–10)، PLAN-64 (9–10).

**SEP-A — فتح المسار وحوكمة القرار (8–10 أيام).**
- واجهة إكمال بنود القائمة والاستثناء والإلغاء، وعرض القائمة في التفاصيل.
- آلة حالات للسجل، وربطه بمسار `SEPARATION-WIZARD` بفحص دور الخطوة في الخادم، وفصل المهام.
- قرار إداري إلزامي قبل التنفيذ، ومعاملة مربوطة بـ `SeparationRecordId`.
- سياسات صريحة على كل POST في المعالج والسجلات.
- تحقق من الموظف والعقد، وفهرس فريد لسجل مفتوح، وقفل التعديل بعد أول بند، ورمز تزامن.
- القبول: AC-SEP-Aa (رحلة موجبة بإعدادات الإنتاج حتى النشر)، AC-SEP-Ab (المنشئ لا يعتمد، والدور الخاطئ مرفوض في الخادم)، AC-SEP-Ac (`hr-management-approver` لا ينفّذ)، AC-SEP-Ad (سجل مكرر أو عقد لموظف آخر مرفوض)، AC-SEP-Ae (استثناء بمعتمد ثانٍ حقيقي).

**SEP-B — الربط القانوني وأنواع الإنهاء وبيئة الإنتاج (5–7 أيام).**
- حزمة أنظمة الخدمة كمورد مضمَّن بنسخة وhash، وفشل صريح عند الإقلاع.
- فحص النوع والنظام ومهلة الإشعار عند الإنشاء.
- كتالوج الأنواع حسب D-SEP-1، مع «وفاة» و«نقل خارجي».
- بوابة الحجز بهوية خدمة ومفتاح موحد ورفض عند الفشل.
- إعداد S2S لـ HR.Contracts في compose وk8s (بالتنسيق مع OPS).
- القبول: AC-SEP-Ba (نشر في حاوية مبنية بإعدادات الإنتاج)، AC-SEP-Bb (نوع غير مسموح يُرفض عند الإنشاء والعقد سليم)، AC-SEP-Bc (حجز قانوني يرفض، وHR.Core غير متاح يرفض)، AC-SEP-Bd (مهلة إشعار ناقصة ترفض).

**SEP-C — قطع الوصول والإخلاء الحقيقي (8–10 أيام).**
- طرف الدليل يعطّل Keycloak ويلغي الجلسات، و`Faulted` عند الفشل. تفعيل المزامنة في الإنتاج.
- أطراف العهدة وIT تُبلغ عن البنود المفتوحة وتنتظر الإرجاع بالدليل، وبلاغ فقد عند التعذر.
- HR.Health طرف إلزامي، وتصحيح `SchemaVersion`، وبنود الأطراف تُكمَل بإقرار الطرف.
- القبول: AC-SEP-Ca (الدخول مرفوض بعد الفصل والجلسة ملغاة)، AC-SEP-Cb (Keycloak غير متاح يقود إلى تدخل لا نجاح)، AC-SEP-Cc (أصل مميز غير معاد يوقف الاكتمال حتى الإرجاع بدليل)، AC-SEP-Cd (التأمين ملغى، والمستهلكون الأربعة يعالجون الحدث الحقيقي).

**SEP-D — الـsaga والإلغاء والتعويض وآثار HR.Core (8–10 أيام).**
- تعويض محصور بعلامة في كل طرف، والرواتب تعيد ملف الأجر.
- علم التعويض يحكم `Resume`، والإلغاء أمر على السجل يحدّث كل الأطراف، وإعادة العقد.
- إخلاء المنصب بنسخة `Vacated`، وتنبيه رئاسة الوحدة والتفويضات، و`Faulted` عند رفض انتقال الحالة.
- انتقال إعادة التعيين.
- القبول: AC-SEP-Da (تنفيذ ثم إلغاء يعيد كل طرف حرفياً دون مس السجلات التاريخية)، AC-SEP-Db (فشل أثناء التعويض ثم استئناف بلا `ISeparationCompleted`)، AC-SEP-Dc (المنصب شاغر وطلب الشاغر البديل يُنشأ)، AC-SEP-Dd (إعادة تعيين بحالة نشطة وحساب مفعّل وبلا خصومات قديمة).

**SEP-E — التسوية النهائية والتسليم للرواتب (8–10 أيام).**
- سجل الخدمة من HR.Core، وقواعد حسب النظام والنوع، ومكوّن الراتب النهائي المتناسب، واسترداد الزيادة.
- دفتر سلف واحد، وذمة عند الصافي السالب، وسقف الإجازات، ورفض بلا إعداد مصدَّق أو ملف أجر.
- إنشاء بأمر من الفصل، و`CreatedBy` وفصل المهام، وأمر دفع.
- القبول: AC-SEP-Ea..Ed (حالات ذهبية معتمدة: متعاقد مجدَّد، ومصروف تأديبياً، وسلفة ثم إعادة تعيين، ونفاذ منتصف الشهر قبل التشغيل وبعده)، AC-SEP-Ee (POST يدوي اعتباطي مرفوض، والحاسب لا يعتمد، ولا صرف بلا أمر دفع).

**SEP-F — مدخل واحد لإنهاء الخدمة (5–7 أيام).**
- منع الحالات النهائية في تعديل الموظف.
- القرار الطبي والتأديبي والقضائي ينشئ طلب فصل `Draft` (مع ER-B في دراسة ER).
- `LimitedDuties` ليس فصلاً، وإصلاح السجلات الزائفة القائمة بسكربت.
- القبول: AC-SEP-Fa (تعديل الحالة النهائية من شاشة الموظف مرفوض)، AC-SEP-Fb (تقاعد طبي ينشئ طلباً يمر بالاعتماد)، AC-SEP-Fc (`LimitedDuties` لا ينشئ سجلاً)، AC-SEP-Fd (السكربت يلغي سجلات `Guid.Empty` إلغاءً ناعماً).

**SEP-G — قنوات البدء والوثائق وما بعد الفصل (8–10 أيام).**
- استقالة من ESS مع سحب ضمن المهلة، ومهمة سن التقاعد، وإحالة المعاش.
- قرار الإنهاء وبراءة الذمة وشهادة الخدمة بـ PDF.
- ESS محدودة للموظف السابق، وتقارير الفصل حسب الوحدة، وشرائح DSAR الناقصة، واحتفاظ 75 سنة، وعلم الأهلية.
- مهل الأطراف من المصفوفة مع متابعة SLA، وتوقيت دمشق.
- القبول: AC-SEP-Ga (استقالة ذاتية حتى الاعتماد)، AC-SEP-Gb (طلب إحالة آلي قبل 90 يوماً)، AC-SEP-Gc (الوثائق الثلاث، وبراءة الذمة ترفض ببند مفتوح)، AC-SEP-Gd (DSAR يعيد شريحة العقود)، AC-SEP-Ge (الأرشيف 75 سنة)، AC-SEP-Gf (تجاوز SLA ينبّه المالك).

**SEP-H — التحقق والتوثيق (5–6 أيام).**
- مجلدات `acceptance-evidence/AC-SEP-*` بالعناصر الثمانية، تحت `ACCEPTANCE_REQUIRE_ALL=true`.
- رحلة E2E شبيهة بالإنتاج من طلب الفصل حتى إخلاء المنصب ورفض الدخول.
- اختبارات تكامل بالمستهلكين الحقيقيين وبالرسالة كما تنشرها الـsaga، وحذف الاختبارات التي تفرض حالات مستحيلة.
- تصحيح `FUN_002_*` وADR-FUN-002 و`PROCESS_SAGAS.md` والمصفوفة، وحالات DOM-001 وINT-003 وFUN-002 في `completion-backlog`.
- القبول: AC-SEP-Ha (الرحلة الشبيهة بالإنتاج خضراء)، AC-SEP-Hb (صفر نجاح دون فحص)، AC-SEP-Hc (كل ادعاء «Done» يشير إلى دليل).

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين: قرارا D-SEP-1 وD-SEP-2 (قانونيان)، ونتيجة «يحتاج تحقق» في G-SEP-02 وG-SEP-13 عند أول تشغيل في الحاويات.

---

## 7. معيار «اكتمال رحلة إنهاء الخدمة» المقترح

يُعدّ المسار مكتملاً لإصدار v1 عندما تتحقق البنود الآتية:

- [ ] **1. تُغلق كل فجوات P0 وP1** (G-SEP-01…G-SEP-14 وG-SEP-19)، عدا ما يؤجَّل بقرار موثّق.
- [ ] **2. تنجح رحلة E2E واحدة على الأقل بإعدادات الإنتاج** (`IsLabScale=false`، حاويات مبنية، إعداد compose الإنتاجي لـ HR.Contracts وAuth.Directory): طلب استقالة ← اعتماد خطوتين بمستخدمَين مختلفين ← قرار إداري PDF ← إخلاء بأدلة من المالكين (إرجاع أصل بدليل) ← تاريخ النفاذ ← saga بكل الأطراف ومنها HR.Health ← دخول الموظف مرفوض في Keycloak ← تسوية معتمدة بأمر دفع ← حالة `Resigned` والمنصب `Vacant` ← طلب شاغر بديل يُنشأ.
- [ ] **3. تنجح السيناريوهات السالبة على الخادم:** المنشئ لا يعتمد، والمعتمد المزوَّر مرفوض، والسجل المكرر مرفوض، والحجز القانوني يرفض، والإلغاء بعد التنفيذ يعيد كل طرف حرفياً (ومنه ملف الأجر والعقد) دون مس السجلات التاريخية.
- [ ] **4. حالات التسوية الذهبية** لكل نظام خدمة ونوع إنهاء معتمدة من القانوني والرواتب، ومطابقة للحساب الآلي حتى الليرة.
- [ ] **5. توجد أدلة `acceptance-evidence/AC-SEP-*`** لكل سيناريو موجب وسالب، ولا اختبار ينجح دون فحص أو بحالة لا يصل إليها التطبيق. وتُصحَّح `FUN_002_*` وبطاقات DOM-001 وINT-003 وFUN-002 لتعكس الواقع.
