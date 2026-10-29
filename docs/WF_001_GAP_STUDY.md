# دراسة محرك سير العمل والاعتمادات عبر النطاقات (WF-001) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** محرك سير العمل في HR.Core: `WorkflowEngine` و`WorkflowStarter` و`WorkflowInstanceActionService` و`ApprovalStartCoordinator` و`WorkflowApprovalEvaluator` وبوابات `IWorkflowApprovalGate` و`WorkflowStageGuard` و`WorkflowPanelBuilder`. ويشمل التعريفات وإصداراتها ونشرها وبذورها، والمهل والتصعيد، والتفويض، وصندوق العمل، وواجهة HTTP التي تستدعيها الخدمات الأخرى (`WorkflowInstancesController` و`WorkflowTriggerService`). ويشمل أيضاً **مسار الاعتماد في كل نطاق في كل خدمة**: 23 نطاقاً في الجدول §2-ب. العمق الموضوعي لبعض النطاقات تملكه دراساتها، كالتأديب في [HCM_006_EMPLOYEE_RELATIONS_GAP_STUDY.md](HCM_006_EMPLOYEE_RELATIONS_GAP_STUDY.md). هنا ندرس آلية الاعتماد وحدها.  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والوثائق والأدلة في `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق. تتبّعنا كل اعتماد من الإنشاء إلى الأثر في الخدمة المالكة، وكل حدث نتيجة من المنتج إلى المستهلك.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات، لعدم وجود .NET SDK على الجهاز. كل فجوة مدعومة بمرجع ملف/سطر تحققنا منه بالعرض. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [WF_001_WORKFLOW_ENGINE.md](WF_001_WORKFLOW_ENGINE.md) · [WF_002_WORK_INBOX.md](WF_002_WORK_INBOX.md) · [ADR-WF-001](ADR-WF-001-workflow-engine.md) · [PLAN-03 (AC-11/12)](acceptance-plans/PLAN-03-workflow-sod.md) · [PLAN-12 (AC-33)](acceptance-plans/PLAN-12-logistics-custody.md) · [PLAN-23](acceptance-plans/PLAN-23-wf-work-inbox-ui.md) · [PLAN-24](acceptance-plans/PLAN-24-establishment-workflow-ui.md) · [PLAN-25](acceptance-plans/PLAN-25-hcm-production-workflow-binding.md) و[تقريره النهائي](../reports/plan25-final-report.md) (§6 و§11) · [SOD_MATRIX.md](SOD_MATRIX.md) · بطاقة IAM-003 في [completion-backlog/02](completion-backlog/02-P1-PLATFORM-INTEGRITY.md) · بطاقتا WF-001/WF-002 في [completion-backlog/09](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md) · PROD-005 في [completion-backlog/07](completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md) · [ADR-FUN-004](ADR-FUN-004-health-training-compliance-closure.md) · [acceptance-evidence/summary.md](../acceptance-evidence/summary.md)

---

## 1. الخلاصة

1. **المحرك سليم ومحكوم في 7 أنواع فقط.** أنواع التوظيف الستة وتغيير الملاك تمر كلها بما بناه PLAN-25: منسّق البدء، ودور الخطوة على الخادم، وبوابات الكيان، وتطابق اللوحة مع الخادم. خارج هذه الأنواع السبعة، النطاقان الوحيدان اللذان يستخدمان المحرك هما العهدة وفقدان الأصل، وكلاهما معطّل. أما بقية النطاقات فتعتمد بأعلام حالة محلية في خدماتها، وأربعة منها لا اعتماد فيها أصلاً. وعبارات «Done» في بطاقات WF-001 وWF-002 وIAM-003 وPROD-005 لا تطابق الشيفرة.
2. **4 عوائق P0:**
   - **(أ)** قرارات تنهي الخدمة أو تمنح صلاحية تُنفَّذ بمستخدم واحد دون أي اعتماد: الفصل (يوقف الراتب)، وإصدار بطاقة الهوية، والإجراء التأديبي (G-ER-01). والسبب الجذري: لا آلية عامة لربط اعتماد خدمة أخرى بالمحرك وإعادة نتيجته إليها.
   - **(ب)** اعتماد العهدة والفقدان طريق مسدود. فشل البدء يترك `PendingApproval` بلا نسخة ويجمّد الأصل. ولا شاشة اعتماد، والرابط إليها معطوب. والدور المكلّف بخطوة الفقدان (`admin-logistics`) لا يصل إلى واجهة الاعتماد.
   - **(ج)** أحداث تُنشر بعد آخر `SaveChanges` تحت Bus Outbox، فلا تغادر الخدمة أبداً: نتائج اعتماد العهدة والفقدان، وتنفيذ الترفيع والندب («يحتاج تحقق» حيّ).
   - **(د)** تعريفات المسارات يكتبها وينشرها أي مستخدم `hr-core` أو حساب خدمة، بلا مراجعة ولا تحقق. طلب POST واحد يلغي ما بناه PLAN-25 لكل الجهات.
3. **ثغرات سلامة القرار والمال (P1):**
   - واجهة المحرك العامة تقبل بدءاً مزوّراً، وتقبل الاعتماد من حساب خدمة.
   - في أنواع المحرك غير المفروضة، شخص واحد يعتمد كل الخطوات.
   - «العين الثانية» في حجوزات الصرف وتنبيهات الاحتيال والمتأخرات واستثناء قائمة الفصل يكتبها المنفّذ نفسه في النموذج.
   - الاعتماد المزدوج فوق العتبات المالية (رواتب > 50 مليون، سلفة > 5 ملايين ل.س) غير منفّذ.
   - قرارات تُعتمد بشخص واحد: الترشيح، والإيفاد مع بدله، وحذف بيانات DSAR، وتسوية نهاية الخدمة.
   - تعديل المحتوى ومبالغ الراتب بعد الاعتماد.
   - التفويض لا أثر له.
   - لا قفل تزامن على نسخة المسار، ولا فهرس فريد، وتحديث النسخة والأثر غير ذريين.
   - ادعاءات «Done» بلا أدلة.
4. **فجوات قدرات (P2):** لا مهل ولا تصعيد ولا إشعار للمعتمد. صندوق العمل «الموحد» يغطي 5 أنواع في HR.Core فقط. إدارة التعريفات بلا واجهة ولا إصدارات للجهات ولا خطوات متوازية ولا إعادة للتصحيح. ستة تعريفات وقواعد سياسة يتيمة. والقرار الإداري سجلّ حر قابل للحذف.
5. **فجوة تحقق:** لا سيناريو قبول لأي اعتماد خارج التوظيف والملاك. AC-11 يعتمد على معرّف كيان عشوائي. AC-33 «Pass» يقبل الاستجابة 403. AC-12 محجوب. ولا اختبار واحد يمر من الاعتماد إلى المستهلك عبر الـ Outbox.

**التقدير الإجمالي:** نحو 59–76 يوم عمل لمطوّر واحد، منها 23–28 يوماً لمرحلة P0 (WF-A وWF-B وWF-C). ويتوقف جزء من P0 على القرارين D-WF-1 وD-WF-2.

---

## 2. خريطة المسار كما هو في الشيفرة

### 2-أ. مراحل المحرك

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | التعريف والإصدار والنشر | `WorkflowDefinitionsController` (API فقط)، إصدار تلقائي، النشر يشترط خطوة واحدة | ❌ مفتوح لـ `hr-core` وحسابات الخدمة بلا مراجعة (G-WF-04)، ولا إصدارات للجهات (G-WF-16) |
| 2 | البذور | `WorkflowPackSeeder` (17 تعريفاً) + `WorkflowPlan03Seeder`، والبذرتان القديمتان بلا مستدعٍ | ⚠️ لا ترقية للتعريفات القائمة، و6 تعريفات يتيمة (G-WF-16، G-WF-17) |
| 3 | البدء | المنسّق للتوظيف والملاك؛ `StartByWorkflowCodeAsync` للاستئناف والمسابقات؛ `POST api/workflow-instances` للعهدة | ✅ للأنواع السبعة؛ ❌ العهدة (G-WF-02، G-WF-05)؛ ❌ لا آلية عابرة للخدمات (G-WF-01) |
| 4 | التقييم | `WorkflowApprovalEvaluator`: البنية ← دور الخطوة ← بوابة الكيان ← المنشئ | ✅ للأنواع السبعة؛ ❌ لغيرها (G-WF-06) |
| 5 | الاعتماد والرفض | `WorkflowInstanceActionService` + تواقيع الخطوات | ⚠️ لا قفل تزامن، والأثر غير ذري (G-WF-12) |
| 6 | تسليم النتيجة | معالجات داخل HR.Core؛ أحداث للعهدة والفقدان فقط | ❌ الأحداث لا تغادر الـ Outbox (G-WF-03) ولا حدث نتيجة عام (G-WF-01) |
| 7 | صندوق العمل | `WorkflowInboxQueryService`: 5 أنواع + `ApprovalRequest` القديم | ⚠️ لا عهدة ولا مسابقات ولا خدمات أخرى (G-WF-15) |
| 8 | التفويض | `DelegationsController` ينشئ `ApprovalDelegation` | ❌ لا يقرؤه أحد (G-WF-11) |
| 9 | المهل والتصعيد والإشعار | `TimeoutHours`/`EscalationRole` في الخطوة | ❌ لا مهمة ولا إشعار (G-WF-14) |
| 10 | الإلغاء والإصلاح | إلغاء خاص بكل خدمة في HR.Core؛ `ApprovalRepairService` لحالات «بلا نسخة» في التوظيف والملاك | ⚠️ لا إلغاء عام، ولا إصلاح للعهدة ولا لـ «نسخة مكتملة وكيان معلّق» (G-WF-12) |

### 2-ب. مصفوفة النطاق × الضوابط

الرموز: ✅ مطبَّق على الخادم · ⚠️ جزئي · ❌ غائب · — لا ينطبق. «بإعدادات Production» = `IsLabScale=false` ووضع المحرك `Off` لكل المجالات عدا `Core.Recruitment=Shadow`.

| # | النطاق (الخدمة) | آلية البدء | بإعدادات Production | SoD على الخادم | دور الخطوة على الخادم | الرفض/الإلغاء محروس | SLA/تصعيد | في صندوق العمل |
|---|-----------------|------------|---------------------|----------------|------------------------|----------------------|-----------|----------------|
| 1 | طلب الشاغر، العرض، اللجنة، الاستئناف (HR.Core) | المحرك عبر `ApprovalStartCoordinator`، والاستئناف عبر `WorkflowStarter` دائماً | ✅ يبدأ دائماً، والحالة تُحفظ مع النسخة | ✅ بوابات الكيان + قاعدة المنشئ | ✅ مفروض | ✅ الرفض يمر بالدور والبوابة | ❌ مؤشر KPI فقط | ✅ |
| 2 | المسابقة ونتائجها (HR.Core) | المحرك (`StartByWorkflowCodeAsync`) | ✅ | ✅ بوابتان | ✅ | ✅ | ❌ | ❌ لوحة الشاشة فقط |
| 3 | تغيير الملاك (HR.Core) | المحرك عبر المنسّق | ✅ (أُصلح «التطبيق عند التقديم» في PLAN-25) | ✅ مركزي فقط + المنشئ | ✅ | ✅ | ❌ | ✅ |
| 4 | تسليم العهدة (AdminLogistics) | السياسة (shim) ثم `POST api/workflow-instances` عبر HTTP | ⚠️ اعتماد للمميَّز أو للقيمة > 10,000 فقط؛ فشل البدء يترك `PendingApproval` بلا نسخة (G-WF-02) | ❌ المنشئ حساب خدمة، وشخص واحد يعتمد الخطوتين (G-WF-06) | ❌ غير مفروض | ⚠️ أي `HrCoreBranchOperation` | ❌ | ❌ والرابط معطوب |
| 5 | فقدان الأصل (AdminLogistics) | HTTP للمميَّز فقط | ⚠️ كالسابق | ❌ | ❌ الدور `admin-logistics` لا يصل إلى الواجهة | ⚠️ | ❌ | ❌ |
| 6 | أوامر التوريد (AdminLogistics) | حالة محلية | لا أثر للإعداد | ✅ الطالب ≠ المعتمد | ⚠️ دور الوحدة فقط | ⚠️ التعديل بعد الاعتماد مسموح (G-WF-10) | ❌ | ❌ |
| 7 | النقل (HRMovements) | محلية + `HrManagementApprove` | لا أثر | ✅ الطالب من التوكن | ✅ سياسة صريحة | ✅ الرفض بسياسة؛ ⚠️ مبالغ الراتب تُدخل عند التنفيذ (G-WF-10) | ❌ | ❌ |
| 8 | الانتداب، الإعارة، الترفيع، الندب (HRMovements) | محلية + `HrManagementApprove` | لا أثر | ⚠️ `RequestedBy ??=` قابل للتزوير (G-WF-07) | ✅ | ❌ لا رفض ولا إلغاء للترفيع والندب | ❌ | ❌ |
| 9 | خطط التوزيع (HRMovements) | محلية | لا أثر | ❌ | ❌ بلا سياسة | — | ❌ | ❌ |
| 10 | الإجازات (HR.Contracts) | محلية؛ السياسة عند الإنشاء (`Off`) | لا أثر | ⚠️ المنشئ فقط، لا المستفيد | ✅ `HrContractsApprove` | ⚠️ الرفض بلا سياسة | ❌ | ❌ |
| 11 | السلف (HR.Contracts) | محلية | لا أثر | ⚠️ لا اعتماد ثانٍ فوق 5,000,000 (G-WF-08) | ✅ | ⚠️ الرفض بلا سياسة | ❌ | ❌ |
| 12 | معاملات الخدمة والتعيين (HR.Contracts) | `ServiceTransaction.MoveTo` محلياً | لا أثر | ⚠️ `Step3Approve` بدور منفصل، لكن `Advance` بلا فصل واجبات | ⚠️ | ⚠️ | ⚠️ `SlaHours` للعرض فقط | ❌ |
| 13 | الفصل (HR.Contracts) | **لا اعتماد**؛ `ApprovedBy` = المنشئ | ❌ يُنفَّذ بمستخدم واحد (G-WF-01) | ❌ استثناء القائمة بمعتمد يكتبه المنفّذ | ❌ | ⚠️ الإلغاء بسياسة | ⚠️ للعرض فقط | ❌ |
| 14 | القرار الإداري (HR.Contracts) | سجل بلا حالة | — | ❌ | ❌ | ❌ الحذف بلا سياسة (G-WF-18) | ❌ | ❌ |
| 15 | مسيّر الرواتب (Payroll) | محلية (`PayrollRun`) | لا أثر | ✅ منشئ ≠ معتمد ≠ مُرحِّل؛ ❌ لا اعتماد ثانٍ فوق 50,000,000 | ✅ سياسات | ✅ | ❌ | ❌ |
| 16 | حجز الصرف، تنبيه الاحتيال، المتأخرات (Payroll) | محلية | — | ❌ المعتمد الثاني يُكتب في النموذج (G-WF-07) | ✅ `PayrollApprove` | — | ❌ | ❌ |
| 17 | تسوية نهاية الخدمة (Payroll) | محلية | — | ⚠️ لا منشئ مسجَّل؛ المعتمد ≠ الصارف فقط | ✅ | — | ❌ | ❌ |
| 18 | طلب البطاقة (IDCard) | **لا اعتماد**؛ `ProcessRequest` يُصدر البطاقة | ❌ (G-WF-01) | ❌ | ❌ دور الوحدة | ⚠️ التعديل بعد الإصدار مسموح | ⚠️ مهلة الإلغاء فقط | ❌ |
| 19 | الترشيح للتدريب والإيفاد (HRTraining) | محلية | — | ❌ `NominatedBy` من النموذج | ❌ دور الوحدة | ✅ الرفض مقيد بالحالة | ❌ | ❌ |
| 20 | الإجراء التأديبي (Compliance) | **لا اعتماد**؛ الحدث يُنشر عند الإنشاء | ❌ (G-WF-01، G-ER-01) | ❌ | ❌ | ❌ التعديل بعد النشر (G-ER-06) | ❌ | ❌ |
| 21 | DSAR (Compliance) | محلية | — | ❌ المنشئ ينفّذ الحذف (G-WF-09) | ⚠️ `compliance` | ✅ انتقالات محددة | ❌ | ❌ |
| 22 | إتلاف الوثائق (RecordsArchive) | محلية | — | ✅ الطالب ≠ المعتمد | ⚠️ دور الوحدة | ✅ | ❌ | ❌ |
| 23 | الاعتماد القديم `ApprovalRequest` (ApprovedCV) | `ApprovalWorkflowService` | — | ✅ | ❌ `Approve` لا يفحص `RoleRequired` (G-WF-19) | ⚠️ الإلغاء لأي `HrCoreWrite` | ❌ | ✅ |

**القراءة:** الأنواع السبعة في الصفوف 1–3 وحدها مكتملة الضوابط. لا صف خارجها فيه مهلة أو تصعيد أو ظهور في الصندوق. وثلاثة صفوف (13، 18، 20) تطبّق القرار عند التقديم.

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **محرك صغير وقابل للاختبار:** تثبيت الإصدار على النسخة (`DefinitionVersion`)، والتقدم المتسلسل بالشروط (`FindNextApplicableStep`)، ومنع اعتماد الخطوة نفسها مرتين من الشخص نفسه، وتواقيع لكل خطوة (`WorkflowSignatureService`).
- **PLAN-25 متين في نطاقه:** `ApprovalStartCoordinator` لا يغيّر الحالة إلا مع النسخة وداخل معاملة. و`WorkflowStageGuard.ValidateStepRole` يفحص كل أدوار الفاعل في الاعتماد والرفض. وسبع بوابات تنقل قواعد فصل الواجبات والنصاب والمراجع والنطاق إلى الخادم. واللوحات تسأل المقيِّم نفسه (84 حالة تطابق). و`Workflow:AdminBypass=false`. وإصلاح «بلا نسخة» وسحب `RECRUITMENT-HIRE` واختبار `WorkflowDefinitionsHaveHandlersTests`.
- **إعادة الاستخدام الآمنة:** المنسّق لا يتبنى نسخة فيها تاريخ أو خطوة متقدمة ([ApprovalStartCoordinator.cs:172-192](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs)). والمعالجات لا تطبّق إلا النسخة المرتبطة بالكيان ([RecruitmentRequisitionWorkflowHandler.cs:17-19](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionWorkflowHandler.cs)).
- **واجهة البدء العامة تحمي الأنواع المفروضة:** `Start` يرفض بدء أنواع التوظيف والملاك من خارج التقديم ([WorkflowInstancesController.cs:54-55](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)).
- **اعتمادات محلية سليمة:**
  - الرواتب: منشئ ≠ معتمد ≠ مُرحِّل، وصارف الدفعة ≠ المعتمد ([PayrollSodGuard.cs:7-69](../src/Services/Payroll/Payroll.Application/Services/PayrollSodGuard.cs)).
  - النقل: الطالب من التوكن، والتعديل مقيّد بالحالة، والتنفيذ ذري بشرط الحالة ([TransferRequestsController.cs:87-91، 149-158، 350-354](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)).
  - الإجازة: الاعتماد والإلغاء ذريان بـ `ExecuteUpdate` بشرط الحالة.
  - إتلاف الوثائق: الطالب ≠ المعتمد ([ArchiveRetentionRules.cs:78-84](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs)).
  - أمر التوريد: الطالب ≠ المعتمد.
  - التعيين: `Step3Approve` بدور `hr-management-approver` منفصل عن المنفّذ.
- **صندوق العمل (PLAN-23/24)** يطبّق نطاق الفرع والدور والمنشئ وقاعدة العرض الاستثنائي في القائمة.
- **سياسات صريحة في HR.Core:** كل POST في HR.Core خارج قائمة مجمّدة من 42 إجراءً يحمل سياسة صريحة (`HrCorePostActionsHaveExplicitPolicyTests`).

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-WF-01 — لا آلية لربط اعتماد خدمة أخرى بالمحرك؛ فقرارات تنهي الخدمة أو تمنح صلاحية تُنفَّذ بلا اعتماد
- **الدليل:**
  - **الفصل:**
    - `SeparationWizardController.Step1` يسجّل المنشئ نفسه معتمداً: `ApprovedBy = ParseUserId()` ([SeparationWizardController.cs:55-87](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs)). ويفعل `SeparationRecordsController.Create` الشيء نفسه عبر `MapCreate(vm, ParseUserId())` ([SeparationRecordsController.cs:91، 209-210](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)).
    - لا حالة «بانتظار الاعتماد». و`Step3Execute` ([SeparationWizardController.cs:153-165](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs)) بلا سياسة صريحة، ويستدعي `PublishSeparationHandler`. هذا المعالج لا يفحص إلا الإلغاء والنشر السابق وتاريخ النفاذ واكتمال القائمة، ثم يبدأ saga الانفصال ([SeparationRecordHandlers.cs:129-183](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)).
    - عند وصول الحدث يعطّل Payroll ملف الأجر ([InboundEventHandlers.cs:195-213](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
    - التعريف `SEPARATION-WIZARD` (خطوة `hr-core` ثم `compliance`) منشور ([WorkflowPackSeeder.cs:32-34](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)) ولا يبدؤه أحد.
  - **بطاقة الهوية:**
    - `ProcessRequest` يُنشئ `IssuedCard` بحالة `Active` مباشرة ([CardRequestsController.cs:123-170](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)). لا يقارن `RequestedBy` بالمعالج، رغم التعليق «four-eyes comparison at approval time» (`:98`).
    - التعريف `ID-CARD-ISSUANCE` (دور `security`) منشور ([WorkflowPackSeeder.cs:35-37](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs))، والقاعدة `R-ID-PROCESS` تطلقه ([PolicyEngineDomainSeeds.cs:15-19](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)). لكن `ProcessRequest` يتجاهل `WorkflowInstanceId` في نتيجة السياسة (`:132-139`).
  - **التأديب:** النمط نفسه، مفصّل في G-ER-01 وG-ER-02. والوثيقة تدّعي «التأديب لا يُصدر خارج workflow» ([ADR-FUN-004:34](ADR-FUN-004-health-training-compliance-closure.md)).
  - **السبب الجذري في المحرك:**
    - الآلية العابرة للخدمات الوحيدة هي `WorkflowTriggerService` الخاصة بالعهدة: POST خام بلا ربط حالة ([WorkflowTriggerService.cs:17-72](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs)).
    - `PublishWorkflowOutcomeAsync` لا يعرف إلا العهدة والفقدان وأنواع HR.Core ([WorkflowInstanceActionService.cs:171-206](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)).
    - لا يوجد حدث نتيجة عام، ولا منسّق بدء يمكن استدعاؤه عبر HTTP.
- **الأثر:**
  - موظف `hr-contracts` واحد ينهي خدمة أي موظف، فيتوقف راتبه.
  - موظف `idcard` واحد يُصدر بطاقة دخول نافذة لأي شخص.
  - موظف `compliance` واحد يوقع عقوبة تُطبَّق في الرواتب فوراً.
  - كل ذلك يحدث بإعدادات الإنتاج، وهو نظير «تطبيق تغيير الملاك عند التقديم» الذي صنّفه PLAN-25 عائقاً.
- **الإصلاح المقترح:**
  - **ربط اعتماد عابر للخدمات في المحرك:**
    - `POST api/workflow-approvals/start` بنطاق S2S مخصص لكل خدمة، يحمل الفاعل الحقيقي. يتبع دلالات المنسّق: نسخة واحدة لكل كيان، ومفتاح idempotency، ورفض عند غياب التعريف.
    - حدث نتيجة عام `IWorkflowOutcomeDecided` (`EntityType`، `EntityId`، `InstanceId`، `Approved`، `DecidedBy`، `Reason`) يُنشر عبر الـ Outbox.
    - مستهلك في الخدمة المالكة يطبّق الأثر بعد التحقق من أن `entity.WorkflowInstanceId == InstanceId`.
  - **ربط المسارات:** الفصل لا يُنفَّذ إلا بعد `Approved`، وإصدار البطاقة لا يتم إلا بعد اعتماد `ID-CARD-ISSUANCE`. وتُتاح الآلية لـ ER-A إن اختار المالك المحرك في D-ER-2.
  - حدود الخطوات وأدوارها بقرار D-WF-1.
- **الخطة:** [PLAN-46](acceptance-plans/PLAN-46-wf-cross-service-approvals.md) — للبطاقة والتأديب وجهة HR.Core للفصل؛ وجهة HR.Contracts للفصل في [PLAN-60](acceptance-plans/PLAN-60-sep-unblock-decision-governance.md)
- **التحقق المطلوب:**
  - موجب (ملف شبيه بالإنتاج): فصل ← بانتظار الاعتماد ← لا حدث ولا تغيير في Payroll ← اعتماد خطوتين ← تنفيذ ← تعطيل ملف الأجر.
  - سالب: `Step3Execute` قبل الاعتماد ← رفض. المنشئ يعتمد ← رفض. معالج البطاقة = طالبها ← رفض. نتيجة بنسخة غير مرتبطة بالكيان ← تجاهُل مُسجَّل.

#### G-WF-02 — اعتماد العهدة والفقدان طريق مسدود في الإنتاج
- **الدليل:**
  - **البدء الفاشل يجمّد الأصل:**
    - البدء يجري عبر HTTP قبل الحفظ. أي فشل (403، مهلة، عنوان غير مضبوط) يعيد `null` ([WorkflowTriggerService.cs:26-31، 56-60، 66-71](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs)).
    - يُحفظ التسليم عندئذ `PendingApproval` مع `WorkflowInstanceId = null` ورسالة «تعذّر بدء سير الموافقة» ([AssetsController.cs:422-464](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs))، ومثله الفقدان (`:634-643`).
    - لا إجراء في AdminLogistics لإعادة البدء أو السحب أو الإلغاء.
    - التسليم المعلّق يُعدّ حائزاً مفتوحاً (`ReturnedAt == null`)، فيمنع أي تسليم جديد للأصل ([AssetAssignmentRules.cs:11-14](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetAssignmentRules.cs)). وطلب الفقدان المعلّق يمنع أي طلب جديد ([AssetLostRules.cs:12-15](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetLostRules.cs)).
  - **لا شاشة اعتماد:**
    - صندوق العمل يقرأ 5 أنواع ليس بينها `AssetAssignment` ولا `AssetLossReport` ([WorkflowInboxQueryService.cs:22-29](../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs)).
    - رابط «عرض الموافقة» يذهب إلى `/ui/hr-core/Approvals/Review?workflowInstanceId=…` ([Details.cshtml:288-291](../src/Services/AdminLogistics/Admin.Logistics.Web/Views/Assets/Details.cshtml)). لكن `Review(Guid id)` يقرأ `ApprovalRequests` القديمة بمعامل `id` ([ApprovalsController.cs:78-84](../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs))، فيعيد 404.
    - الطريق الوحيد هو POST خام إلى `api/workflow-instances/{id}/approve`، كما يفعل AC-11.
  - **الدور المكلّف لا يصل:**
    - خطوة `ASSET-LOSS-APPROVAL` تتطلب `admin-logistics` ([WorkflowPackSeeder.cs:26-28](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)). لكن سياسة الاعتماد `HrCoreBranchOperation` لا تشمل `admin-logistics` ([HrManagementAuthorizationExtensions.cs:91-97](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
    - لأن فرض الدور معطّل لهذا النوع (G-WF-06)، يعتمد الفقدان فعلياً `hr-core` أو مدير فرع بدل مسؤول الأصول.
    - مصفوفة SoD نفسها تجعل المنشئ `admin-logistics` والمدقق `hr-management-approver` ([PROD001ab_ComprehensivePolicyEngine.cs:205](../src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260805120418_PROD001ab_ComprehensivePolicyEngine.cs)).
  - **تقييم متابعة PLAN-25 («دور الخطوة 1 يتوقف على ترتيب البذور»):**
    - `AssetAssignmentWorkflowSeeder` (`admin-logistics`، خطوة واحدة) و`AssetLossWorkflowSeeder` **بلا أي مستدعٍ** في الشيفرة الحالية.
    - قاعدة البيانات الجديدة تحصل دائماً على `hr-core` ثم `hr-management-approver` ([WorkflowPackSeeder.cs:23-25](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs))، و`WorkflowPlan03Seeder` لا يضيف شيئاً.
    - يبقى الاعتماد على الترتيب في قواعد بيانات بُذرت بإصدار قديم فقط. هناك يبقى v1 (`admin-logistics`) منشوراً بجانب v2 ([WorkflowPlan03Seeder.cs:19-28، 47-59](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPlan03Seeder.cs))، والنسخ المثبّتة على v1 تطلب دوراً لا يصل.
    - الملفان يستخدمان المعرّف نفسه `f0050001-…-0001` ([AssetAssignmentWorkflowSeeder.cs:10](../src/Services/HRCore/HR.Core.Infrastructure/Data/AssetAssignmentWorkflowSeeder.cs)). أثر ذلك على قاعدة قديمة «يحتاج تحقق».
    - **الحكم:** المسألة قرار أدوار (D-WF-2)، لا ترتيب بذور.
- **الأثر:**
  - كل تسليم لأصل مميَّز أو تزيد قيمته على 10,000 يعلق، وفقاً لـ G-WF-03 حتى بعد اعتماده.
  - الأصل يتجمد ولا يمكن تسليمه لغيره.
  - لا أحد يرى الطلب إلا عبر استدعاء API.
  - وفقدان الأصول المميزة يعتمده قسم ليس مسؤولاً عنها.
- **الإصلاح المقترح:**
  - بدء العهدة والفقدان بالآلية العابرة في G-WF-01. لا تُحفظ `PendingApproval` إلا مع نسخة، ويُعاد البدء آلياً بـ Outbox/retry.
  - إجراء سحب الطلب وإعادة بدئه في AdminLogistics.
  - إضافة النوعين إلى صندوق العمل وإلى لوحة اعتماد في HR.Core (`WorkflowInstances/Details`)، وتصحيح الرابط.
  - سياسة اعتماد تُدخل `admin-logistics` لخطوته وحدها، وفرض دور الخطوة للنوعين بحسب D-WF-2.
  - حذف البذرتين الميتتين، وسكربت يسحب v1 القديم ويُلغي نسخه المعلّقة بتدقيق.
- **الخطة:** [PLAN-45](acceptance-plans/PLAN-45-wf-asset-custody-approval-loop.md)
- **التحقق المطلوب:**
  - موجب: تسليم أصل مميَّز ← نسخة مرتبطة ← يظهر في صندوق `hr-core` ثم المعتمد ← خطوتان من شخصين ← التسليم `Active` في AdminLogistics والأصل `InUse`.
  - سالب: HR.Core متوقف عند التسليم ← لا `PendingApproval` بلا نسخة (رفض أو بدء مؤجل مرئي). سحب الطلب يحرر الأصل. مستخدم `hr-core` يعتمد خطوة `admin-logistics` ← رفض.

#### G-WF-03 — أحداث تُنشر بعد آخر `SaveChanges` لا تغادر الـ Outbox
- **الدليل:**
  - **آلية الـ Outbox:** Bus Outbox مفعّل لكل خدمة ([MassTransitExtensions.cs:112-121](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)). `IPublishEndpoint` الخاص بالطلب يضيف الرسالة إلى `DbContext`، ولا تُكتب إلا عند `SaveChangesAsync` لاحق. والفريق يعرف ذلك ويطبّقه في إغلاق التعيين: النشر ثم الحفظ ([RecruitmentHireClosureService.cs:97-100، 321-337](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs)).
  - **العهدة والفقدان:** `WorkflowInstanceActionService.ApproveAsync` يحفظ النسخة (`:117`)، ثم ينشر `IAssetAssignmentApproved` أو `IAssetLossApproved` (`:119-120` ← `:216`، `:245`) دون أي حفظ بعدها. والرفض كذلك (`:164-166`) ([WorkflowInstanceActionService.cs](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)).
  - **الترفيع والندب:** `PromotionRequestsController.Execute` يحفظ (`:1032`)، ثم ينشر `IPromotionExecuted` (`:1034`) ولا يحفظ بعده. وكذلك `DelegationRequestsController.Execute` (`:1189` ← `:1191`) ([MovementsExtendedControllers.cs](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)). أما الانتداب فينشر ثم يحفظ (`:434-446`)، وهذا صحيح.
  - **لا اختبار يغطي المسار:** `Ac33AssetLossWorkflowTests` يستدعي المستهلك مباشرة ([Ac33AssetLossWorkflowTests.cs:39-72](../src/Tests/HRManagement.IntegrationTests/Ac33AssetLossWorkflowTests.cs)). وAC-11 يعتمد على معرّف تسليم عشوائي.
  - **«يحتاج تحقق» حيّ:** تأكيد أن صف `OutboxMessage` لا يُكتب في هذه المسارات.
- **الأثر:**
  - يكتمل اعتماد العهدة في HR.Core، لكن AdminLogistics لا تعلم به، فيبقى التسليم `PendingApproval` إلى الأبد.
  - الترفيع المنفّذ لا يصل إلى سجل التوظيف في HR.Core. ولا يوجد في Payroll أي مستهلك لـ `IPromotionExecuted` أصلاً.
  - فشل صامت لا يظهر في أي سجل خطأ.
- **الإصلاح المقترح:**
  - النشر قبل `SaveChangesAsync` الأخير في المعاملة نفسها، في المواقع الأربعة.
  - اختبار معماري يرفض `Publish` بعد آخر `SaveChangesAsync` في الطريقة نفسها.
  - اختبار تكامل على PostgreSQL: اعتماد ← صف Outbox ← تسليم ← مستهلك.
- **الخطة:** [PLAN-40](acceptance-plans/PLAN-40-msg-atomic-event-publishing.md)
- **التحقق المطلوب:**
  - موجب: اعتماد الخطوة الأخيرة ← صف `OutboxMessage` في قاعدة HR.Core ← التسليم `Active` خلال مهلة. تنفيذ ترفيع ← سجل توظيف محدَّث في HR.Core.
  - سالب: إعادة تسليم الحدث نفسه ← لا تغيير ثانٍ.

#### G-WF-04 — تعريفات المسارات مفتوحة للكتابة والنشر بلا مراجعة ولا تحقق
- **الدليل:**
  - **الصلاحية:** `WorkflowDefinitionsController` كله تحت `HrCoreWrite` ([WorkflowDefinitionsController.cs:14](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowDefinitionsController.cs)). تشمل هذه السياسة Admin و`hr-core` وأي حساب خدمة بنطاق `hr-core.workflow` ([HrManagementAuthorizationExtensions.cs:73-77](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). والبوابة تمرر الطلب بالسياسة نفسها ([GatewayPathAuthorizationMiddleware.cs:180-191](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
  - **لا تحقق:**
    - `Create` يقبل أي `Code` و`DomainEntityType` وخطوات، ويرفع الإصدار (`:46-70`).
    - `Publish` لا يتحقق إلا من وجود خطوة واحدة (`:97-114`). لا موافقة ثانية، ولا تحقق من الأدوار، ولا حد أدنى للخطوات.
    - `GuardrailRolesJson` لا يقرؤه أي كود عدا المحوِّل ([WorkflowDefinitionMapper.cs:18](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowDefinitionMapper.cs))، رغم وعد [ADR-WF-001:29-30](ADR-WF-001-workflow-engine.md) و[WF_001_WORKFLOW_ENGINE.md:39](WF_001_WORKFLOW_ENGINE.md).
  - **الإصدار الأعلى يسري فوراً:** كل بدء جديد يأخذ أعلى إصدار منشور ([WorkflowStarter.cs:116-124](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs)). وتعريفات الجهة الافتراضية تخدم كل جهة لا تملك تعريفاً. والخطوة ذات `RoleRequired` فارغ تمر من فحص الدور ([WorkflowStageGuard.cs:119-120](../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs)).
  - **سيناريو:**
    - مستخدم `hr-core` في الجهة الافتراضية ينشر `ESTABLISHMENT-CHANGE-APPROVAL` v2 بخطوة واحدة بلا دور.
    - كل تغيير ملاك بعده يعتمده أي زميل غير منشئ وحده، وتختفي خطوة `hr-management-approver`.
    - ينطبق الشيء نفسه على `RECRUITMENT-SELECTION-COMMITTEE` و`RECRUITMENT-APPEAL-REVIEW` و`RECRUITMENT-COMPETITION-APPROVAL` و`RECRUITMENT-RESULTS-APPROVAL`، لأنها بلا ربط في `PolicyWorkflowBindings`.
    - **استثناء:** طلب الشاغر والعرض والعرض الاستثنائي محميّة من هذا السيناريو. فـ`WorkflowStarter` يقدّم الربط المثبَّت على v1 قبل أعلى إصدار ([WorkflowStarter.cs:93-113](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs))، والبذرة تربط هذه الرموز الثلاثة بمعرّف v1 ([PolicyEngineSeeder.cs:172-185، 187-208](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineSeeder.cs)). ولا واجهة لتعديل الربط ولا لسحب نشر v1. يبقى الخطر هنا في قاعدة بُذر ربطها قبل وجود التعريف (`WorkflowDefinitionId = null`) («يحتاج تحقق»).
  - **المهل والصلاحية:** `WorkflowStarter` يتجاهل `ValidFrom` و`ValidTo`. وحده `Start` العام يفحصهما ([WorkflowInstancesController.cs:62-63](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)).
- **الأثر:** ضمانات PLAN-25 لتغيير الملاك واللجان والاستئناف والمسابقات ونتائجها (G-02 وG-04 في دراسة التوظيف) قابلة للإلغاء بطلب POST واحد من المستخدم الأكثر شيوعاً في HR، ولكل الجهات معاً إن كان الناشر في الجهة الافتراضية. ولا يبقى أثر يميّز تعريفاً «رسمياً» من تعريف معدَّل. *(صُحّح بعد التحقق المستقل: طلب الشاغر والعروض مثبّتة على v1 عبر `PolicyWorkflowBindings` فلا يمسّها إصدار جديد؛ الباقي مؤكد)*
- **الإصلاح المقترح:**
  - سياسة `WorkflowAdmin` مركزية (D-WF-4).
  - نشر بعينين: مسودة ← بانتظار النشر ← منشور، بفاعل مختلف.
  - تحقق إلزامي عند النشر:
    - الأدوار من كتالوج الأدوار.
    - لا خطوة بلا دور للأنواع المفروضة.
    - حد أدنى للخطوات وأدوار إلزامية لكل `DomainEntityType` في الشيفرة (guardrails حقيقية).
  - تدقيق لكل تغيير.
  - `WorkflowStarter` يحترم `ValidFrom` و`ValidTo`.
  - سحب صلاحية الكتابة من حسابات الخدمة.
- **الخطة:** [PLAN-43](acceptance-plans/PLAN-43-wf-definition-governance-engine-api.md)
- **التحقق المطلوب:**
  - سالب: `hr-core` ينشئ إصداراً ← 403. `WorkflowAdmin` ينشر تعريفاً بخطوة بلا دور لنوع مفروض ← رفض. الناشر = المنشئ ← رفض.
  - موجب: تعريف صالح بعينين ← يسري على الطلبات الجديدة فقط، والنسخ الجارية تبقى على إصدارها.

### 4.2 ثغرات سلامة القرار والحوكمة (P1)

#### G-WF-05 — واجهة المحرك العامة تقبل بدءاً مزوّراً واعتماداً من حساب خدمة، والمستهلك لا يتحقق من النسخة
- **الدليل:**
  - **البدء:** `Start` تحت `HrCoreWrite` يقبل أي نوع غير مفروض، بأي `EntityId` وأي `ContextJson`، ويجعل المستدعي `InitiatedBy` ([WorkflowInstancesController.cs:48-101](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)).
  - **الحمولة:** حمولة النتيجة تُقرأ من `ContextJson` ([WorkflowInstanceActionService.cs:215-224](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)).
  - **المستهلك:** يفعّل التسليم بمعرّفه دون مقارنة `WorkflowInstanceId` ([AssetAssignmentWorkflowConsumers.cs:23-42](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/AssetAssignmentWorkflowConsumers.cs)). فتكفي نسخة مزوّرة بمعرّف تسليم معلّق، تُعتمد، لتفعيله.
  - **حسابات الخدمة:**
    - `HrCoreBranchOperation` يقبل أي حساب خدمة بنطاق `hr-core.workflow` ([HrManagementAuthorizationExtensions.cs:91-97](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، فيعتمد ويرفض أي نسخة من نوع غير مفروض.
    - الإنتاج يمنح هذا النطاق لـ `svc-admin-logistics` ([docker-compose.production.yml:439-441](../docker-compose.production.yml))، والبيئة التطويرية لـ `svc-hr-contracts` أيضاً ([sync-keycloak-dev-realm.ps1:521](../scripts/sync-keycloak-dev-realm.ps1)).
  - **المنشئ:** AdminLogistics تستدعي بـ client credentials دون الفاعل الحقيقي (`forwardActingUser = false`) ([ServiceIdentityClient.cs:141-145](../src/Shared/HRManagement.ServiceDefaults/ServiceIdentityClient.cs)، [DependencyInjection.cs:25-27](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/DependencyInjection.cs)). لذلك `InitiatedBy` هو حساب الخدمة، ولا تحمي قاعدة المنشئ أحداً.
- **الأثر:**
  - مستخدم `hr-core` واحد يفعّل تسليم عهدة مميزة دون مدقق.
  - اختراق خدمة اللوجستيات يكفي لاعتماد كل طلباتها.
  - سجل المسار لا يعرف من طلب.
- **الإصلاح المقترح:**
  - `Start` لحسابات الخدمة فقط، بقائمة أنواع لكل حساب، وبالفاعل الحقيقي في ترويسة موقّعة يتحقق منها الخادم (يُسجَّل `RequestedBy`).
  - حدث النتيجة يحمل `InstanceId`، والمستهلك يرفض النسخة غير المرتبطة.
  - إخراج حسابات الخدمة من سياسة الاعتماد والرفض.
  - يُدمج ذلك في آلية G-WF-01.
- **الخطة:** [PLAN-43](acceptance-plans/PLAN-43-wf-definition-governance-engine-api.md)
- **التحقق المطلوب:**
  - سالب: `hr-core` يبدأ نسخة `AssetAssignment` ← 403. حساب خدمة يعتمد ← 403. نسخة غير مرتبطة تكتمل ← التسليم لا يتغير، وتحذير مُسجَّل.
  - موجب: البدء من AdminLogistics يسجّل الطالب الحقيقي `InitiatedBy`.

#### G-WF-06 — لا فصل مهام في أنواع المحرك غير المفروضة (العهدة والفقدان)
- **الدليل:**
  - **النوعان خارج الفرض:** `Workflow:EnforceStepRoleEntityTypes` لا يشملهما ([appsettings.Production.json:260-271](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)، [WorkflowStageGuard.cs:14-34](../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs)). ولا بوابة كيان لهما ([WorkflowApprovalEvaluator.cs:42-65](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/WorkflowApprovalEvaluator.cs)).
  - **المحرك يمنع حالتين فقط:**
    - الفاعل نفسه في **الخطوة نفسها** ([WorkflowEngine.cs:54-60](../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowEngine.cs)).
    - المنشئ في الخطوات **بعد الأولى** ([WorkflowStageGuard.cs:104-107](../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs)).
  - **النتيجة:** مستخدم واحد يحمل `hr-core` يعتمد الخطوتين في `ASSET-ASSIGN-APPROVAL` («موافقة لوجستية» ثم «موافقة إدارية» لدور `hr-management-approver`). وAC-11 نفسه يُظهر المنشئ يعتمد الخطوة 1 ([Ac11PolicyWorkflowTests.cs:78-86](../src/Tests/HRManagement.AcceptanceTests/Ac11PolicyWorkflowTests.cs)).
  - **الرفض:** لأي حامل `HrCoreBranchOperation`.
- **الأثر:** «اعتماد على مستويين» شكلي. أي مستخدم HR مركزي أو مدير فرع يعتمد عهدة مميزة وحده.
- **الإصلاح المقترح:**
  - قلب الإعداد: الفرض افتراضي لكل الأنواع، وقائمة استثناء صريحة فارغة في الإنتاج.
  - قاعدة عامة في المقيِّم تمنع الفاعل نفسه من خطوتين في نسخة واحدة (قابلة للضبط في التعريف).
  - تعريف «المنشئ» ليشمل الطالب الحقيقي (D-WF-8).
- **الخطة:** [PLAN-43](acceptance-plans/PLAN-43-wf-definition-governance-engine-api.md)
- **التحقق المطلوب:**
  - سالب: الفاعل نفسه يعتمد الخطوة 2 بعد الخطوة 1 ← رفض. `hr-core` على خطوة `hr-management-approver` ← رفض.
  - موجب: شخصان بالدورين الصحيحين ← اكتمال.

#### G-WF-07 — «العين الثانية» يكتبها المنفّذ نفسه، والطالب قابل للتزوير
- **الدليل:**
  - **حجز الصرف:** `PaymentHoldsController.Release` يأخذ المعتمد الثاني من النموذج (`releaseApprovedBy`، أو `Request.Form["ReleaseApprovedBy"]`) ([PaymentHoldsController.cs:57-70](../src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsController.cs)). والخدمة لا تفحص إلا أنه يختلف عن المنفّذ ([PaymentHoldService.cs:24-28](../src/Services/Payroll/Payroll.Application/Services/PaymentHoldService.cs)). والواجهة البرمجية مثلها ([PaymentHoldsApiController.cs:25-31](../src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsApiController.cs)).
  - **تنبيه الاحتيال:** `FraudAlertsController.Resolve` بالطريقة نفسها ([FraudAlertsController.cs:84-100](../src/Services/Payroll/Payroll.Web/Controllers/FraudAlertsController.cs)).
  - **المتأخرات:** التعديل اليدوي يأخذ `approverId` من النموذج ([ArrearsController.cs:93-103](../src/Services/Payroll/Payroll.Web/Controllers/ArrearsController.cs)).
  - **استثناء خطوة قائمة الفصل:** يأخذ `approvedBy` من النموذج ([SeparationRecordsController.cs:178-191](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)). والمعالج لا يفحص إلا الاختلاف ([SeparationRecordHandlers.cs:274-275](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)).
  - **الطالب في الحركات:** الانتداب والإعارة والترفيع والندب تُبقي `RequestedBy` المرسَل من العميل (`model.RequestedBy ??= …`) ([MovementsExtendedControllers.cs:267، 647، 917، 1081](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)). ومتحكم النقل نفسه يوثّق أن هذا الشكل ثغرة تُفسد فحص العينين ([TransferRequestsController.cs:87-91](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)).
  - **الترشيح للتدريب:** `NominatedBy` يُربط من النموذج ولا يضبطه الخادم ([TrainingExtendedControllers.cs:246-258](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
  - **ما تدّعيه الوثيقة:** «Approval actors are taken only from the authenticated token» ([SOD_MATRIX.md:35-38](SOD_MATRIX.md)).
- **الأثر:**
  - مستخدم `payroll-approver` واحد يُفرج عن صرف محجوز بسبب الاحتيال، أو يعدّل متأخرات، بكتابة أي GUID في حقل.
  - موظف HR واحد يتجاوز خطوة «تسوية الراتب» أو «إرجاع العهدة» في الفصل.
  - المدير يعتمد ترفيعه بعد نسبه لغيره.
- **الإصلاح المقترح:**
  - عينان حقيقيتان: الإجراء ينشئ طلباً معلّقاً، والفاعل الثاني يعتمده من جلسته. الهويتان من التوكن، ويُحذف حقل المعتمد من النماذج.
  - `RequestedBy` و`NominatedBy` من التوكن دائماً.
  - اختبار معماري يمنع معاملات `approvedBy`/`approverId` في المتحكمات.
- **التحقق المطلوب:**
  - سالب: POST إفراج بحقل معتمد ← يُتجاهَل ويبقى الحجز `Active` بانتظار معتمد. طلب ترفيع بـ `RequestedBy` مزوّر ← يُسجَّل الطالب الحقيقي، واعتماده لنفسه ← رفض.
  - موجب: مستخدمان مختلفان ← إفراج.

#### G-WF-08 — الاعتماد المزدوج فوق العتبات المالية غير منفّذ، والعتبات مثبتة في الشيفرة
- **الدليل:**
  - **ما تَعِد به المصفوفة:** اعتمادان للمسيّر فوق 50,000,000 ل.س، وللسلفة فوق 5,000,000 ([SOD_MATRIX.md:7، 9](SOD_MATRIX.md)). والثابتان في [SodMatrixDefaults.cs:9-10](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs).
  - **المسيّر:** `PayrollRun.Approve` ينقل التشغيل إلى `Approved` بقرار واحد ([PayrollRun.cs:135-149](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)). والمعالج يمرر `lastApproverId: null` ([PayrollRunHandlers.cs:119-133](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs))، فلا مرحلة ثانية أصلاً.
  - **السلفة:** اعتماد واحد ثم خطة أقساط ([AdvanceRequestHandlers.cs:128-154](../src/Services/HRContracts/HR.Contracts.Application/Features/AdvanceRequests/AdvanceRequestHandlers.cs)).
  - **قناة ESS تتجاوز سقف السلفة (أضيف بعد التحقق المستقل):**
    - واجهة HR تحدّ السلفة بـ 100,000 (`DefaultMaxSingleAdvance`، [LeaveAndAdvanceRules.cs:9](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs))، ويُفحص الحد عند الإنشاء والتعديل فقط ([AdvanceRequestHandlers.cs:53، 92](../src/Services/HRContracts/HR.Contracts.Application/Features/AdvanceRequests/AdvanceRequestHandlers.cs)).
    - لكن نموذج ESS لا يقيّد المبلغ ([EssPortalController.cs:203-207](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs))، و`ProcessAdvanceRequestedHandler` ينشئ السلفة دون `ValidateAdvanceRequest` ([InboundEventCommands.cs:292-326](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)).
    - و`ApproveAdvanceRequestHandler` لا يعيد فحص الحد ([AdvanceRequestHandlers.cs:121-156](../src/Services/HRContracts/HR.Contracts.Application/Features/AdvanceRequests/AdvanceRequestHandlers.cs)).
    - النتيجة: سلفة بأي مبلغ، حتى فوق عتبة 5,000,000، تُعتمد بشخص واحد. فجزء السلفة في هذه الفجوة قابل للبلوغ فعلاً. يبقى في الموجة 2 (WF-D)؛ والإصلاح البسيط المقترح: فحص الحد في المعالج الوارد وعند الاعتماد.
  - **عدد الاعتمادات المطلوب لا يُستخدم:** `GetRequiredApprovalCount` لا يستدعيه إلا المسار القديم ([ApprovalWorkflowService.cs:69-76](../src/Services/HRCore/HR.Core.Domain/Services/ApprovalWorkflowService.cs)).
- **الأثر:** مسيّر رواتب جهة كاملة، أو سلفة كبيرة، يعتمدهما شخص واحد، خلافاً للمصفوفة المعلنة وبطاقة IAM-003 «Done».
- **الإصلاح المقترح:**
  - عدد الاعتمادات المطلوب يُحسب عند الإرسال من عتبة مضبوطة لكل جهة (D-WF-3)، ويُحفظ على الكيان. لا `Approved` قبل اكتماله بفاعلين مختلفين.
  - ربط العتبات بمصدر قانوني/إعداد لا بثوابت.
  - الخيار الأبسط: مسيّر الرواتب والسلفة عبر المحرك بشرط خطوة على المبلغ.
- **التحقق المطلوب:**
  - موجب: مسيّر فوق العتبة ← بعد الاعتماد الأول يبقى `UnderReview` ← اعتماد ثانٍ من شخص آخر ← `Approved`.
  - سالب: المعتمد الأول يعتمد ثانيةً ← رفض. مسيّر تحت العتبة ← اعتماد واحد يكفي.

#### G-WF-09 — قرارات تُعتمد أو تُنفَّذ بشخص واحد في المسارات المحلية
- **الدليل:**
  - **الترشيح للتدريب:** `Approve` بلا سياسة وبلا فصل واجبات، وينشئ التسجيل في الدورة ([TrainingExtendedControllers.cs:272-322](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
  - **الإيفاد:** `Approve` ينشر `IMissionApproved` (`:398-420`). وPayroll يضيف بدل ابتعاث بقيمة `MissionAllowanceAmount` ([InboundEventHandlers.cs:541-556](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). المنشئ يُسجَّل (`MissionFile : AuditableEntity`، ويختمه `AuditSaveChangesInterceptor` بـ `CreatedBy ??=`، فيقبل قيمة من النموذج)، لكن `Approve` بلا سياسة ولا يقارنه بالمعتمد.
  - **DSAR:** أي مستخدم `compliance`، ومنه منشئ الطلب، ينقل الطلب إلى `InProgress` ثم ينفّذ **الحذف** النهائي عبر الخدمات دون اعتماد ثانٍ ([DsarRequestsController.cs:275-300، 359-413، 455-469](../src/Services/Compliance/Compliance.Web/Controllers/DsarRequestsController.cs)).
  - **تسوية نهاية الخدمة:** الكيان بلا `CreatedBy` (`BaseEntity`) ([EndOfServiceSettlement.cs:8](../src/Services/Payroll/Payroll.Domain/Entities/EndOfServiceSettlement.cs)). والاعتماد يفحص فقط «اعتُمدت من الشخص نفسه» ([EndOfServiceHandlers.cs:57-62](../src/Services/Payroll/Payroll.Application/Features/EndOfService/EndOfServiceHandlers.cs)).
  - **خطط التوزيع:** `Approve` بلا سياسة وبلا فصل واجبات ([MovementsExtendedControllers.cs:224-240](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - **معاملات الخدمة:**
    - `ServiceTransactionsController.Advance` يتيح لأي `hr-contracts` نقل المعاملة من `PendingApproval` إلى `Approved` دون فصل واجبات ([ServiceTransactionsController.cs:58-80](../src/Services/HRContracts/HR.Contracts.Web/Controllers/ServiceTransactionsController.cs)، [ServiceTransaction.cs:16، 58-80](../src/Services/HRContracts/HR.Contracts.Domain/Entities/ServiceTransaction.cs)).
    - في معاملة `Appointment` يتجاوز ذلك `Step3Approve`، ويترك بند `CareerRecordApproved` ناقصاً، فتعلق المعاملة ([HireWizardController.cs:244-280](../src/Services/HRContracts/HR.Contracts.Web/Controllers/HireWizardController.cs)، [HireWorkflowRules.cs:34-52](../src/Services/HRContracts/HR.Contracts.Domain/Services/HireWorkflowRules.cs)).
  - **الإجازة والسلفة:** فصل الواجبات يقارن المنشئ فقط، لا الموظف المستفيد ([LeaveRequestHandlers.cs:238-248](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
- **الأثر:**
  - حذف بيانات شخصية لا رجعة فيه، وبدلات مالية، وتسويات نهاية خدمة، كلها بقرار فرد.
  - موظف يعتمد إجازته إن أنشأها غيره. *(صُحّح بعد التحقق المستقل: ملف الإيفاد يسجّل منشئه عبر المعترض، والغائب هو المقارنة؛ الباقي مؤكد)*
- **الإصلاح المقترح:**
  - تسجيل المنشئ على كل كيان قابل للاعتماد (`AuditableEntity`).
  - قاعدة «المعتمد ≠ المنشئ ≠ المستفيد» عبر `employee_id`.
  - اعتماد ثانٍ لحذف DSAR (مسؤول حماية البيانات).
  - سياسات صريحة على كل `Approve`.
  - `Advance` لا ينقل إلى `Approved` (يُحصر في المسارات المتخصصة).
- **التحقق المطلوب:** لكل نطاق: المنشئ أو المستفيد يعتمد ← رفض، وشخص آخر بالدور ← قبول. حذف DSAR بفاعل واحد ← رفض.

#### G-WF-10 — تعديل بعد الاعتماد، ومسارات رفض وإلغاء غير محروسة
- **الدليل:**
  - **أمر التوريد:**
    - المحتوى قابل للتعديل وإضافة البنود بعد الاعتماد ([SupplyOrderWorkflowRules.cs:15-16](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/SupplyOrderWorkflowRules.cs)، [AdminExtendedControllers.cs:171-226](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AdminExtendedControllers.cs))، بلا إعادة اعتماد.
    - الاعتماد لا يسجل من اعتمد (`:244`).
  - **النقل:** فرق الراتب والبدل يُدخلهما منفّذ `hr-movements` عند التنفيذ، أي بعد الاعتماد ([TransferRequestsController.cs:286، 370-380، 395-399](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)). ثم يصلان إلى Payroll تسويةً مالية ([InboundEventHandlers.cs:287-299](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - **البطاقة:** `Edit` يغيّر `EmployeeId` دون فحص الحالة، حتى بعد الإصدار ([CardRequestsController.cs:85-104](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)).
  - **الإجازة والسلفة:** `Reject` و`RejectAjax` بلا سياسة الاعتماد، بخلاف `Approve` ([LeaveRequestsController.cs:194-210، 225-235](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveRequestsController.cs)، [AdvanceRequestsController.cs:165، 193](../src/Services/HRContracts/HR.Contracts.Web/Controllers/AdvanceRequestsController.cs)).
  - **الترفيع والندب:** لا إجراء رفض ولا إلغاء لهما، فيبقى الطلب `Pending` إلى الأبد ([MovementsExtendedControllers.cs:884-1230](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - **التأديب:** التعديل بعد نشر الحدث مفصّل في G-ER-06.
- **الأثر:**
  - ما يُصرف أو يُنفَّذ ليس ما اعتُمد: كمية التوريد، وراتب المنقول، وصاحب البطاقة.
  - والمعتمد المختص لا يملك وسيلة لرفض الترفيع.
- **الإصلاح المقترح:**
  - قفل المحتوى عند الاعتماد، وأي تعديل يعيد الحالة إلى `Pending` مع مسار جديد.
  - المبالغ المالية جزء من الطلب المعتمد، لا من التنفيذ.
  - سياسة الاعتماد نفسها على الرفض.
  - إجراءا رفض وإلغاء مقيدان بالحالة للترفيع والندب.
- **التحقق المطلوب:**
  - سالب: تعديل أمر توريد معتمد ← يعود إلى `Pending`. تنفيذ نقل بمبلغ مختلف عن المعتمد ← رفض. دور الوحدة دون سياسة الاعتماد يرفض إجازة ← 403.
  - موجب: رفض ترفيع من `hr-management-approver`.

#### G-WF-11 — التفويض لا أثر له
- **الدليل:**
  - **الإنشاء ينجح:** `DelegationsController.Create` ينشئ `ApprovalDelegation` ويعرض «تم إنشاء التفويض المؤقت» ([DelegationsController.cs:74-108](../src/Services/HRCore/HR.Core.Web/Controllers/DelegationsController.cs)).
  - **لا أحد يقرأ التفويضات:**
    - كل `new SeparationOfDutiesService()` يُبنى بلا تفويضات ([LeaveRequestHandlers.cs:238](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)، [TransferRequestsController.cs:211](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)، [ApprovalWorkflowService.cs:69، 135](../src/Services/HRCore/HR.Core.Domain/Services/ApprovalWorkflowService.cs)). وPayroll يسجّله singleton بلا تفويضات ([DependencyInjection.cs:29](../src/Services/Payroll/Payroll.Application/DependencyInjection.cs)).
    - `ResolveDelegate` و`IsActingAsDelegate` بلا مستدعٍ ([SeparationOfDutiesService.cs:158-178](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SeparationOfDutiesService.cs)).
    - الكيان `WorkflowDelegation` له `DbSet` فقط ([HRCoreDbContext.cs:79](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)).
    - المقيِّم والصندوق لا يعرفان المفوَّض، و`DelegateItemAsync` يرمي `NotSupportedException` ([HrCoreWorkInboxService.cs:35-36](../src/Services/HRCore/HR.Core.Web/Services/HrCoreWorkInboxService.cs)).
  - **ما تدّعيه الوثائق:** [WF_001_WORKFLOW_ENGINE.md:56-57](WF_001_WORKFLOW_ENGINE.md)، [SOD_MATRIX.md:40-45](SOD_MATRIX.md)، و[WF_002_WORK_INBOX.md:12، 77](WF_002_WORK_INBOX.md).
- **الأثر:**
  - يعتقد المدير أنه فوّض قبل إجازته، لكن المفوَّض لا يرى شيئاً ولا يستطيع الاعتماد.
  - الأدوار التي يحملها شخص واحد في الجهة (المعتمد الإداري، المراجع المستقل المعيَّن، ومعتمد الرواتب) تُوقف المسارات طوال غيابه.
- **الإصلاح المقترح:**
  - المقيِّم يقبل الفاعل نيابة عن أصيل يحمل الدور بتفويض نافذ يغطي النوع.
  - التوقيع يسجّل «بالنيابة عن».
  - الصندوق يعرض عناصر الأصيل للمفوَّض.
  - لا يتجاوز التفويض فصل الواجبات: لا يعتمد المفوَّض ما أنشأه.
  - انتهاء تلقائي، وتدقيق.
  - توحيد الكيانين في كيان واحد.
- **الخطة:** [PLAN-50](acceptance-plans/PLAN-50-iam-approver-reach-delegation.md)
- **التحقق المطلوب:**
  - موجب: تفويض نافذ ← المفوَّض يرى العنصر ويعتمده، والتوقيع يذكر الأصيل.
  - سالب: بعد الانتهاء أو الإلغاء ← رفض. المفوَّض منشئ الطلب ← رفض.

#### G-WF-12 — سلامة نسخة المسار تحت التزامن والفشل
- **الدليل:**
  - **لا قفل تزامن:** لا رمز تزامن على `WorkflowInstance` ([HRCoreDbContext.cs:875-882](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)). في HR.Core كله لا يحمل `xmin` إلا `Employee` و`ApprovalRequest` القديم (`:152`، `:579`)، فالمسار القديم محمي والمحرك ليس محمياً. اعتمادان متزامنان يفقدان قيداً من `StepHistoryJson`، أو يكملان النسخة مرتين، فيعمل المعالج مرتين.
  - **متابعة PLAN-25 (الملاحظة 8) — مؤكَّدة:**
    - الفهرس `(EntityType, EntityId)` غير فريد (`:877`)، والمنسّق يقرأ ثم يُدرج ([ApprovalStartCoordinator.cs:172-192](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs))، فتقديمان متزامنان ينشئان نسختين `InProgress`.
    - الصندوق يعرض النسختين، لأنه لا يتحقق من ارتباط النسخة بالكيان ([WorkflowInboxQueryService.cs:191-205](../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs)).
    - من يعتمد النسخة الشبح يضيع جهده، إذ يتجاهلها المعالج ([RecruitmentRequisitionWorkflowHandler.cs:17-19](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionWorkflowHandler.cs)).
  - **النسخة والأثر غير ذريين:**
    - النسخة تُحفظ مكتملة ([WorkflowInstanceActionService.cs:117](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs))، ثم يحفظ المعالج الكيان في حفظ منفصل ([RecruitmentRequisitionWorkflowHandler.cs:32](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionWorkflowHandler.cs)).
    - فشل المعالج يترك نسخة `Completed` وكياناً `PendingApproval`.
    - الإصلاح القائم لا يعالج إلا حالة «بلا نسخة» ([ApprovalRepairService.cs:60-110](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalRepairService.cs)).
  - **نسخ يتيمة في العهدة:** بدء العهدة عبر HTTP يسبق الحفظ المحلي ([AssetsController.cs:455-472](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs))، ولا `[Idempotent]` على الإجراء. فشل الحفظ أو النقر المزدوج يترك نسخاً يتيمة.
- **الأثر:** اعتمادات ضائعة، وكيانات عالقة بلا مخرج، وسجل تواقيع لا يطابق التاريخ المحفوظ في النسخة. *(صُحّح بعد التحقق المستقل: عبارة «لا xmin في HR.Core كله» خاطئة؛ `Employee` و`ApprovalRequest` يحملانه، والفجوة على `WorkflowInstance` مؤكدة)*
- **الإصلاح المقترح:**
  - `xmin` رمز تزامن على `WorkflowInstance`، مع رسالة تعارض واضحة.
  - فهرس فريد جزئي `(TenantId, EntityType, EntityId) WHERE Status='InProgress'`، والتقاط التعارض في المنسّق.
  - المعالج داخل معاملة الاعتماد نفسها، أو تطبيق الأثر عبر Outbox داخلي.
  - مهمة مصالحة تكشف «نسخة منتهية وكيان معلّق» وتُصلحه.
  - الصندوق يشترط `entity.WorkflowInstanceId == instance.Id`.
- **الخطة:** [PLAN-44](acceptance-plans/PLAN-44-wf-instance-integrity.md)
- **التحقق المطلوب:**
  - تكامل PostgreSQL: تقديمان متزامنان ← نسخة واحدة. اعتمادان متزامنان للخطوة نفسها ← نجاح واحد وتعارض. فشل محقون في المعالج ← لا نسخة مكتملة دون أثر.

#### G-WF-13 — ادعاءات «Done» بلا أدلة، والقبول لا يغطي أي اعتماد خارج التوظيف والملاك
- **الدليل:**
  - **WF-001:** «Done» بنطاق يشمل parallel وquorum وtimeouts/escalation وdelegation وSLA وsubflows وsimulation وmigration وguardrails ([completion-backlog/09:176-192](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md)). والمحرك تسلسلي فقط (G-WF-16).
    - ومن أدلة الإغلاق جسر `CreateViaEngineOrLegacy`، وهو بلا أي مستدعٍ ([ApprovalWorkflowService.cs:21-57](../src/Services/HRCore/HR.Core.Domain/Services/ApprovalWorkflowService.cs)).
  - **WF-002:** «Done»، وفيه `[x]` على التفويض والتصعيد ([WF_002_WORK_INBOX.md:12-13، 73-77](WF_002_WORK_INBOX.md)). واختباراته انعكاس ووجود ملفات ([Wf002WorkInboxTests.cs:22-121](../src/Tests/HRManagement.UnitTests/Wf002WorkInboxTests.cs)).
  - **IAM-003:** «Done»، بمعيار «قواعد الفصل تطبق في الخادم على كل قناة» ([completion-backlog/02:47-57](completion-backlog/02-P1-PLATFORM-INTEGRITY.md)). وG-WF-07 وG-WF-08 وG-WF-09 تنقضه.
  - **AC-33:** يَعِد بـ «فقد + تسوية ⇒ مسار اعتماد» ([PLAN-12:25](acceptance-plans/PLAN-12-logistics-custody.md)). لكن الاختبار يقرأ صفحة ويقبل 403 ([Ac33AssetLossLiveTests.cs:31](../src/Tests/HRManagement.AcceptanceTests/Ac33AssetLossLiveTests.cs)).
  - **AC-11:** يمرر `Mode = Enforce` من العميل، بمعرّف تسليم عشوائي ([Ac11PolicyWorkflowTests.cs:27-44](../src/Tests/HRManagement.AcceptanceTests/Ac11PolicyWorkflowTests.cs)).
  - **AC-12:** محجوب ([summary.md](../acceptance-evidence/summary.md)).
  - **لا سيناريو قبول آخر:** لا سيناريو لاعتماد إجازة أو نقل أو فصل أو بطاقة أو حجز صرف.
- **الأثر:** المالك يرى نطاقات «مكتملة»، بينما أربعة منها لا اعتماد فيها، ومسار المحرك الوحيد خارج التوظيف معطّل.
- **الإصلاح المقترح:** خطة WF-I:
  - تصحيح الوثائق والبطاقات.
  - سيناريوهات AC-WF لكل خطة على الملف الشبيه بالإنتاج، موجبة وسالبة.
  - استبدال اختبارات الانعكاس باختبارات سلوك.
- **الخطة:** [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md)، و[PLAN-45](acceptance-plans/PLAN-45-wf-asset-custody-approval-loop.md) (جزئياً: AC-11 وAC-33 للعهدة والفقدان يختبران الاعتماد فعلاً)
- **التحقق المطلوب:** مجلد أدلة لكل سيناريو AC-WF-*، ومراجعة تطابق الوثائق مع الشيفرة.

### 4.3 قدرات ناقصة (P2)

#### G-WF-14 — لا مهل ولا تصعيد ولا إشعار للمعتمد
- **الدليل:**
  - **المهلة والتصعيد بلا تنفيذ:** `TimeoutHours` و`EscalationRole` معرَّفان في الخطوة ([WorkflowStepSnapshot.cs](../src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStepSnapshot.cs))، ولا يُستخدمان إلا في مؤشر التوظيف ([ApprovalSlaRules.cs:7](../src/Services/HRCore/HR.Core.Domain/Services/RecruitmentKpi/ApprovalSlaRules.cs)، [RecruitmentMetricsService.cs:631](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentMetricsService.cs)). ولا مهمة خلفية للمسارات ([Program.cs:146-168](../src/Services/HRCore/HR.Core.Web/Program.cs)).
  - **الصندوق:** الأولوية دائماً `Normal` ولا `DueDate` ([WorkInboxItemMapper.cs:25، 45](../src/Services/HRCore/HR.Core.Web/Services/WorkInboxItemMapper.cs)). و`EscalateItemAsync` يرمي استثناء ([HrCoreWorkInboxService.cs:38-39](../src/Services/HRCore/HR.Core.Web/Services/HrCoreWorkInboxService.cs)).
  - **قوائم التحقق:** `OwnerRole` و`SlaHours` في قوائم الفصل والتعيين والحركة يُعرضان في الشاشة فقط ([Step4.cshtml:19](../src/Services/HRContracts/HR.Contracts.Web/Views/HireWizard/Step4.cshtml)). لا يفرض أي كود الدور المالك، ولا يحسب التأخر ([SeparationChecklistDefaults.cs:11-20](../src/Services/HRContracts/HR.Contracts.Domain/Services/SeparationChecklistDefaults.cs)).
  - **لا إشعار:** لا يُبلَّغ المعتمد التالي (لا استخدام لـ `INotificationService` في مسارات الاعتماد).
  - **أنواع الخطوات:** `Notification` و`Review` و`Condition` تُعامل كاعتماد ([WorkflowStepType.cs](../src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStepType.cs)، [WorkflowEngine.cs:39-82](../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowEngine.cs)).
- **الأثر:** لا يعرف أحد أن طلباً تجاوز مهلته القانونية. الطلب المنسي يبقى منسياً، ويتصيّد المعتمد الطلبات يدوياً.
- **الإصلاح المقترح:**
  - مهمة مهل تحسب الاستحقاق من `TimeoutHours`، وتصعّد إلى `EscalationRole`، وتسجّل ذلك في التاريخ.
  - إشعار داخلي عند وصول الخطوة.
  - `DueDate` وأولوية في الصندوق.
  - قيم المهل من D-WF-6.
- **التحقق المطلوب:** خطوة بمهلة ساعة بلا إجراء ← تصعيد مسجّل وإشعار وظهور عند دور التصعيد. اعتماد قبل المهلة ← لا تصعيد.

#### G-WF-15 — صندوق العمل «الموحد» يغطي 5 أنواع في HR.Core فقط
- **الدليل:**
  - **الأنواع المغطاة:** `SupportedEntityTypes` خمسة ([WorkflowInboxQueryService.cs:22-29](../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs)). لا مسابقات ولا نتائج ولا عهدة، ولا شيء من العقود أو الحركة أو الرواتب أو البطاقات أو التدريب أو الامتثال أو اللوجستيات.
  - **صندوق الفريق:** يعيد قائمة فارغة دائماً ([HrCoreWorkInboxService.cs:26-27](../src/Services/HRCore/HR.Core.Web/Services/HrCoreWorkInboxService.cs)).
  - **الأداء:** كل عرض يحمّل كل النسخ الجارية وكل الطلبات القديمة المعلّقة بلا حد (`:45-49، 103-107`).
  - **ما تدّعيه البطاقات:** PROD-005 «Done» بمعيار «لا معاملة تضيع بين الوحدات» ([completion-backlog/07:473-476](completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md)).
- **الأثر:** معتمد الإدارة العليا يتنقل بين 8 وحدات ليعرف ما ينتظره. ولا عدّاد يعكس الواقع.
- **الإصلاح المقترح:**
  - إسقاط `WorkInboxItem` مُخزَّن، تغذيه أحداث موحدة من كل خدمة: مطلوب، اكتمل، أُلغي (D-WF-7).
  - صندوق الفريق حسب الدور والنطاق.
  - ترقيم الصفحات.
  - روابط عميقة لكل خدمة.
- **التحقق المطلوب:** طلب إجازة وطلب نقل وحجز صرف يظهر كل منها لمعتمده فقط، ويختفي عند البت. تعطّل خدمة المصدر لا يُخفي العنصر.

#### G-WF-16 — إدارة التعريفات ودورة حياتها ناقصة
- **الدليل:**
  - **لا واجهة:** إدارة التعريفات عبر API فقط، ولا شاشة لها في `Views`.
  - **إصدار الجهة يتعارض:** `(Code, Version)` فريد على مستوى كل الجهات ([HRCoreDbContext.cs:868](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs))، و`Create` يحسب الإصدار التالي بمرشح الجهة ([WorkflowDefinitionsController.cs:49-51](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowDefinitionsController.cs)). فإصدار جهة لرمز قائم يصطدم بإصدار الجهة الافتراضية («يحتاج تحقق»).
  - **البذور:** تتخطى أي رمز موجود، فتغييرات الخطوات لا تصل إلى قواعد البيانات القائمة ([WorkflowPackSeeder.cs:88-93](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)).
  - **المحرك تسلسلي فقط:** لا خطوات متوازية، ولا «أيٌّ من»، ولا إعادة للتصحيح، ولا طلب استيفاء، ولا إلغاء أو سحب عام ([IWorkflowEngine.cs](../src/Shared/HRManagement.ServiceDefaults/Workflow/IWorkflowEngine.cs)).
  - **لا ترحيل ولا محاكاة:** لا ترحيل للنسخ الجارية، ولا محاكاة قبل النشر.
- **الأثر:** «اختلاف السلسلة حسب الجهة دون تفرّع في الشيفرة»، وهو الهدف المعلن، غير ممكن عملياً. وأي تعديل يحتاج مطوّراً.
- **الإصلاح المقترح:**
  - شاشة إدارة بالحوكمة المذكورة في G-WF-04.
  - مفتاح فريد `(TenantId, Code, Version)`.
  - آلية ترقية البذور بإصدار جديد.
  - عمليات عامة في المحرك: `ReturnForCorrection` و`RequestInfo` و`Withdraw` و`Cancel` مع تاريخ.
  - خطوة جماعية بحد أدنى (quorum).
- **التحقق المطلوب:** جهة تنشر إصداراً خاصاً بثلاث خطوات ← طلباتها فقط تتبعه. إعادة للتصحيح ← المنشئ يعدّل ويعيد التقديم على النسخة نفسها.

#### G-WF-17 — تعريفات وقواعد سياسة يتيمة، وقائمة السماح تبررها بأسباب غير صحيحة
- **الدليل — تقييم متابعة PLAN-25 لقائمة السماح ([WorkflowDefinitionsHaveHandlersTests.cs:15-23](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs)):**

  | النوع | السبب المكتوب | الواقع |
  |-------|---------------|--------|
  | `ServiceTransaction` (HIRE-WIZARD) | العقود تقود الحالة | صحيح جزئياً: `Step3Approve` محلي، والتعريف لا يُبدأ أبداً، فهو زخرفي |
  | `SeparationRecord` | العقود تنشر الانفصال | لا اعتماد أصلاً (G-WF-01) |
  | `CardRequest` | IDCard تعالج محلياً | لا اعتماد أصلاً (G-WF-01) |
  | `TransferRequest` | اعتماد محلي بـ `HrManagementApprove` | صحيح، لكن التعريف (`hr-core` مرتين) يخالف الواقع (`hr-management-approver` مرة واحدة) ([WorkflowPackSeeder.cs:38-40](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)) |
  | `DisciplinaryAction` | Compliance تملك الإجراء | لا مستهلك ولا اعتماد (G-ER-01) |
  | `Ticket` | IT تملك التصعيد | لم نجد استدعاءً للمحرك في IT («يحتاج تحقق») |

  - **قواعد سياسة يتيمة:** القواعد `R-ID-PROCESS` و`R-TRF` و`R-DISC`، وقواعد `HIRE-WIZARD` و`SEPARATION-WIZARD` و`IT-TICKET`، تطلق هذه التعريفات ([PolicyEngineDomainSeeds.cs:15-19، 43، 51، 74-78، 120-124، 140](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)). لا يستدعي المحرك منها إلا IDCard، ثم يتجاهل النسخة. في وضع Enforce ينشأ نمط `RECRUITMENT-HIRE` (G-14) من جديد.
  - **بدء من خارج النطاق:** يمكن بدء هذه التعريفات من `Start` العام، فتنشأ نسخ لا تغيّر شيئاً.
- **الأثر:** الاختبار المعماري أخضر لكنه يغطي غياب الاعتماد في 4 نطاقات، وتظهر للمالك سلاسل اعتماد غير موجودة.
- **الإصلاح المقترح:**
  - بعد WF-C: إما ربط كل نوع فعلاً، أو سحبه كما سُحب `RECRUITMENT-HIRE` (تعريفاً وقاعدة).
  - الاختبار يشترط لكل نوع في القائمة رمزاً قابلاً للفحص لمستهلك النتيجة في الخدمة المسماة، لا جملة نصية.
- **الخطة:** [PLAN-46](acceptance-plans/PLAN-46-wf-cross-service-approvals.md)، و[PLAN-49](acceptance-plans/PLAN-49-pol-engine-scope-orphans.md) (جزئياً: سحب قواعد السياسة اليتيمة التي تطلق الرموز المسحوبة، ومنها `R-ID-PROCESS`)، و[PLAN-60](acceptance-plans/PLAN-60-sep-unblock-decision-governance.md) (جزئياً: مدخل `SeparationRecord` في قائمة السماح)
- **التحقق المطلوب:** القائمة فارغة، أو كل بند فيها يشير إلى مستهلك موجود. Enforce مع `R-ID-PROCESS` ← لا نسخة يتيمة.

#### G-WF-18 — القرار الإداري سجلّ حر بلا إصدار ولا اعتماد
- **الدليل:**
  - **سجل نصي:** `AdministrativeDecision` بلا حالة، و`IssuedBy` نص ([AdministrativeDecision.cs:13-51](../src/Services/HRContracts/HR.Contracts.Domain/Entities/AdministrativeDecision.cs)).
  - **لا سياسات:** `Create` و`Edit` و`Delete` بلا سياسة صريحة ([AdministrativeDecisionsController.cs:86-120، 155-211، 213-230](../src/Services/HRContracts/HR.Contracts.Web/Controllers/AdministrativeDecisionsController.cs)).
  - **قرار التعيين نفسه:** معالج التعيين يصدر قرار التعيين بهذا الكيان (G-22 في دراسة التوظيف).
- **الأثر:** القرار الرسمي، كقرار التعيين أو الفصل، يعدّله أو يحذفه أي موظف عقود بعد صدوره، بلا أثر ولا رقم محجوز.
- **الإصلاح المقترح:**
  - دورة حياة: مسودة ← بانتظار التوقيع (عبر المحرك) ← صادر برقم غير قابل للتعديل ← ملغى بقرار لاحق.
  - لا حذف بعد الصدور.
  - التوقيع الإلكتروني لاحقاً ضمن SIG-001.
- **التحقق المطلوب:** حذف قرار صادر ← رفض. تعديله ← رفض. إصداره دون اعتماد ← رفض.

### 4.4 تحسينات (P3)

#### G-WF-19 — مواءمة الصندوق مع الخادم، والمسار القديم، وشيفرة ميتة
- **الدليل:**
  - **تقييم متابعة PLAN-25 (الملاحظة 7):**
    - الصندوق يستخدم `CreatedBy ?? InitiatedBy` ([WorkflowInboxQueryService.cs:203، 222، 243، 282](../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs))، بينما البوابات تستخدم `CreatedBy` وحده ([RecruitmentApprovalGates.cs:22-32](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/RecruitmentApprovalGates.cs)).
    - عبارة «يُخفي ولا يمنح» صحيحة بالنسبة للصندوق. لكن حين يكون `CreatedBy` فارغاً، يُخفي الصندوق العنصر عن المنشئ، بينما يسمح له الخادم باعتماد الخطوة 1.
    - والمُقدِّم غير المنشئ لا يُمنع من الخطوة 1 في الحالتين.
    - **الحكم:** خطر منخفض. الإصلاح تعريف واحد للمنشئ في البوابة (D-WF-8)، لا في الصندوق.
  - **المسار القديم:** `ApprovalsController.Approve` لا يفحص `RoleRequired` للخطوة ([ApprovalsController.cs:102-149](../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs)، [ApprovalWorkflowService.cs:104-181](../src/Services/HRCore/HR.Core.Domain/Services/ApprovalWorkflowService.cs)). و`Cancel` متاح لأي `HrCoreWrite` لا لصاحب الطلب (`:184-203`). يستخدمه `ApprovedCV` وحده.
  - **شيفرة ميتة:** `CreateViaEngineOrLegacy`، و`AssetAssignmentWorkflowSeeder` و`AssetLossWorkflowSeeder`.
- **الأثر:** تباين صغير بين ما يراه المستخدم وما يقبله الخادم، وشيفرة توهم بقدرات غير موجودة.
- **الإصلاح المقترح:**
  - ترحيل `ApprovedCV` إلى المحرك، وحذف المسار القديم والجسر والبذرتين.
  - الصندوق يسأل المقيِّم نفسه لكل عنصر بعد تجميع الاستعلامات.
- **الخطة:** [PLAN-44](acceptance-plans/PLAN-44-wf-instance-integrity.md) — عدا ترحيل `ApprovedCV` إلى المحرك (الموجة 2، WF-D)
- **التحقق المطلوب:** ثبات `Hcm025PanelServerParityTests` بعد توسيعه ليشمل الصندوق، وحالة `CreatedBy` فارغ.

### 4.5 تقاطعات مع الدراسات الأخرى

- **→ POL:** `POST api/policies/enforce` يقبل من العميل `Mode` و`ActorUserId` (`??=`) و`TenantId` و`SubjectType`، ويبدأ بها مسارات ([PoliciesController.cs:27-37](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)، [PolicyOrchestrator.cs:168-177](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs)).
- **→ POL:** عتبات الـ shim (10,000 و5,000) مثبتة في الشيفرة ([PolicyEnforceClient.cs:125، 146](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)). ولا مفتاح `PolicyEngine` في أي خدمة غير HR.Core، فوضعها `Off` (`:29`).
- **→ IAM:** `WorkflowInstancesController` (`List` و`Get` و`History`) بلا نطاق فرع، ويقرؤه أي حساب خدمة بنطاق `hr-core.lookup`، ومعه `ContextJson` ([WorkflowInstancesController.cs:25-46، 134-142](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)، [HrManagementAuthorizationExtensions.cs:62-71](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- **→ IAM:** المعتمد الثاني لمنح الأدوار المميزة يختاره المنشئ ولا يتصرف هو ([AuthUsersController.cs:67، 93-126](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthUsersController.cs)).
- **→ IAM:** نطاقات حسابات الخدمة تختلف بين البيئة التطويرية والإنتاج: `svc-hr-contracts` يحمل `hr-core.workflow` في التطوير فقط ([sync-keycloak-dev-realm.ps1:521](../scripts/sync-keycloak-dev-realm.ps1)، [docker-compose.production.yml:290-292](../docker-compose.production.yml)).
- **→ IAM:** `Delegations/Index` يعرض كل التفويضات لكل من يملك `ManageDelegations` ([DelegationsController.cs:26-61](../src/Services/HRCore/HR.Core.Web/Controllers/DelegationsController.cs)).
- **→ QA:** AC-33 ينجح باستجابة 403 ([Ac33AssetLossLiveTests.cs:31](../src/Tests/HRManagement.AcceptanceTests/Ac33AssetLossLiveTests.cs)). وAC-11 يمرر وضع المحرك من العميل ([Ac11PolicyWorkflowTests.cs:43](../src/Tests/HRManagement.AcceptanceTests/Ac11PolicyWorkflowTests.cs)). وAC-12 وAC-EST-24e محجوبان ([summary.md](../acceptance-evidence/summary.md)). ولا اختبار تكامل للمسار الكامل: اعتماد ← Outbox ← مستهلك.
- **→ OPS:** لا مقياس ولا تنبيه للعناصر العالقة: `PendingApproval` بلا نسخة، أو نسخة جارية تجاوزت مهلتها، أو صفوف Outbox لم تُرسل. و`ApprovalMetrics` يغطي المسار القديم وحده ([ApprovalsController.cs:127-133](../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs)).

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-WF-1 | أي قرارات تمر إلزامياً باعتماد، وبأي سلسلة (خطوات وأدوار وسلطة)؟ | **كل قرار ينهي الخدمة أو يمس الأجر أو يمنح صلاحية دخول أو يحذف بيانات شخصية** يمر باعتماد على الخادم: الفصل، والتأديب، والبطاقة، وحذف DSAR، وحجز الصرف. السلطة لكل نوع من المرجع القانوني، ويبقى `SYD-001` شرطاً للتأديب | WF-C، WF-D، ER-A |
| D-WF-2 | من يعتمد العهدة والفقدان؟ | الخطوة 1 لمسؤول الأصول (`admin-logistics`) بسياسة اعتماد ضيقة. الخطوة 2 لـ `hr-management-approver` للأصول المميزة. فرض الدور للنوعين. سحب v1 القديم | WF-B |
| D-WF-3 | عتبات الاعتماد المزدوج (رواتب 50 مليون، سلفة 5 ملايين، أصل 10,000 ل.س) | قيم إعداد لكل جهة مربوطة بمصدر قانوني، لا ثوابت. والقيم الحالية افتراضية مؤقتة | WF-D |
| D-WF-4 | من يغيّر تعريفات المسارات، وهل للجهة تخصيصها؟ | دور مركزي `workflow-admin` ونشر بعينين. للجهة أن **تضيف** خطوات فقط، لا أن تحذف خطوة إلزامية | WF-A، WF-H |
| D-WF-5 | سياسة التفويض | تفويض بحد أقصى 90 يوماً بنطاق نوع أو دور، وغياب آلي (out-of-office). المفوَّض لا يعتمد ما أنشأه، ولا تفويض متسلسل | WF-F |
| D-WF-6 | مهل البت والتصعيد لكل نوع قرار | من المرجع القانوني حيث يوجد (مثل مهل التظلم والإجازة)، وإلا قيمة إدارية موثقة تُراجع | WF-F |
| D-WF-7 | نطاق صندوق العمل الموحد | إسقاط مُخزَّن في HR.Core تغذيه أحداث موحدة من كل خدمة. البديل صناديق منفصلة لكل خدمة، وهو أرخص لكنه يكسر وعد WF-002 | WF-G |
| D-WF-8 | من هو «المنشئ» الممنوع من الاعتماد؟ | المنشئ ∪ المُقدِّم ∪ الطالب الحقيقي ∪ الموظف المستفيد، ممنوعون من **كل** خطوات النسخة | WF-A، WF-D، WF-E |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 1 (P0)   WF-A (حوكمة التعريفات وواجهة المحرك)  ║  WF-B (العهدة والفقدان + الـ Outbox)
الموجة 2 (P0)   WF-C (الربط العابر للخدمات: الفصل والبطاقة، وآلية ER-A)
الموجة 3 (P1)   WF-D (فصل المهام في الاعتمادات المحلية)  ║  WF-E (سلامة النسخة والتزامن)
الموجة 4 (P1/P2) WF-F (التفويض والمهل والتصعيد)  ←  WF-G (صندوق العمل الموحد)
الموجة 5 (P2)   WF-H (إدارة التعريفات والقرار الإداري)
مستمر          WF-I (القبول الحي والتوثيق) — يُغلق مع كل خطة
```

**ربط الخطط بأرقام PLAN (الموجة 1، 2026-10-30):** خطط الموجة 1 صارت ملفات في `acceptance-plans/`، وما بقي برمزه الحرفي فهو «الموجة 2». ترتيب الموجات في المخطط أعلاه تاريخي، والترتيب المعتمد هو عمود «يعتمد على».

| الخطة | رقم التنفيذ | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) |
|-------|-------------|---------|---------|----------|-----------|-------------------|
| WF-A | [PLAN-43](acceptance-plans/PLAN-43-wf-definition-governance-engine-api.md) | حوكمة التعريفات وتقوية واجهة المحرك | G-WF-04، G-WF-05، G-WF-06 | P0 | PLAN-42، D-WF-4، D-WF-8 | 7–8 |
| WF-B | [PLAN-45](acceptance-plans/PLAN-45-wf-asset-custody-approval-loop.md) | إغلاق حلقة اعتماد العهدة والفقدان | G-WF-02 (+ G-IDA-02) | P0 | PLAN-40، PLAN-43، D-WF-2 | 8–10 |
| — | [PLAN-40](acceptance-plans/PLAN-40-msg-atomic-event-publishing.md) (مؤلف OPS) | نشر الأحداث ذرياً (خرج من WF-B) | G-WF-03 (+ G-OPS-08) | P0 | PLAN-34 | — |
| WF-C | [PLAN-46](acceptance-plans/PLAN-46-wf-cross-service-approvals.md) | الاعتماد العابر للخدمات: العقد العام، والبطاقة والتأديب، وجهة HR.Core للفصل | G-WF-01، G-WF-17 | P0 | PLAN-40، PLAN-43، PLAN-45، D-WF-1 | 10 |
| — | [PLAN-60](acceptance-plans/PLAN-60-sep-unblock-decision-governance.md) (مؤلف SEP) | جهة HR.Contracts لاعتماد الفصل (يستهلك عقد PLAN-46) | G-WF-01 (الفصل) | P0 | PLAN-46 | — |
| WF-D | الموجة 2 | فصل المهام في الاعتمادات المحلية (ومنها سقف سلفة ESS) | G-WF-07، G-WF-08، G-WF-09، G-WF-10 | P1 | D-WF-3، D-WF-8 | 8–10 |
| WF-E | [PLAN-44](acceptance-plans/PLAN-44-wf-instance-integrity.md) | سلامة النسخة تحت التزامن والفشل | G-WF-12، G-WF-19 (عدا ترحيل `ApprovedCV`) | P1 | PLAN-43 (وينسَّق مع PLAN-40) | 5–6 |
| WF-F | الموجة 2 (أثر التفويض في [PLAN-50](acceptance-plans/PLAN-50-iam-approver-reach-delegation.md)) | التفويض والمهل والتصعيد والإشعار | G-WF-11 ← PLAN-50، G-WF-14 ← الموجة 2 | P1/P2 | WF-E، D-WF-5، D-WF-6 | 7–9 |
| WF-G | الموجة 2 | صندوق العمل الموحد عبر الخدمات | G-WF-15 | P2 | WF-C، WF-F، D-WF-7 | 7–9 |
| WF-H | الموجة 2 | إدارة التعريفات والقرار الإداري | G-WF-16، G-WF-18 | P2 | WF-A، WF-C | 6–8 |
| WF-I | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) | التحقق والتوثيق | G-WF-13 | P1 ← مستمر | كل الخطط | 4–6 |
| **المجموع** | | | | | | **≈ 62–76** (دون PLAN-40 وPLAN-60) |

**WF-A (PLAN-43) — حوكمة التعريفات وتقوية واجهة المحرك.** تُغلق أبواب إلغاء ضمانات PLAN-25 قبل توسيعها.
- سياسة `WorkflowAdmin`، ونشر بعينين، وتحقق من الأدوار والحد الأدنى للخطوات لكل نوع، وتدقيق.
- `WorkflowStarter` يحترم `ValidFrom` و`ValidTo`.
- `Start` لحسابات الخدمة فقط، بقائمة أنواع لكل حساب، وفاعل حقيقي في ترويسة موقّعة.
- إخراج حسابات الخدمة من `HrCoreBranchOperation`.
- الفرض افتراضي لكل الأنواع، وقاعدة «لا فاعل في خطوتين».
- السيناريوهات: AC-WF-Aa (`hr-core` ينشئ تعريفاً ← 403)، AC-WF-Ab (نشر بلا دور لنوع مفروض ← رفض)، AC-WF-Ac (الناشر = المنشئ ← رفض)، AC-WF-Ad (حساب خدمة يعتمد ← 403)، AC-WF-Ae (الفاعل نفسه في خطوتين ← رفض).

**WF-B (PLAN-45) — ربط اعتماد العهدة والفقدان وإصلاح نشر الأحداث.**
- النشر قبل الحفظ في المواقع الأربعة، واختبار معماري لذلك. *(انتقل إلى PLAN-40 مع AC-WF-Bd)*
- لا `PendingApproval` بلا نسخة، مع بدء مؤجل بإعادة المحاولة.
- سحب الطلب وإعادة بدئه.
- لوحة اعتماد ودخول إلى الصندوق، وتصحيح الرابط.
- سياسة ضيقة لـ `admin-logistics`، ومستهلك يتحقق من `InstanceId`.
- سحب v1 القديم بسكربت مدقَّق.
- السيناريوهات: AC-WF-Ba (تسليم مميَّز ← اعتماد خطوتين من الصندوق ← `Active` في AdminLogistics)، AC-WF-Bb (HR.Core متوقف ← لا تجميد للأصل)، AC-WF-Bc (فقدان يعتمده `admin-logistics` ثم المعتمد ← `Lost`)، AC-WF-Bd (تنفيذ ترفيع ← سجل التوظيف محدَّث)، AC-WF-Be (نسخة غير مرتبطة ← لا أثر).

**WF-C (PLAN-46) — الربط العابر للخدمات وقرارات الوظيفة.**
- `POST api/workflow-approvals/start` بدلالات المنسّق، و`IWorkflowOutcomeDecided` عبر الـ Outbox، ومكتبة مستهلك مشتركة تتحقق من الارتباط.
- ربط الفصل وإصدار البطاقة.
- إتاحة الآلية لـ ER-A.
- ربط تعريفات قائمة السماح الست أو سحبها، وتشديد `WorkflowDefinitionsHaveHandlersTests`.
- السيناريوهات: AC-WF-Ca (فصل ← لا أثر قبل الاعتماد ← تنفيذ بعده)، AC-WF-Cb (`Step3Execute` قبل الاعتماد ← رفض)، AC-WF-Cc (معالج البطاقة = طالبها ← رفض)، AC-WF-Cd (Enforce مع `R-ID-PROCESS` ← نسخة مرتبطة لا يتيمة)، AC-WF-Ce (قائمة السماح فارغة أو كل بند فيها يشير إلى مستهلك).

**WF-D (الموجة 2) — فصل المهام في الاعتمادات المحلية.**
- عينان حقيقيتان لحجز الصرف وتنبيه الاحتيال والمتأخرات واستثناء قائمة الفصل.
- `RequestedBy` و`NominatedBy` من التوكن.
- اعتماد ثانٍ فوق العتبات للمسيّر والسلفة.
- المعتمد ≠ المستفيد.
- سياسات صريحة على كل `Approve` و`Reject`.
- قفل المحتوى بعد الاعتماد (التوريد، والبطاقة، ومبالغ النقل).
- رفض وإلغاء للترفيع والندب.
- اعتماد ثانٍ لحذف DSAR، ومنشئ مسجَّل لتسوية نهاية الخدمة.
- السيناريوهات: AC-WF-Da (إفراج بحقل معتمد ← يبقى `Active`)، AC-WF-Db (مسيّر فوق العتبة ← اعتمادان من شخصين)، AC-WF-Dc (ترفيع بطالب مزوّر ← اعتماد الذات مرفوض)، AC-WF-Dd (تعديل توريد معتمد ← `Pending`)، AC-WF-De (حذف DSAR بفاعل واحد ← رفض)، AC-WF-Df (موظف يعتمد إجازته ← رفض).

**WF-E (PLAN-44) — سلامة النسخة تحت التزامن والفشل.**
- `xmin` على `WorkflowInstance`، وفهرس فريد جزئي للنسخ الجارية.
- المعالج في معاملة الاعتماد، ومهمة مصالحة.
- شرط الارتباط في الصندوق، وتعريف واحد للمنشئ في البوابة.
- ترحيل `ApprovedCV` وحذف الشيفرة الميتة.
- السيناريوهات: AC-WF-Ea (تقديمان متزامنان ← نسخة واحدة)، AC-WF-Eb (اعتمادان متزامنان ← نجاح واحد)، AC-WF-Ec (فشل المعالج ← لا نسخة مكتملة دون أثر)، AC-WF-Ed (`CreatedBy` فارغ ← الخادم والصندوق متطابقان).

**WF-F (الموجة 2؛ التفويض في PLAN-50) — التفويض والمهل والتصعيد والإشعار.**
- تفويض يقرؤه المقيِّم والصندوق، مع «بالنيابة عن» في التوقيع.
- مهمة مهل تصعّد إلى `EscalationRole`.
- إشعار المعتمد التالي.
- `DueDate` وأولوية في الصندوق.
- السيناريوهات: AC-WF-Fa (المفوَّض يعتمد بالنيابة)، AC-WF-Fb (بعد الانتهاء ← رفض)، AC-WF-Fc (تجاوز المهلة ← تصعيد وإشعار)، AC-WF-Fd (المفوَّض منشئ الطلب ← رفض).

**WF-G (الموجة 2) — صندوق العمل الموحد عبر الخدمات.**
- `WorkInboxItem` مُخزَّن، وأحداث «مطلوب/اكتمل/أُلغي» من كل خدمة.
- صندوق الفريق، وترقيم الصفحات، وعدّاد متصالح.
- السيناريوهات: AC-WF-Ga (إجازة ونقل وحجز صرف كل منها لمعتمده)، AC-WF-Gb (البت يُخفي العنصر)، AC-WF-Gc (تعطّل المصدر لا يُخفيه)، AC-WF-Gd (العدّاد = عدد العناصر).

**WF-H (الموجة 2) — إدارة التعريفات والقرار الإداري.**
- شاشة إدارة، ومفتاح `(TenantId, Code, Version)`، وترقية البذور.
- عمليات `ReturnForCorrection` و`RequestInfo` و`Withdraw` و`Cancel`.
- دورة حياة القرار الإداري برقم غير قابل للتعديل.
- السيناريوهات: AC-WF-Ha (جهة بإصدار خاص)، AC-WF-Hb (إعادة للتصحيح على النسخة نفسها)، AC-WF-Hc (حذف قرار صادر ← رفض).

**WF-I (PLAN-65) — التحقق والتوثيق.**
- تصحيح `WF_001_WORKFLOW_ENGINE.md` و`WF_002_WORK_INBOX.md` و`ADR-WF-001` و`SOD_MATRIX.md`، وبطاقات WF-001 وWF-002 وIAM-003 وPROD-005، و`ADR-FUN-004`.
- AC-33 يختبر الاعتماد فعلاً.
- AC-11 بلا وضع من العميل.
- إزالة حجب AC-12.
- استبدال `Wf002WorkInboxTests` باختبارات سلوك.
- مجلدات `acceptance-evidence/AC-WF-*` على الملف الشبيه بالإنتاج، مع `ACCEPTANCE_REQUIRE_ALL=true`.
- السيناريوهات: AC-WF-Ia (كل AC-WF-* بأدلة كاملة)، AC-WF-Ib (مراجعة تطابق الوثائق مع الشيفرة، موقّعة).

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين: نتيجة التحقق الحي من G-WF-03، وقرار D-WF-7، وحجم ربط التأديب الذي تقرره D-ER-2.

---

## 7. معيار «اكتمال محرك الاعتمادات» المقترح

يُعدّ المحرك والاعتمادات مكتملة لإصدار v1 عندما تتحقق البنود الخمسة الآتية:

- [ ] **1. لا قرار يُطبَّق عند التقديم.** على الملف الشبيه بالإنتاج، كل قرار ينهي الخدمة أو يمس الأجر أو يمنح صلاحية أو يحذف بيانات شخصية (D-WF-1) لا يُحدث أثراً قبل اعتماد مسجَّل على الخادم. سيناريو AC-WF-C* أو AC-WF-D* لكل نوع ناجح بأدلة.
- [ ] **2. فصل المهام على الخادم في كل قناة.** لا معامل معتمد يأتي من العميل (اختبار معماري). دور الخطوة وقاعدة «لا فاعل في خطوتين» مفروضان لكل أنواع المحرك، والقائمة المستثناة فارغة في الإنتاج. الاعتماد المزدوج فوق العتبات مثبت حيّاً.
- [ ] **3. كل اعتماد يصل إلى أثره.** اختبار تكامل لكل نتيجة عابرة: اعتماد ← Outbox ← مستهلك يتحقق من `InstanceId`. لا `Publish` بعد آخر `SaveChanges` (اختبار معماري). لا كيان `PendingApproval` بلا نسخة، ولا نسخة منتهية وكيانها معلّق (مهمة مصالحة تعيد صفراً).
- [ ] **4. التعريفات محكومة.** لا يغيّرها إلا `workflow-admin` بعينين. والتحقق يرفض أي تعريف أضعف من حدود نوعه.
- [ ] **5. الوثائق تطابق الشيفرة.** البطاقات WF-001 وWF-002 وIAM-003 وPROD-005 و`SOD_MATRIX.md` مصحَّحة. وكل سيناريو AC-WF-* له مجلد أدلة كامل، ولا اختبار قبول ينجح دون فحص.
