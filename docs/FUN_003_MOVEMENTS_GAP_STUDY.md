# دراسة حركات الموظفين: النقل والانتداب والإعارة والترفيع وتنفيذها (MOV) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** خدمة `HRMovements` كاملة: طلبات النقل (`TransferRequests`) والانتداب (`SecondmentRequests`) والإعارة (`LoanAssignmentRequests`) والترفيع (`PromotionRequests`) والندب (`DelegationRequests`)، وسجلات التنفيذ (`MovementExecutions`)، والأثر المالي (`TransferFinancialImpacts`)، ونشرات التنقلات (`MovementNotices`)، وخطط التوزيع، والتدخل البشري (`ProcessInterventions`)، وsagas النقل والانتداب. ومعها آثار التنفيذ في HR.Core (الوحدة، المنصب الممول، السجل الوظيفي، نظام الخدمة) وPayroll (ملف الأجر، البدلات، الدرجة) وHR.Contracts (القرار الإداري، معاملة الخدمة) وAuth.Directory وAdmin.Logistics، والوثائق والخدمة الذاتية والتقارير.  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والوثائق في `main` @ `f9eab1a`، ومقارنتها بما تدّعيه وثائق FUN-003 وSYR-003 وبطاقات الإكمال.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات ولا الحاويات (لا يوجد .NET SDK). كل فجوة مدعومة بمرجع ملف/سطر تحقّقنا منه. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [ADR-FUN-003](ADR-FUN-003-movements-journey-closure.md) · [FUN_003_JOURNEY_MATRIX](FUN_003_JOURNEY_MATRIX.md) · [FUN_003_EXCEPTION_PATHS](FUN_003_EXCEPTION_PATHS.md) · [FUN_003_RECONCILIATION_CHECKLIST](FUN_003_RECONCILIATION_CHECKLIST.md) · [FUN_003_UAT_PACK](FUN_003_UAT_PACK.md) · [FUN_003_VERIFICATION](FUN_003_VERIFICATION.md) · [fun-movements/journey-matrix.json](fun-movements/journey-matrix.json) · [PROCESS_SAGAS](PROCESS_SAGAS.md) · [SYR_003_SERVICE_REGIMES](SYR_003_SERVICE_REGIMES.md) (ربط الحركات PLAN-SYR-08/09) · [completion-backlog/04-P3 (FUN-003)](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) · [05-TRACEABILITY](completion-backlog/05-TRACEABILITY.md) · [09 (EST-001)](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md) · [13 (SYR-003)](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md) · [syr/legal-source-binding](syr/legal-source-binding.md) · [syr/service-regimes.json](syr/service-regimes.json) · [PLAN-01 (AC-06)](acceptance-plans/PLAN-01-center-branch-isolation.md) · [acceptance-evidence/summary.md](../acceptance-evidence/summary.md)

---

## 1. الخلاصة

1. **الهيكل موجود، والأثر الفعلي محصور في النقل.** توجد كيانات وشاشات لخمسة أنواع حركة، وقائمة حوكمة، وsaga للنقل وأخرى للانتداب، وحدث لكل نوع، وطابور تدخل بشري. لكن النقل وحده يغيّر شيئاً حقيقياً في الخدمات الأخرى (الوحدة الرئيسية وملف الأجر). الانتداب والإعارة والترفيع والندب تنتهي إلى **سطر نصي في `EmploymentRecord.Notes`**. عبارة «Done» في `04-P3` و`FUN_003_VERIFICATION` و«ReadyForSign-off» في حزمة UAT لا تطابق الواقع.
2. **خمسة عوائق P0:**
   - **(أ) G-MOV-01:** دور `hr-management-approver` لا يدخل وحدة الحركات، ودور `hr-movements` لا يعتمد. لا يعتمد حركةً إلا `admin`.
   - **(ب) G-MOV-02:** ملف `service-regimes.json` لا يُشحن في صورة HR.Movements، فيفشل إنشاء النقل واعتماده وتنفيذه، وإنشاء الترفيع والندب، في كل نشر بالحاويات (يحتاج تحقق تشغيلي).
   - **(ج) G-MOV-03:** بوابة التعارض تعدّ أي نقل **منفّذ سابقاً** تعارضاً دائماً. بعد أول نقل لموظف لا يمكن نقله ولا انتدابه ولا إعارته مرة أخرى.
   - **(د) G-MOV-04:** Payroll يطبّق فرق الراتب للنقل **مرتين** (طرف الـ saga ثم الحدث النهائي)، وفي كل مرة كزيادة دائمة في الأساس وكدفعة لمرة واحدة معاً.
   - **(هـ) G-MOV-05:** حدث الترفيع (والندب) يُنشر دون حفظ الـ outbox فلا يُرسل، ولو وصل فلا مستهلك في Payroll، وHR.Core يكتب ملاحظة فقط. الترفيع بلا أي أثر.
3. **ثغرات سلامة وحوكمة (P1):** النقل لا يمس الملاك ولا يحفظ بيانات الوظيفة؛ تاريخ النفاذ غير قابل للإدخال ولا يصل إلى الأطراف؛ الـ saga تنسّق إقرارات فارغة والتعويض شكلي؛ إنهاء الانتداب والإعارة لا يعيد شيئاً؛ معظم الشاشات بلا نطاق فرع؛ فصل المهام يُتجاوز بحقل `RequestedBy` وبالتعديل؛ الأثر المالي يكتبه المنفّذ دون اعتماد؛ التنقل الداخلي من التوظيف لا يصل إلى نقل؛ لا رموز تزامن خارج النقل.
4. **قدرات ناقصة (P2):** لا ربط قانوني لمدد الانتداب والإعارة وشروط الترفيع؛ لا قرار إداري ولا وثيقة PDF ولا معاملة خدمة؛ لا خدمة ذاتية ولا إشعارات؛ تقارير شكلية؛ نموذج الحركات المؤقتة لا يمثّل الجهات الخارجية ولا المعالجة المالية للإعارة.
5. **فجوة تحقق:** AC-06 (أثر النقل على الوصول) لم يُشغَّل، واختباره لا ينفّذ نقلاً أصلاً. اختبار E2E «منع الاعتماد الذاتي» يقارن معرّفَي شخصيتين فقط. اختبارات الـ saga تستخدم طرف Payroll وهمياً، فلا ترى التطبيق المزدوج. ولا اختبار يمر بحاوية HR.Movements.

**التقدير الإجمالي:** نحو 58–75 يوم عمل لمطوّر واحد، منها 19–25 يوماً للعوائق P0 (الخطط MOV-A وMOV-B وMOV-C).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | تسجيل طلب نقل | `TransferRequestsController.Create`: نطاق AND على المصدر والهدف، `RequestedBy` من التوكن، لقطة نظام الخدمة، checklist من 7 خطوات | ❌ يفشل في الحاويات (G-MOV-02)؛ لا حقل تاريخ نفاذ (G-MOV-07)؛ لا تحقق أن الموظف في وحدة المصدر (G-MOV-10) |
| 2 | اعتماد محلي | `Approve` بسياسة `HrManagementApprove` + منع المنشئ + SoD | ❌ المعتمد لا يصل إلى الوحدة (G-MOV-01)؛ التعديل ثم الاعتماد يتجاوز فصل المهام (G-MOV-11) |
| 3 | تنفيذ النقل | `Execute`: بوابة تاريخ + checklist + تعارض ← `Executed` فوراً ← `IStartTransferSaga` | ❌ التعارض يمنع كل حركة لاحقة (G-MOV-03)؛ الحالة تسبق الأطراف (G-MOV-08) |
| 4 | أطراف saga النقل | Payroll يطبّق؛ HR.Core وAuth.Directory وAdmin.Logistics إقرار فارغ | ⚠️ (G-MOV-08) |
| 5 | الأثر في HR.Core | `ITransferExecuted`: وحدة رئيسية + إغلاق السجل وفتح سجل جديد + انتقال نظام الخدمة والمادة 159 | ⚠️ لا منصب ممول، والسجل الجديد يفقد بيانات الوظيفة (G-MOV-06) |
| 6 | الأثر في Payroll | `HandleTransferExecutedCommand` مرتين؛ قيم يكتبها المنفّذ | ❌ (G-MOV-04، G-MOV-12) |
| 7 | المزامنة في الخدمات الأخرى | لقطات الوحدة في Contracts وTraining وAdmin.Logistics وAuth.Directory | ✅ (مع ملاحظة الإصدار في G-MOV-08) |
| 8 | إلغاء/تعويض النقل | soft-cancel قبل التنفيذ؛ مسار ما بعد التنفيذ ميت؛ التعويض إقرارات | ❌ (G-MOV-08) |
| 9 | الانتداب | Create/Approve/Activate + saga (Payroll، Directory) + Close | ⚠️ بلا نطاق (G-MOV-10)؛ الإنهاء لا يعيد شيئاً (G-MOV-09) |
| 10 | الإعارة | حدث مباشر `ILoanAssignmentExecuted` / `ILoanAssignmentEnded` | ❌ لا مستهلك للإنهاء، ولا أثر مالي (G-MOV-09، G-MOV-19) |
| 11 | الترفيع | Create/Approve/Execute + `IPromotionExecuted` | ❌ الحدث لا يُحفظ، ولا أثر على الدرجة أو الراتب (G-MOV-05) |
| 12 | الندب | مثل الترفيع + Close | ❌ (G-MOV-05، G-MOV-09) |
| 13 | التنقل الداخلي من التوظيف | `IInternalPlacementCompleted` ← مسودة نقل | ❌ لا مسودة عملياً، وإن وُجدت لا تُنفَّذ (G-MOV-13) |
| 14 | القرار الإداري والوثائق | مرجع نصي حر `TransitionDecisionRef` | ❌ (G-MOV-16) |
| 15 | النشرات والتقارير | نشرة للنقل فقط + أربعة عدّادات | ⚠️ (G-MOV-18) |
| 16 | الخدمة الذاتية | لا شيء | ❌ (G-MOV-17) |
| 17 | التدخل البشري | API `api/process-interventions` بلا شاشة | ⚠️ (G-MOV-08) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **نطاق النقل AND:** التفاصيل والاعتماد والرفض والتنفيذ والإلغاء والقائمة تفحص المصدر والهدف معاً ([TransferRequestsController.cs:36-45, 62-71](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs))، ومثلها الإعارة ([MovementsExtendedControllers.cs:603-632](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
- **مقدّم طلب النقل من التوكن** لا من النموذج، مع تعليق يشرح السبب ([TransferRequestsController.cs:88-91](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs))، ومنع المنشئ من الاعتماد، واختبار `SoDForgedApproverTests` للنقل.
- **تنفيذ النقل ذري ومحمي من الضغط المزدوج:** انتقال مشروط `Approved → Executed` بـ `ExecuteUpdateAsync` ([TransferRequestsController.cs:350-360](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs))، و`xmin` على `TransferRequest`.
- **Outbox:** `AddPostgresOutbox` مع `UseBusOutbox()` ([Program.cs:64](../src/Services/HRMovements/HR.Movements.Web/Program.cs)، [MassTransitExtensions.cs:112-121](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs))، ومتحكمات النقل والانتداب والإعارة تحفظ بعد النشر. ومرشح Inbox في المستهلكات.
- **ربط نظام الخدمة (SYR-008):** لقطة النظام عند الطلب، ومرجع قرار إلزامي للنقل بين نظامين، وتطبيق الانتقال وطابور المادة 159 في HR.Core ([TransferExecutedConsumer.cs:53-117](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs)).
- **مستهلك HR.Core للنقل** idempotent بالحالة، يغلق السجلات المفتوحة ويفتح سجلاً جديداً ([TransferExecutedConsumer.cs:31-48](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs))، وتنتقل الوحدة إلى لقطات Contracts وTraining وAdmin.Logistics وAuth.Directory وملف الأجر.
- **لا توأم لـ G-02 في التوظيف:** الاعتماد المحلي غير مشروط بـ `IsLabScale` ولا بوضع محرك السياسات (لا استخدام لهما في `HRMovements`).
- **Saga بمهلات وطابور تدخل وتعويض بترتيب عكسي**، محفوظة في EF بتزامن متشائم ([TransferSecondmentStateMachines.cs:52-193](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs)).
- **الإلغاء الناعم** يحفظ التاريخ، وفهرس فريد على `(MovementKind, CorrelationId, Step)` في checklist.

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-MOV-01 — المعتمد لا يصل إلى وحدة الحركات، والمنشئ لا يعتمد
- **الدليل:**
  - كل إجراءات MVC في الخدمة خلف مرشح الوحدة `ModuleHrMovements` ([Program.cs:17-18](../src/Services/HRMovements/HR.Movements.Web/Program.cs))، والمرشح لا يستثني أي متحكم حركات ([HrModuleAuthorizationFilter.cs:27-46](../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs)).
  - السياسة = `admin` أو `hr-movements` فقط ([HrManagementAuthorizationExtensions.cs:39, 194-200](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، والبوابة تفرضها على مسار `hr-movements` ([GatewayPathAuthorizationMiddleware.cs:22](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
  - الاعتماد يتطلب `HrManagementApprove` = `admin` أو `hr-management-approver` ([HrManagementAuthorizationExtensions.cs:149-151](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، على `Approve`/`Reject` في النقل ([TransferRequestsController.cs:185, 253](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)) و`Approve` في الأنواع الأربعة ([MovementsExtendedControllers.cs:335, 681, 951, 1115](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - لا شخصية تحمل الدورين: `a-management-approver` = `hr-management-approver` فقط، و`a-movements` = `hr-movements` فقط ([personas.json:69-74, 166-170](../testdata/acceptance/personas.json))؛ وفي realm التطوير `e2e-checker-mgmt` = `hr-management-approver` + `hr-core` ([realm-hrmanagement-dev.template.json:153-169](../docker/keycloak/realm-hrmanagement-dev.template.json)).
- **الأثر:** في جهة حقيقية لا يعتمد أي حركة إلا `admin`. فإما تتوقف الحركات، أو يُمنح المعتمدون صلاحية المدير العام فيسقط فصل المهام. هذا توأم G-05 في دراسة التوظيف.
- **الإصلاح المقترح:** سياسة صريحة لكل إجراء بدل مرشح الوحدة العام (القراءة والإنشاء والتنفيذ: `hr-movements`؛ التفاصيل والاعتماد والرفض: `hr-management-approver`)، واستثناء متحكمات الحركات من المرشح كما فعل PLAN-28 لـ `RecruitmentAppeals`، مع قاعدة بوابة مطابقة. القرار D-MOV-2.
- **الخطة:** [PLAN-50](acceptance-plans/PLAN-50-iam-approver-reach-delegation.md) (وصول المعتمد، ودور المنشئ لا يعتمد، والمنشئ لا يعتمد طلبه؛ يغطيها AC-IAM-50a)
- **التحقق المطلوب:** AC-MOV-Aa: `a-movements` ينشئ نقلاً، و`a-management-approver` يفتح التفاصيل ويعتمد عبر البوابة (Approved). سالب: `a-management-approver` لا ينشئ ولا ينفّذ (403)؛ `a-movements` لا يعتمد (403).

#### G-MOV-02 — حزمة أنظمة الخدمة لا تُشحن مع HR.Movements
- **الدليل:**
  - `ServiceRegimeMovementRules` يقرأ `movement_types` من `seed-packs/syr003-service-regimes-v1/regimes.json` بجوار التطبيق، ثم يصعد المجلدات بحثاً عن `docs/syr/service-regimes.json` ([ServiceRegimeMovementRules.cs:92-136](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeMovementRules.cs)). وقواعد الترفيع تبحث في المسارات نفسها ([ServiceRegimePromotionRules.cs:80](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimePromotionRules.cs)).
  - الملف يُنسخ إلى مخرجات HR.Core فقط ([HR.Core.Infrastructure.csproj:16](../src/Services/HRCore/HR.Core.Infrastructure/HR.Core.Infrastructure.csproj)). مشاريع HR.Movements الثلاثة لا تنسخه ([HR.Movements.Web.csproj](../src/Services/HRMovements/HR.Movements.Web/HR.Movements.Web.csproj)).
  - الصورة لا تحوي إلا مخرجات النشر ([DotnetService.Dockerfile:51](../docker/DotnetService.Dockerfile))، فلا `docs/` ولا `seed-packs/` تحت `/app` ولا فوقه.
  - مع غياب الملف يكون القاموس فارغاً، فيعيد `ValidateRegimeChangeFields` «نوع الحركة غير معروف في سجل أنظمة الخدمة» ([ServiceRegimeMovementRules.cs:64-67](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeMovementRules.cs)). يُستدعى هذا الفحص في إنشاء النقل وتعديله واعتماده وتنفيذه ([TransferRequestsController.cs:94-99, 172-177, 222-227, 339-344](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)) وفي إنشاء الندب ([MovementsExtendedControllers.cs:1075-1077](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)). والترفيع يعيد «لا توجد قواعد ترفيع مهيأة» ([ServiceRegimePromotionRules.cs:70](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimePromotionRules.cs)) عند الإنشاء والاعتماد ([MovementsExtendedControllers.cs:911-913, 975-980](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - الاختبارات تنجح لأن مجلد `bin/` يقع داخل المستودع، فيصل البحث إلى `docs/syr/service-regimes.json`.
- **الأثر:** في Staging وProduction، وفي مكدّس القبول المبني من الصورة نفسها، لا يمكن إنشاء نقل ولا اعتماده ولا تنفيذه (حتى النقول المزروعة)، ولا إنشاء ترفيع أو ندب. الانتداب والإعارة وحدهما يعملان لأنهما لا يفحصان النظام. **يحتاج تحقق** تشغيلي على الحاوية؛ الاستنتاج من الشيفرة مباشر.
- **الإصلاح المقترح:** تضمين الحزمة مورداً في `SharedKernel` (EmbeddedResource) أو Content في كل خدمة تستعملها، وفحص إقلاع يفشل صراحةً إن كانت القواعد فارغة، واختبار على الصورة المبنية.
- **الخطة:** [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md)
- **التحقق المطلوب:** AC-MOV-Ab: على الصورة المبنية ينجح إنشاء نقل واعتماده وتنفيذه، ويُبلغ فحص الصحة عن 4 أنواع حركة و4 قواعد ترفيع. سالب: حذف الملف ← فشل الإقلاع برسالة واضحة، لا رفض صامت لكل طلب.

#### G-MOV-03 — بوابة التعارض تمنع أي حركة بعد أول نقل منفّذ
- **الدليل:**
  - `HasConcurrentMovementConflict` يعدّ أي نقل آخر بحالة `Approved` **أو `Executed`** تعارضاً، بلا تواريخ ولا فترة: `t.Status is ApprovalStatus.Approved or ApprovalStatus.Executed` ([MovementWorkflowRules.cs:64-76](../src/Services/HRMovements/HR.Movements.Domain/Services/MovementWorkflowRules.cs)).
  - المتحكمات تمرر كل نقول الموظف غير الملغاة منذ البداية ([TransferRequestsController.cs:319-330](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)؛ [MovementsExtendedControllers.cs:409-419, 757-767](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - النقل المنفّذ لا يُلغى: `CanCancelTransfer` يستثني `Executed`، و`CanCancelAfterExecutePreEffective` يتطلب `EffectiveDate > today` ([MovementWorkflowRules.cs:15-21](../src/Services/HRMovements/HR.Movements.Domain/Services/MovementWorkflowRules.cs))، بينما التنفيذ يتطلب `EffectiveDate <= today` ([TransferWorkflowRules.cs:14-17](../src/Services/HRMovements/HR.Movements.Domain/Services/TransferWorkflowRules.cs)).
  - نقلان معتمدان للموظف نفسه يمنع كل منهما الآخر.
  - الفحص عند التنفيذ والتفعيل فقط؛ لا يشمل الترفيع والندب؛ ولا يفحص حالة الموظف (منتهية خدمته، في إجازة، موقوف) رغم [FUN_003_EXCEPTION_PATHS.md:7](FUN_003_EXCEPTION_PATHS.md).
  - الاختبار الوحيد حالة إيجابية ([Fun003MovementsJourneyTests.cs:100-123](../src/Tests/HRManagement.UnitTests/Fun003MovementsJourneyTests.cs)).
- **الأثر:** كل موظف نُقل مرة لا يمكن نقله ثانيةً ولا انتدابه ولا إعارته، إلى الأبد ودون مخرج من الواجهة. الجهات الحكومية تنقل نسبة من موظفيها كل سنة، فيتوقف المسار تدريجياً.
- **الإصلاح المقترح:** تعريف «حركة مفتوحة» (معتمدة لم تُنفَّذ، أو مؤقتة نشطة ضمن فترتها)، والتعارض = تداخل فترات بين حركات مفتوحة؛ الفحص عند الاعتماد والتنفيذ؛ شمول الندب والترفيع المعلّق؛ فحص حالة الموظف من HR.Core.
- **التحقق المطلوب:** AC-MOV-Ac: نقلان منفّذان متتاليان للموظف نفسه، ثم انتداب بعدهما. سالب: انتداب نشط يمنع نقلاً؛ نقل ثانٍ معتمد يتداخل مع الأول يُرفض عند الاعتماد برسالة؛ موظف منتهية خدمته يُرفض.

#### G-MOV-04 — Payroll يطبّق الأثر المالي للنقل مرتين، وكدفعة وأساس معاً
- **الدليل:**
  - طرف Payroll في الـ saga يستدعي `HandleTransferExecutedCommand` ([PayrollEventConsumers.cs:109-123](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs))، ثم مستهلك `ITransferExecuted` النهائي يستدعيه ثانيةً ([PayrollEventConsumers.cs:216-225](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)).
  - المعالج بلا فحص تكرار: يضيف `PayrollAdjustment` بقيمة الراتب + البدل ([InboundEventHandlers.cs:291-299](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs))، **و**يزيد `BaseSalary` ([:313](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). العلامة `marker` ([:286](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) نص لا يُفحص، بخلاف معالج التوصية الطبية الذي يفحص علامته ([:691-697](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - مرشح Inbox يمنع تكرار الرسالة نفسها لكل مستهلك ([IdempotentConsumerFilter.cs:66](../src/Shared/HRManagement.ServiceDefaults/IdempotentConsumerFilter.cs))، لا رسالتين مختلفتين.
  - التسوية بلا `PayrollRunId` تدخل أول مسير قادم ([PayrollPartitionInputLoader.cs:139-144](../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs)).
  - في `AwaitingIntervention` يُتجاهل إقرار متأخر ([TransferSecondmentStateMachines.cs:168](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs))، ثم `Resume` يعيد الأمر للأطراف المعلّقة ([:153-161](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs))، فيُطبَّق مرة ثالثة.
  - النمط نفسه في الانتداب: طرف ([PayrollEventConsumers.cs:184-188](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)) + حدث نهائي ([:286-294](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)) ← بدل الانتداب مرتين ([InboundEventHandlers.cs:505-537](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). النسبة الافتراضية 0 ([LegalDecisionRegisterSeed.cs:23](../src/Services/Payroll/Payroll.Domain/Services/LegalDecisionRegisterSeed.cs))، فيظهر الخطأ حين تُضبط.
- **الأثر:** نقل بفرق راتب 50,000 ل.س. يرفع الأساس 100,000 دائماً، ويضيف في أول مسير دفعة لمرة واحدة 100,000 وضعف فرق البدل. خطأ مالي صامت في كل نقل بفرق مالي.
- **الإصلاح المقترح:** نقطة تطبيق واحدة (الطرف أو الحدث النهائي، لا كلاهما)؛ مفتاح idempotency بـ `TransferRequestId` مع قيد فريد؛ فصل تغيير الأساس (نسخة ملف أجر بتاريخ نفاذ) عن الفروق لمرة واحدة؛ اختبار تكامل على PostgreSQL بأطراف حقيقية ورسائل مكررة.
- **التحقق المطلوب:** AC-MOV-Ba: نقل بفرق X ← `BaseSalary` يزيد X مرة واحدة بنسخة ملف أجر مؤرخة، ولا دفعة إلا ما يقرره D-MOV-5. سالب: إعادة `ITransferExecuted`، و`Resume` بعد مهلة، وإقرار متأخر ← لا تغيير إضافي.

#### G-MOV-05 — الترفيع والندب بلا أثر
- **الدليل:**
  - `PromotionRequestsController.Execute` يحفظ الحالة أولاً ([MovementsExtendedControllers.cs:1032](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)) ثم ينشر `IPromotionExecuted` ([:1034-1047](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)) ولا يحفظ بعدها. مع `UseBusOutbox()` تُكتب الرسالة عند `SaveChangesAsync` فقط، فلا تُرسل. وكذلك `DelegationRequestsController.Execute` ([:1189-1202](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)). قارن النقل الذي يحفظ بعد النشر ([TransferRequestsController.cs:389-407](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)). **يحتاج تحقق** تشغيلي.
  - لو وصل الحدث: HR.Core يضيف سطراً في `Notes` فقط ([PromotionExecutedConsumer.cs:26-43](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/PromotionExecutedConsumer.cs))، ولا يوجد للموظف حقل درجة. Payroll لا يستهلك `IPromotionExecuted` (المستهلك الوحيد في HR.Core)، فلا يتغير `PayGradeId` ولا الأساس. والندب مثله ([DelegationExecutedConsumer.cs:22-35](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/DelegationExecutedConsumer.cs)).
  - الدرجة والمرتبة الحاليتان يكتبهما المستخدم ([Views/PromotionRequests/Create.cshtml:9-12](../src/Services/HRMovements/HR.Movements.Web/Views/PromotionRequests/Create.cshtml)) ولا تُقارنان بالفعلي؛ وهما `int` بينما سلّم الرواتب يستعمل `GradeCode` نصياً.
  - `MinTenureMonths` و`RequiresVacancy` تُحمَّلان ([ServiceRegimePromotionRules.cs:15-16](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimePromotionRules.cs)) ولا يستعملهما `ValidatePromotion` ([:57-76](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimePromotionRules.cs))، الذي يفحص مرجع القرار وأن الهدف أعلى فقط. لا ربط بالتقييم السنوي ولا بشاغر.
  - لا `Reject` ولا `Cancel` في متحكمَي الترفيع والندب، والحالة `PromotionRequestStatus.Cancelled` غير مستعملة.
- **الأثر:** الترفيع يظهر «منفّذ» دون أن تتغير درجة الموظف أو راتبه في أي نظام. يمكن «ترفيع» موظف بلا مدة خدمة ولا تقييم ولا شاغر، ومن درجة ليست درجته.
- **الإصلاح المقترح:** الحفظ بعد النشر واختبار معماري يمنع `Publish` بلا `SaveChanges` لاحق؛ الدرجة الحالية من المصدر (HR.Core/Payroll)؛ مستهلك Payroll يغيّر الدرجة والأساس بتاريخ نفاذ مع فروق رجعية مرة واحدة؛ قواعد الأهلية من الحزمة القانونية (D-MOV-3)؛ رفض وإلغاء.
- **الخطة:** [PLAN-40](acceptance-plans/PLAN-40-msg-atomic-event-publishing.md) (جزئياً: نشر `IPromotionExecuted`/`IDelegationExecuted` قبل الحفظ، والحارس المعماري، وإعادة نشر ما فُقد؛ والدرجة ومستهلك Payroll والأهلية والرفض والإلغاء «الموجة 2»)
- **التحقق المطلوب:** AC-MOV-Ca: ترفيع بتاريخ نفاذ ماضٍ ← درجة في HR.Core، و`PayGradeId` والأساس في Payroll، وفروق رجعية مرة واحدة. سالب: مدة غير مستوفاة أو تقييم أدنى أو لا شاغر ← رفض؛ درجة «من» لا تطابق الفعلية ← رفض.

### 4.2 ثغرات سلامة القرار والحوكمة (P1)

#### G-MOV-06 — النقل لا يمس الملاك، والسجل الوظيفي الجديد يفقد بياناته
- **الدليل:**
  - `TransferRequest` لا يحمل منصباً ممولاً لا في المصدر ولا في الهدف ([TransferRequest.cs:7-71](../src/Services/HRMovements/HR.Movements.Domain/Entities/TransferRequest.cs)).
  - مستهلك HR.Core يغيّر `PrimaryOrgUnitId` فقط ([TransferExecutedConsumer.cs:50](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs))، ولا يستدعي `VacatePosition` المكتوب لهذا الغرض ([EstablishmentService.cs:33-44](../src/Services/HRCore/HR.Core.Domain/Services/EstablishmentService.cs)). طرف HR.Core في الـ saga إقرار فقط ([ProcessPartyAckConsumers.cs:111-126](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/ProcessPartyAckConsumers.cs)).
  - السجل الجديد: `PositionTitle = "نقل وظيفي"`، و`EmploymentType = Permanent` ثابت، بلا `PositionCode` ولا `JobClassificationCode` ([TransferExecutedConsumer.cs:80-89](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs)).
  - يناقض معيار EST-001 «لا تعيين/نقل لمركز غير معتمد أو غير ممول» ([09-NATIONAL-FUNCTIONAL-CAPABILITIES.md:74](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md)).
- **الأثر:** يبقى منصب المصدر «مشغولاً» بموظف غادر، فلا يُفتح عليه طلب شاغر (التوظيف يشترط `Vacant`). الوحدة الهدف تضم موظفاً بلا منصب. تقارير المعتمد/المشغول/الشاغر لا تتصالح. الموظف المؤقت يصبح «دائماً» في سجله.
- **الإصلاح المقترح:** منصب هدف شاغر ممول (بقرار D-MOV-4)؛ إخلاء وإشغال ذريان مع `FundedPositionVersion` عند التنفيذ؛ السجل الجديد يرث المسمى والرمز والتصنيف والنوع من المنصب الهدف أو السجل السابق.
- **التحقق المطلوب:** AC-MOV-Da: بعد النقل منصب المصدر `Vacant` ومنصب الهدف `Occupied` بالموظف، ونسختان في `FundedPositionVersion`، والسجل الجديد بنوع التوظيف الأصلي. سالب: هدف مشغول أو مجمّد أو غير ممول ← رفض عند الاعتماد.

#### G-MOV-07 — تاريخ النفاذ لا يُدخل ولا يصل إلى الأطراف
- **الدليل:**
  - نموذجا إنشاء النقل وتعديله بلا حقل `EffectiveDate` ([Create.cshtml:8-37](../src/Services/HRMovements/HR.Movements.Web/Views/TransferRequests/Create.cshtml)، [Edit.cshtml:8-36](../src/Services/HRMovements/HR.Movements.Web/Views/TransferRequests/Edit.cshtml))، والخادم يجعله `RequestDate` ([TransferRequestsController.cs:86-87](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs))، و`RequestDate` حقل حر.
  - `IStartTransferSaga` و`ITransferExecuted` يحملان `ExecutedAt` (لحظة الضغط) لا تاريخ النفاذ ([TransferRequestsController.cs:390-406](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)؛ [TransferSecondmentStateMachines.cs:270-292](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs)).
  - HR.Core يبدأ السجل من `ExecutedAt.Date` ([TransferExecutedConsumer.cs:41, 85](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs))، وPayroll يغيّر الأساس فوراً بلا `EffectiveFrom` ([InboundEventHandlers.cs:301-314](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). و`IPromotionExecuted.EffectiveDate` لا يقرؤه أحد.
  - «اليوم» يُحسب بـ UTC ([MovementWorkflowRules.cs:9-13](../src/Services/HRMovements/HR.Movements.Domain/Services/MovementWorkflowRules.cs))، وسوريا UTC+3.
  - لا مهمة خلفية تنفّذ الحركات المعتمدة في تاريخها (لا `HostedService` في [Program.cs](../src/Services/HRMovements/HR.Movements.Web/Program.cs)).
- **الأثر:** لا نقل مؤجل ولا رجعي. قرار نقل نافذ من أول الشهر ونُفّذ في يومه العشرين يُحتسب في السجل والرواتب من العشرين. والتنفيذ يتوقف على تذكّر مستخدم.
- **الإصلاح المقترح:** تاريخ نفاذ إلزامي في النموذج والأحداث؛ HR.Core وPayroll يطبّقان به (مع فروق رجعية عبر مسير التصحيح)؛ «اليوم» بتوقيت الجهة؛ مهمة يومية تنفّذ المعتمد البالغ تاريخه أو تنبّه.
- **التحقق المطلوب:** AC-MOV-Dc: نقل معتمد بتاريخ مستقبلي يُنفَّذ آلياً في تاريخه، والسجل وملف الأجر يبدآن به. AC-MOV-Dd: نقل بتاريخ ماضٍ داخل فترة مسير مرحّل ← فروق رجعية مرة واحدة. سالب: تنفيذ يدوي قبل التاريخ ← رفض.

#### G-MOV-08 — الـ saga تنسّق إقرارات لا آثاراً، والحالة تسبق الأطراف، والتعويض شكلي
- **الدليل:**
  - الطلب يصبح `Executed` قبل بدء الـ saga ([TransferRequestsController.cs:350-354](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs))، رغم أن مستهلك Movements موصوف «لا ينهي النقل إلا بعد الـ saga» ([SagaSupportConsumers.cs:29-55](../src/Services/HRMovements/HR.Movements.Infrastructure/Consumers/SagaSupportConsumers.cs))، و[PROCESS_SAGAS.md:24](PROCESS_SAGAS.md) يقول إن بدء الـ saga «لا يعني الاكتمال». الواجهة تعرض «تم تنفيذ النقل» ([Details.cshtml:108-111](../src/Services/HRMovements/HR.Movements.Web/Views/TransferRequests/Details.cshtml)) ولو علقت الـ saga.
  - ثلاثة من أربعة أطراف إلزامية إقرار فارغ: HR.Core ([ProcessPartyAckConsumers.cs:111-126](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/ProcessPartyAckConsumers.cs))، Auth.Directory ([PartyAckConsumers.cs:37](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs))، Admin.Logistics ([PartyAckConsumers.cs:56](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)). الأثر الفعلي يقع في مستهلكات `ITransferExecuted` **بعد** إعلان الاكتمال، وفشلها لا يفتح تدخلاً.
  - التعويض: Payroll لا يعوّض شيئاً ويقرّ (`if (!m.Compensate)` [PayrollEventConsumers.cs:119-134](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs))، والقاعدة العامة تقرّ فقط ([PartyAckConsumerBase.cs:151-156](../src/Shared/HRManagement.ServiceDefaults/PartyAckConsumerBase.cs)). الـ saga في `Completed` تتجاهل `Cancel` ([TransferSecondmentStateMachines.cs:191](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs)). الإلغاء عبر التدخل يترك الطلب `Executed`، لأن إرجاعه مشروط بتاريخ مستقبلي ([SagaSupportConsumers.cs:179-191](../src/Services/HRMovements/HR.Movements.Infrastructure/Consumers/SagaSupportConsumers.cs)).
  - فشل الانتقال بين نظامين في HR.Core يُسجَّل تحذيراً ويُكمل كنقل داخل النظام ([TransferExecutedConsumer.cs:66-73](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs))، بينما غيّر Payroll `RegimeCode` في خطوة الطرف قبل ذلك ([InboundEventHandlers.cs:305-306](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)؛ [TransferSecondmentStateMachines.cs:222-236](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs)).
  - التدخل: API بلا شاشة، بسياسة الوحدة فقط، بلا نطاق ولا سبب ولا فاعل، و`Resume` يغلق التدخل فوراً قبل معرفة النتيجة ([ProcessInterventionsController.cs:12-68](../src/Services/HRMovements/HR.Movements.Web/Controllers/ProcessInterventionsController.cs)).
  - **يحتاج تحقق:** `ITransferExecuted.SchemaVersion => 3` ([ITransferExecuted.cs:33](../src/Shared/HRManagement.MessageContracts/Events/ITransferExecuted.cs)) ومستهلك Admin.Logistics يقبل حتى 2 ويتجاهل غيره بتحذير ([TransferExecutedConsumer.cs:26](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/TransferExecutedConsumer.cs)). وعند القبول يُغلق العهد كلها كأنها أُعيدت ([:41-51](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/TransferExecutedConsumer.cs)) دون تسليم فعلي.
- **الأثر:** حالة «منفّذ» لا تعني أن الموظف انتقل. خطأ في HR.Core يترك الرواتب في الوحدة الجديدة والملف في القديمة، ولا أحد يعلم. ولا طريقة لعكس نقل خاطئ بعد التنفيذ إلا بنقل معاكس، وهو ممنوع (G-MOV-03).
- **الإصلاح المقترح:** الأطراف تنفّذ الأثر نفسه (HR.Core: الوحدة والمنصب والسجل؛ Directory: النطاق) وتُقرّ بعده؛ الحالة `Executing` حتى `ITransferExecuted`؛ تعويض حقيقي لكل طرف أو «حركة عكسية» موثقة بقرار؛ فشل الانتقال بين نظامين = عطل طرف يفتح تدخلاً؛ شاشة تدخل بنطاق وسبب وفاعل.
- **التحقق المطلوب:** AC-MOV-Ea: تعطيل HR.Core أثناء النقل ← الطلب `Executing` وتدخل مفتوح، ثم الاستئناف يكمل مرة واحدة. AC-MOV-Eb: انتقال بين نظامين مرفوض ← تدخل، وPayroll لا يغيّر النظام. سالب: لا `Executed` قبل إقرار كل الأطراف.

#### G-MOV-09 — إنهاء الانتداب والإعارة والندب لا يعيد شيئاً
- **الدليل:**
  - `ILoanAssignmentEnded` يُنشر ([MovementsExtendedControllers.cs:824-833, 864-873](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)) ولا مستهلك له في أي خدمة. و`ILoanAssignmentExecuted` يضيف ملاحظة في HR.Core فقط ([LoanAssignmentExecutedConsumer.cs:23-37](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/LoanAssignmentExecutedConsumer.cs)).
  - إغلاق الانتداب = `ICancelSecondmentSaga` ([MovementsExtendedControllers.cs:474-481](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)) ← إعادة إرسال الأطراف بـ `Compensate` ([TransferSecondmentStateMachines.cs:462-468](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs)) ← Payroll وDirectory يقرّان فقط. حالة الدليل تبقى `Secondment@<الجهة>` ([SecondmentExecutedConsumer.cs:39](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/SecondmentExecutedConsumer.cs)). والـ saga تنتهي بحالة `Cancelled` لانتداب انتهى طبيعياً.
  - `Close` مسموح من `Pending` و`Approved` ([MovementsExtendedControllers.cs:453-468](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))، والزر ظاهر لكل حالة غير مغلقة ([SecondmentRequests/Details.cshtml:32-35](../src/Services/HRMovements/HR.Movements.Web/Views/SecondmentRequests/Details.cshtml))، فيُنشر إلغاء لـ saga غير موجودة.
  - إغلاق الندب لا ينشر شيئاً ([MovementsExtendedControllers.cs:1205-1225](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - لا إنهاء آلي عند `EndDate`، ولا تذكير، ولا تمديد.
  - الوثائق تدّعي العكس: «Close يستعيد» ([FUN_003_EXCEPTION_PATHS.md:10-11](FUN_003_EXCEPTION_PATHS.md)؛ [FUN_003_UAT_PACK.md:8, 13](FUN_003_UAT_PACK.md)).
- **الأثر:** موظف عاد من انتداب أو إعارة يبقى «منتدَباً» في الدليل، وأي بدل أو وقف راتب لا يُعاد، والمدة تنتهي دون أن يعلم أحد ما لم يغلقها مستخدم.
- **الإصلاح المقترح:** حدث إنهاء صريح (`ISecondmentEnded`/`ILoanAssignmentEnded`/`IDelegationEnded`) بمستهلكات تعيد الأثر في Payroll وDirectory وHR.Core؛ الإنهاء من `Active` فقط؛ مهمة يومية تنهي أو تنبّه عند `EndDate`؛ تمديد بقرار ضمن الحد القانوني (G-MOV-15).
- **التحقق المطلوب:** AC-MOV-Ec: انتداب ← تفعيل ← إنهاء ← الدليل `Active` والبدل متوقف. AC-MOV-Ed: إنهاء إعارة يُستهلك في Payroll وHR.Core. AC-MOV-Ee: انقضاء `EndDate` ← إنهاء آلي أو تنبيه حسب D-MOV-3. سالب: إنهاء طلب `Pending` ← رفض.

#### G-MOV-10 — نطاق الفرع غائب عن معظم الشاشات، ومصدر النقل غير متحقق منه
- **الدليل:**
  - بلا أي فحص نطاق: الانتداب كله ([MovementsExtendedControllers.cs:252-540](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)) والكيان بلا وحدة تنظيمية أصلاً؛ الترفيع ([:895-1050](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))؛ الندب ([:1061-1225](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))؛ سجلات التنفيذ ([:542-563](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))؛ الآثار المالية ([:565-592](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))؛ النشرات ([:19-103](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))؛ التدخلات ([ProcessInterventionsController.cs:17-33](../src/Services/HRMovements/HR.Movements.Web/Controllers/ProcessInterventionsController.cs)).
  - `TransferRequestsController.Edit` (POST) لا يفحص نطاق الطلب الحالي، بل الوحدات الجديدة فقط ([TransferRequestsController.cs:147-161](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)).
  - لا تحقق أن الموظف ينتمي إلى وحدة المصدر ([TransferRequestsController.cs:80-101](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs))، وHR.Core يطبّق الوجهة دون مقارنة المصدر ([TransferExecutedConsumer.cs:50](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/TransferExecutedConsumer.cs)).
  - 28 من 34 إجراء POST في الخدمة بلا سياسة صريحة (مرشح الوحدة فقط)، منها `Execute` و`Activate` و`Close` و`Cancel` واعتماد خطة التوزيع.
  - الادعاء «IDOR مصدر/هدف مغلق» ([05-TRACEABILITY.md:17](completion-backlog/05-TRACEABILITY.md)) صحيح للنقل والإعارة فقط.
- **الأثر:** موظف حركات في فرع يرى رواتب ونقول الجهة كلها، ويعدّل أو يغلق أو يلغي انتداب أي موظف، ويبدأ نقل موظف من فرع آخر باسم فرعه فيسحبه إليه بعد الاعتماد.
- **الإصلاح المقترح:** نطاق على كل قائمة وتفصيل وإجراء (الانتداب والترفيع والندب عبر وحدة الموظف من HR.Core)؛ رفض إذا لم يكن الموظف في وحدة المصدر؛ سياسة صريحة لكل POST مع اختبار سقف على غرار `HrCorePostActionsHaveExplicitPolicyTests`.
- **التحقق المطلوب:** AC-MOV-Fa: مستخدم فرع1 لا يرى ولا يعدّل انتداب/ترفيع/أثر مالي لموظف فرع2 (403/مخفي). AC-MOV-Df: نقل بمصدر لا يطابق وحدة الموظف ← رفض. سالب: مستخدم مركزي يرى الكل.

#### G-MOV-11 — فصل المهام يُتجاوز، ولا رفض لأربعة أنواع
- **الدليل:**
  - `model.RequestedBy ??= ParseUserId(User)` يقبل قيمة من النموذج في الانتداب والإعارة والترفيع والندب ([MovementsExtendedControllers.cs:267, 647, 917, 1081](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)). أُصلح في النقل فقط. من يحمل الدورين يضع `RequestedBy` لغيره ثم يعتمد طلبه.
  - التعديل بيد مستخدم آخر لا يغيّر `RequestedBy`: معتمد يعدّل الموظف أو الوجهة ثم يعتمد ([TransferRequestsController.cs:147-181](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)؛ [MovementsExtendedControllers.cs:310-331](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - مسودات النقل من التوظيف بلا `RequestedBy` ([InternalPlacementCompletedConsumer.cs:40-49](../src/Services/HRMovements/HR.Movements.Infrastructure/Consumers/InternalPlacementCompletedConsumer.cs))، فيسقط فحص المنشئ ([TransferRequestsController.cs:205](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)).
  - لا `Reject` في الانتداب والإعارة والترفيع والندب. رفض النقل لا يحفظ السبب، فهو في `TempData` فقط ([TransferRequestsController.cs:273-279](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)).
  - لا زر إلغاء في أي شاشة (الإجراءات `Cancel` في الخادم فقط)، فسيناريوهات UAT U6 وU7 غير ممكنة لمستخدم.
  - كل الإنشاء والتعديل يربط كيانات EF مباشرة. اختبار `MassAssignmentArchitectureTests` لا يرى `HR.Movements.Web` لأن مشروع الاختبار لا يشير إليه ([MassAssignmentArchitectureTests.cs:40-58](../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs)؛ [HRManagement.UnitTests.csproj:40-41](../src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj)).
- **الأثر:** «العيون الأربع» قابلة للتجاوز في أربعة أنواع من خمسة. المعتمد لا يملك إلا الموافقة، فالطلب المرفوض يبقى `Pending` أو يُعتمد.
- **الإصلاح المقترح:** نماذج عرض بلا `RequestedBy`/`Status`؛ أي تعديل من غير المنشئ ممنوع أو يجعله منشئاً؛ `Reject` بسبب محفوظ لكل الأنواع؛ أزرار الإلغاء في الواجهة؛ إضافة `HR.Movements.Web` إلى مشروع الاختبار.
- **التحقق المطلوب:** AC-MOV-Fb: `RequestedBy` مزوّر في POST يُتجاهل. AC-MOV-Fc: معتمد عدّل الطلب لا يعتمده. AC-MOV-Fd: رفض كل نوع بسبب محفوظ ومرئي.

#### G-MOV-12 — الأثر المالي يكتبه المنفّذ بعد الاعتماد بلا ضوابط
- **الدليل:**
  - فرق الراتب والبدل يُدخلان في نموذج التنفيذ بعد الاعتماد ([Details.cshtml:89-104](../src/Services/HRMovements/HR.Movements.Web/Views/TransferRequests/Details.cshtml)؛ [TransferRequestsController.cs:286, 370-381](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs))، فالمعتمد لا يراهما.
  - لا حدود من سلّم الرواتب ولا من المنصب الهدف، بخلاف تفعيل العقد الذي يفحص حدود الدرجة ([InboundEventHandlers.cs:99-105](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - `TransferFinancialImpactsController.Create` يضيف أثراً مالياً لأي نقل بأي قيمة ([MovementsExtendedControllers.cs:578-591](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))، ويظهر في تفاصيل النقل ولا يصل إلى Payroll.
  - البدل يُعامل دفعة لمرة واحدة لا تغييراً شهرياً ([InboundEventHandlers.cs:289-299](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). ولا مركز تكلفة؛ `OrgUnitId` في ملف الأجر هو البديل الوحيد ([:304](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- **الأثر:** موظف الحركات يقرر وحده زيادة الراتب لحظة التنفيذ. سجل «الأثر المالي» قابل للتزوير ولا يطابق ما دُفع.
- **الإصلاح المقترح:** الأثر المالي يُحسب في Payroll من المنصب والدرجة الهدف (D-MOV-5)، ويُعرض على المعتمد قبل الاعتماد؛ الاستثناء اليدوي بمبرر وموافقة؛ حذف الإنشاء الحر للأثر المالي؛ البدل تغيير شهري بتاريخ.
- **التحقق المطلوب:** AC-MOV-Bc: الفرق يُحسب ويظهر في شاشة الاعتماد، والتنفيذ لا يقبل قيمة مختلفة. سالب: راتب خارج حدود الدرجة ← رفض أو حجز مراجعة؛ POST إلى `TransferFinancialImpacts/Create` ← 404/403.

#### G-MOV-13 — التنقل الداخلي من التوظيف لا يصل إلى نقل قابل للتنفيذ
- **الدليل:**
  - في الإغلاق الداخلي `orgUnitId = employee.PrimaryOrgUnitId ?? position?.OrgUnitId` ([RecruitmentHireClosureService.cs:270](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs))، أي الوحدة القديمة، ويُمرَّر كوجهة ([:321-331, 556-566](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs)). و`PrimaryOrgUnitId` لا يتغير إن كانت له قيمة ([:288-289](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs)).
  - مستهلك Movements يتجاهل الحدث حين المصدر = الهدف ([InternalPlacementCompletedConsumer.cs:27-28](../src/Services/HRMovements/HR.Movements.Infrastructure/Consumers/InternalPlacementCompletedConsumer.cs)).
  - وإن أُنشئت مسودة فهي بلا checklist ([:40-51](../src/Services/HRMovements/HR.Movements.Infrastructure/Consumers/InternalPlacementCompletedConsumer.cs))، والتنفيذ يرفض checklist فارغة (`if (gate.Count == 0) return false;` [MovementWorkflowRules.cs:51-60](../src/Services/HRMovements/HR.Movements.Domain/Services/MovementWorkflowRules.cs)).
  - **يحتاج تحقق:** اختبار التكامل يفحص المنصبين ولا يفحص الوحدات في الحدث ([InternalChannelIntegrationTests.cs:314-334](../src/Tests/HRManagement.IntegrationTests/InternalChannelIntegrationTests.cs)).
- **الأثر:** موظف يفوز بشاغر داخلي في وحدة أخرى يشغل منصبها في HR.Core، لكن وحدته الرئيسية ونطاق الوصول إليه وملف أجره ودليله تبقى في الوحدة القديمة.
- **الإصلاح المقترح:** الوجهة = وحدة المنصب الهدف؛ مسودة نقل كاملة (checklist، منشئ نظامي معلَن، مرجع طلب التوظيف) تمر بالاعتماد، أو تنفيذ مباشر بقرار موثق؛ وعدم تكرار إخلاء/إشغال المنصب بين المسارين.
- **التحقق المطلوب:** AC-MOV-De: فوز موظف بشاغر داخلي في وحدة أخرى ← نقل في Movements يُعتمد ويُنفَّذ ← الوحدة الرئيسية وملف الأجر في الوحدة الجديدة. سالب: لا إشغال مزدوج للمنصب.

#### G-MOV-14 — التزامن والتكرار وقائمة الحوكمة الشكلية
- **الدليل:**
  - رمز التزامن `xmin` على `TransferRequest` وحده ([MovementsDbContext.cs:68](../src/Services/HRMovements/HR.Movements.Infrastructure/Data/MovementsDbContext.cs)؛ [DATA001_ConcurrencyAndUniques.cs:14-20](../src/Services/HRMovements/HR.Movements.Infrastructure/Migrations/20260728142021_DATA001_ConcurrencyAndUniques.cs)).
  - تفعيل الانتداب وتنفيذ الإعارة والترفيع والندب تحديث متتبَّع بلا شرط حالة ذري ([MovementsExtendedControllers.cs:421-446, 769-795, 1021-1032, 1178-1189](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))؛ طلبان متزامنان ينتجان سجلَي تنفيذ وحدثين.
  - لا قيد يمنع طلبين مفتوحين من النوع نفسه للموظف نفسه، ولا فحص `EndDate >= StartDate` في الانتداب والإعارة والندب ([:264-282, 643-662, 1073-1097](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - خطوتا «مسجّل» و«معتمد» تُعلَّمان آلياً بدليل مصطنع مثل `transfer:{id}:recorded` ([TransferRequestsController.cs:108-111, 241-244](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs))، فبوابة «الخطوات الإلزامية بدليل» تحصيل حاصل. خطوات الأطراف (مالية، تنظيم، دليل، عهدة) لا يعلّمها أي سطر، و`SlaHours` غير مستعمل ([MovementChecklistDefaults.cs:8-28](../src/Services/HRMovements/HR.Movements.Domain/Services/MovementChecklistDefaults.cs)). و`MovementKind.Secondment` معروض «إعارة» ([MovementChecklistEnums.cs:9-12](../src/Services/HRMovements/HR.Movements.Domain/Enums/MovementChecklistEnums.cs)).
- **الأثر:** تنفيذ مكرر لحركة واحدة ممكن تحت الضغط. قائمة الحوكمة لا تقول شيئاً عن حال الأطراف، فلا يمكن الاعتماد عليها في المصالحة كما تقترح [FUN_003_RECONCILIATION_CHECKLIST.md](FUN_003_RECONCILIATION_CHECKLIST.md).
- **الإصلاح المقترح:** `xmin` على كل كيانات الطلب، وانتقالات مشروطة ذرية، وفهرس فريد جزئي «طلب مفتوح واحد لكل موظف ونوع»، وتحقق التواريخ؛ تعليم خطوات الأطراف من إقراراتها بدليلها الحقيقي، وتنبيه SLA.
- **التحقق المطلوب:** AC-MOV-Fe: تفعيلان متزامنان لانتداب واحد ← سجل تنفيذ واحد وحدث واحد. سالب: `EndDate` قبل `StartDate` ← رفض؛ طلب ثانٍ مفتوح من النوع نفسه ← رفض.

### 4.3 قدرات ناقصة (P2)

#### G-MOV-15 — القواعد القانونية للحركات غير مربوطة بالمصدر
- **الدليل:**
  - لا `LegalSourceId` على أي طلب حركة؛ لا مدد قصوى للانتداب والإعارة والندب، ولا تمديد، ولا مدة خدمة دنيا للنقل، ولا حصص، ولا فترة بين نقلين.
  - قيم افتراضية مثبتة: 3 أشهر للانتداب والندب و6 للإعارة ([MovementsExtendedControllers.cs:258, 637, 1067](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))، وبديل 6 أشهر في الـ saga ([TransferSecondmentStateMachines.cs:546](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs)).
  - قواعد الترفيع المحمّلة غير مطبّقة (G-MOV-05)، وقيمها في [service-regimes.json:93-98](syr/service-regimes.json) بلا مصدر قانوني لكل قيمة.
  - [legal-source-binding.md:8-14](syr/legal-source-binding.md) يشترط ربط كل قاعدة بمصدر ومادة وتاريخ نفاذ.
- **الأثر:** لا يمكن إثبات أن انتداباً أو ترفيعاً تم ضمن الحدود القانونية، ولا منع تجاوزها.
- **الإصلاح المقترح:** حزمة قواعد حركات لكل نظام خدمة مربوطة بالسجل التشريعي (D-MOV-3)، بلا قيم افتراضية في الإنتاج (على غرار `Recruitment:Competition`)، وفحصها عند الإنشاء والاعتماد والتمديد.
- **التحقق المطلوب:** AC-MOV-Ga: انتداب يتجاوز المدة المعتمدة ← رفض بمرجع المادة. سالب: غياب القيمة في الإنتاج ← الطلب محجوب برسالة «قيمة قانونية غير مهيأة».

#### G-MOV-16 — لا قرار إداري ولا وثيقة ولا معاملة خدمة
- **الدليل:**
  - `TransitionDecisionRef` نص حر ([TransferRequest.cs:63-64](../src/Services/HRMovements/HR.Movements.Domain/Entities/TransferRequest.cs)؛ [PromotionRequest.cs:30-33](../src/Services/HRMovements/HR.Movements.Domain/Entities/PromotionRequest.cs)).
  - لا توليد PDF لقرار النقل أو الترفيع أو الانتداب (لا مرجع QuestPDF في الخدمة).
  - `ServiceTransactionType` بلا نقل ولا ترفيع ولا انتداب ([ServiceTransactionType.cs:5-17](../src/Services/HRContracts/HR.Contracts.Domain/Enums/ServiceTransactionType.cs))، و`AdministrativeDecision` غير مربوط بالحركات.
  - نشرة التنقلات تُولَّد للنقل فقط ([MovementsExtendedControllers.cs:71-102](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)) وتبقى قابلة للتعديل بعد التوليد ([:52-67](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
- **الأثر:** القرار الإداري هو الأداة القانونية للحركة، ولا يصدره النظام. لا رقم ولا توقيع ولا وثيقة تُبلَّغ للموظف أو الجهة المستقبِلة.
- **الإصلاح المقترح:** إصدار `AdministrativeDecision` مرقّم في HR.Contracts عند الاعتماد (D-MOV-7)، وPDF عربي، وأنواع معاملات خدمة للحركات، ونشرة مجمّدة بعد الإصدار.
- **التحقق المطلوب:** AC-MOV-Gb: اعتماد نقل ← قرار مرقّم وPDF مرتبط، ولا تنفيذ بدونه. سالب: تعديل نشرة صادرة ← مرفوض.

#### G-MOV-17 — لا خدمة ذاتية ولا إشعارات
- **الدليل:** لا ذكر للحركات في `EssPortalController` ولا `EssTalentController` في HR.Core، ولا استعمال لخدمة إشعارات في HR.Movements.
- **الأثر:** الموظف لا يطلب نقلاً ولا يرى قراره ولا تاريخ نفاذه. الوحدة المستقبِلة لا تُبلَّغ.
- **الإصلاح المقترح:** صفحة «حركاتي» في ESS (قراءة وطلب نقل اختياري بحسب D-MOV-9)، وإشعار للموظف والوحدتين عند الاعتماد والتنفيذ والإنهاء.
- **التحقق المطلوب:** AC-MOV-Ha: الموظف يرى حركاته وقراراتها فقط. سالب: لا يرى حركات غيره.

#### G-MOV-18 — التقارير ونشرات التنقلات شكلية
- **الدليل:**
  - لوحة القيادة أربعة عدّادات للجهة كلها ([DashboardController.cs:12-19](../src/Services/HRMovements/HR.Movements.Web/Controllers/DashboardController.cs)).
  - لا تصدير Excel/PDF، ولا سجل حركات في ملف الموظف، ولا تغذية Dev.Analytics.
  - تحليل فجوة التوزيع يأخذ القوى الفعلية من إدخال المستخدم ([MovementsExtendedControllers.cs:206-216](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))، واعتماد الخطة بلا سياسة ولا فصل مهام ([:224-240](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
- **الأثر:** لا جواب عن «كم نُقل/انتُدب/رُفّع هذا العام، ومن وإلى أين» إلا بقاعدة البيانات.
- **الإصلاح المقترح:** تقارير بنطاق الوحدة وتصدير، وسجل حركات في ملف الموظف، ونشر مجاميع إلى Dev.Analytics، والقوى الفعلية من HR.Core.
- **التحقق المطلوب:** AC-MOV-Hc: تقرير الحركات حسب النوع والوحدة والفترة مطابق للسجلات، والتصدير بنطاق المستخدم.

#### G-MOV-19 — نموذج الحركات المؤقتة والاعتماد لا يمثّل الواقع
- **الدليل:**
  - الجهة المستضيفة للانتداب والندب نص حر ([SecondmentRequest.cs:14-16](../src/Services/HRMovements/HR.Movements.Domain/Entities/SecondmentRequest.cs)؛ [DelegationRequest.cs:14-17](../src/Services/HRMovements/HR.Movements.Domain/Entities/DelegationRequest.cs))، والإعارة بين وحدتين داخليتين فقط ([LoanAssignmentRequest.cs:14-20](../src/Services/HRMovements/HR.Movements.Domain/Entities/LoanAssignmentRequest.cs))، فلا تُمثَّل إعارة إلى جهة أخرى.
  - لا معالجة مالية للإعارة (من يدفع الراتب)، ولا أثر على الأقدمية والإجازات.
  - اعتماد محلي بخطوة واحدة لكل الأنواع، ولا صندوق عمل للحركات. ولا تمييز بين نقل داخل الجهة ونقل بين جهتين.
- **الأثر:** حالات شائعة (إعارة إلى وزارة أخرى أو منظمة، انتداب براتب من الجهة المستضيفة) لا تُسجَّل كما هي، ونقل بين جهتين غير ممكن.
- **الإصلاح المقترح:** جهة مستضيفة من سجل الجهات (MDM) مع خيار «خارجية»؛ معالجة مالية للإعارة بحسب D-MOV-6؛ مستويات اعتماد لكل نوع (D-MOV-1) وظهورها في صندوق العمل.
- **التحقق المطلوب:** AC-MOV-Gc: إعارة إلى جهة خارجية براتب على المستعير ← وقف الراتب في Payroll طوال المدة واستئنافه عند الإنهاء.

### 4.4 فجوات التحقق والتوثيق

#### G-MOV-20 — ادعاء «Done» و«ReadyForSign-off» بلا دليل (P1)
- **الدليل:**
  - FUN-003 «Done» ([04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md:174-187](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md)؛ [FUN_003_VERIFICATION.md:14](FUN_003_VERIFICATION.md)؛ [05-TRACEABILITY.md:126](completion-backlog/05-TRACEABILITY.md))، وحزمة UAT «ReadyForSign-off» ([FUN_003_UAT_PACK.md:16](FUN_003_UAT_PACK.md)).
  - AC-06 لم يُشغَّل ([summary.md](../acceptance-evidence/summary.md))، واختباره لا ينفّذ نقلاً: يفحص وصول مديري الفروع لموظفين ثابتين ويكتب التوقع نصاً `postTransferExpectation` ([Ac06TransferAccessTests.cs:51-55](../src/Tests/HRManagement.AcceptanceTests/Ac06TransferAccessTests.cs))، بينما [PLAN-01:33](acceptance-plans/PLAN-01-center-branch-isolation.md) يطلب نقلاً فعلياً وانقلاب الوصول.
  - E2E «منع الاعتماد الذاتي» يقارن معرّفَي شخصيتين فقط ([PayrollLeaveTransferFilesRoleJourneyE2ETests.cs:103-111](../src/Tests/HRManagement.E2E/PayrollLeaveTransferFilesRoleJourneyE2ETests.cs))، وبقية اختبارات الحركات تفتح صفحة القائمة.
  - اختبارات الـ saga تستخدم طرف Payroll وهمياً ([Fun003MovementsSagaIntegrationTests.cs:225-242](../src/Tests/HRManagement.IntegrationTests/Fun003MovementsSagaIntegrationTests.cs))، فلا ترى التطبيق المزدوج (G-MOV-04).
  - لا اختبار على الحاوية (G-MOV-02)، ولا على flush الـ outbox للترفيع (G-MOV-05)، ولا على نقل ثانٍ للموظف نفسه (G-MOV-03).
  - الوثائق تخالف الشيفرة: «Close يستعيد» ([FUN_003_EXCEPTION_PATHS.md:10-11](FUN_003_EXCEPTION_PATHS.md))؛ «لا Execute قبل checklist بدليل» ([ADR-FUN-003:30](ADR-FUN-003-movements-journey-closure.md)) والدليل مصطنع؛ «الأثر المالي والوظيفي متزامنان أو حالة معلقة واضحة» ([04-P3:179](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md)).
- **الأثر:** لا شيء في الأدلة يثبت أن أي حركة تعمل من الطرف إلى الطرف خارج المختبر.
- **الإصلاح المقترح:** سيناريوهات AC-MOV-* موسومة `[RequiredAcceptance]` على المكدّس الشبيه بالإنتاج؛ إعادة كتابة AC-06 ليُنفّذ نقلاً؛ رحلة E2E للنقل والترفيع والانتداب؛ تصحيح الوثائق وحالة البطاقة إلى «In progress».
- **التحقق المطلوب:** AC-MOV-Va: AC-06 ينفّذ نقلاً فرع1←فرع2 ويثبت انقلاب الوصول. AC-MOV-Vb: رحلة E2E شبيهة بالإنتاج ناجحة. سالب: غياب البيانات ← `Blocked` لا `Pass`.

### 4.5 تقاطعات مع الدراسات الأخرى

- **→ WF:** التعريف `TRANSFER-APPROVAL` بخطوتين `hr-core` ([WorkflowPackSeeder.cs:38-40](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)) يتيم ومستثنى صراحةً ([WorkflowDefinitionsHaveHandlersTests.cs:20](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs))؛ قرار المحرك أو الاعتماد المحلي في D-MOV-1.
- **→ POL:** القاعدة `R-TRF` تطلق `TRANSFER-APPROVAL` ([PolicyEngineDomainSeeds.cs:74-80](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)) ولا تُقيَّم أبداً؛ `AddHrPolicyEnforceClient` مسجّل بلا استعمال ([Program.cs:36](../src/Services/HRMovements/HR.Movements.Web/Program.cs))؛ قالب السياسات يعرض `Movements.Transfer` بوضع «Shadow» ([PolicyTemplatesController.cs:230](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)).
- **→ IAM:** نمط «دور المعتمد خارج سياسة الوحدة» (G-MOV-01) عام في الوحدات ذات المرشح الواحد ([HrManagementAuthorizationExtensions.cs:194-200](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))؛ 28 POST بلا سياسة صريحة في الحركات.
- **→ QA:** `MassAssignmentArchitectureTests` لا يحمّل `HR.Movements.Web` ([HRManagement.UnitTests.csproj:40-41](../src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj))؛ `OutboxPublishArchitectureTests` يفحص flush في `EssPortalController` وحده ويُدرج متحكمات الحركات في قائمة سماح ([OutboxPublishArchitectureTests.cs:26-47](../src/Tests/HRManagement.UnitTests/OutboxPublishArchitectureTests.cs))؛ AC-06 ينجح دون نقل.
- **→ QA:** عقد `ISeparationCompleted` بإصدار 3 ([ISeparationCompleted.cs:22](../src/Shared/HRManagement.MessageContracts/Events/ISeparationCompleted.cs)) ومستهلكو Admin.Logistics وAuth.Directory يقبلون حتى 2 — يحتاج تحقق كما في G-MOV-08.
- **→ OPS:** الحزمة `service-regimes.json` غير مشحونة لـ HR.Contracts أيضاً، فيرفض التحقق من نوع الفصل ([SeparationRegimeValidationService.cs:8-27](../src/Services/HRContracts/HR.Contracts.Application/Services/SeparationRegimeValidationService.cs)).
- **→ OPS:** `UseDelayedMessageScheduler` ([MassTransitExtensions.cs:79](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)) يحتاج إضافة `rabbitmq_delayed_message_exchange`، ولا يظهر تفعيلها مع الصورة `rabbitmq:3.13-management-alpine` ([docker-compose.yml:27](../docker-compose.yml)). بدونها تفشل جدولة مهلات الـ sagas — يحتاج تحقق.

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-MOV-1 | نموذج الاعتماد: محلي بخطوة واحدة أم عبر محرك المسارات بخطوات لكل نوع؟ | محرك المسارات مع `ApprovalStartCoordinator` كما في PLAN-25، بخطوتين للنقل بين وحدتين رئيسيتين وللترفيع، وخطوة للباقي | MOV-F، G-MOV-19 |
| D-MOV-2 | من يعتمد وكيف يصل إلى الوحدة؟ | `hr-management-approver` يصل إلى التفاصيل والاعتماد فقط؛ النقل بين فرعين يبدؤه المركز (نطاق AND الحالي) | MOV-A |
| D-MOV-3 | القيم القانونية: مدد الانتداب والإعارة والندب وتمديدها، مدة الخدمة الدنيا للنقل والترفيع، شروط التقييم، اشتراط الشاغر، الحصص، والإنهاء الآلي أو التنبيه عند `EndDate` | تُدخل من المرجع المعتمد لكل نظام خدمة، بلا قيم افتراضية في الإنتاج | MOV-C، MOV-E، MOV-G |
| D-MOV-4 | هل يشترط النقل منصباً ممولاً شاغراً في الوحدة الهدف؟ | نعم، تطبيقاً لمعيار EST-001، مع استثناء موثق بقرار | MOV-D |
| D-MOV-5 | مصدر الأثر المالي للنقل: حساب Payroll من المنصب والدرجة، أم إدخال يدوي؟ | حساب Payroll، والاستثناء اليدوي بمبرر وموافقة | MOV-B |
| D-MOV-6 | المعالجة المالية للإعارة والانتداب (من يدفع الراتب، البدل الشهري، أثرهما على الأقدمية والإجازات) | قرار قانوني لكل نظام خدمة؛ الافتراضي «لا أثر» حتى يُعتمد | MOV-G |
| D-MOV-7 | هل القرار الإداري المرقّم شرط قبل التنفيذ؟ | نعم، يصدر من HR.Contracts عند الاعتماد | MOV-G |
| D-MOV-8 | هل تُسمح الحركات بأثر رجعي، وكيف تُسوّى الرواتب؟ | نعم بقرار، والفروق عبر مسير التصحيح القائم | MOV-B، MOV-D |
| D-MOV-9 | تعريف الانتداب والإعارة والندب، والجهات المستضيفة الممكنة (داخلية/خارجية)، وهل يطلب الموظف نقله ذاتياً؟ | تعريف مكتوب من المرجع القانوني؛ الجهة من سجل الجهات؛ طلب ذاتي للنقل فقط | MOV-G، MOV-H |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 1 (P0)   MOV-A (تشغيل المسار في الإنتاج)
الموجة 2 (P0)   MOV-B (الأثر المالي)  ║  MOV-C (الترفيع الفعلي)
الموجة 3 (P1)   MOV-D (الملاك وتاريخ النفاذ والتنقل الداخلي)  ║  MOV-F (النطاق وفصل المهام والتزامن)
الموجة 4 (P1)   MOV-E (الـ saga والتعويض والإنهاء)
الموجة 5 (P2)   MOV-G (القانون والقرار الإداري والنموذج)  ║  MOV-H (الخدمة الذاتية والتقارير)
مستمر           MOV-V (القبول الحي وE2E وتصحيح الوثائق) — يُغلق مع كل خطة
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) |
|-------|---------|---------|----------|-----------|-------------------|
| MOV-A | تشغيل مسار الحركات بإعدادات الإنتاج | G-MOV-01، G-MOV-02، G-MOV-03، flush الـ outbox من G-MOV-05، سياسات POST من G-MOV-10 | P0 | D-MOV-2 | 5–7 |
| MOV-B | أثر مالي صحيح ومرة واحدة | G-MOV-04، G-MOV-12، جزء Payroll من G-MOV-07 | P0 | MOV-A، D-MOV-5، D-MOV-8 | 6–8 |
| MOV-C | الترفيع الفعلي | G-MOV-05، قواعد الترفيع من G-MOV-15 | P0 | MOV-A، MOV-B، D-MOV-3 | 8–10 |
| MOV-D | النقل والملاك وتاريخ النفاذ والتنقل الداخلي | G-MOV-06، G-MOV-07، G-MOV-13، تحقق المصدر من G-MOV-10 | P1 | MOV-A، MOV-B، D-MOV-4 | 8–10 |
| MOV-E | الـ saga والتعويض وإنهاء الحركات المؤقتة | G-MOV-08، G-MOV-09 | P1 | MOV-B، MOV-D | 8–10 |
| MOV-F | النطاق وفصل المهام والتزامن | G-MOV-10، G-MOV-11، G-MOV-14 | P1 | MOV-A، D-MOV-1 | 6–8 |
| MOV-G | الربط القانوني والقرار الإداري ونموذج الحركات المؤقتة | G-MOV-15، G-MOV-16، G-MOV-19 | P2 | MOV-C، MOV-E، D-MOV-3/6/7/9 | 7–9 |
| MOV-H | الخدمة الذاتية والإشعارات والتقارير | G-MOV-17، G-MOV-18 | P2 | MOV-D، MOV-E | 5–7 |
| MOV-V | التحقق والتوثيق | G-MOV-20 | P1→مستمر | كل الخطط | 5–6 |
| **المجموع** | | | | | **≈ 58–75** |

**MOV-A — تشغيل مسار الحركات بإعدادات الإنتاج.**
- سياسة صريحة لكل إجراء، واستثناء متحكمات الحركات من مرشح الوحدة، وقاعدة بوابة للمعتمد.
- شحن `service-regimes.json` مع كل خدمة تستعمله، وفحص إقلاع يرفض القواعد الفارغة.
- إعادة تعريف بوابة التعارض (حركات مفتوحة وتداخل فترات، عند الاعتماد والتنفيذ، مع الترفيع والندب وحالة الموظف).
- `SaveChangesAsync` بعد النشر في الترفيع والندب، واختبار معماري لنمط «Publish بلا Save لاحق».
- القبول: AC-MOV-Aa (وصول المعتمد)، AC-MOV-Ab (الحاوية)، AC-MOV-Ac (حركات متتالية)، AC-MOV-Ad (وصول `IPromotionExecuted` إلى الـ outbox ثم المستهلك)، AC-MOV-Ae (سالب: POST بدور خاطئ ← 403).

**MOV-B — أثر مالي صحيح ومرة واحدة.**
- نقطة تطبيق واحدة في Payroll للنقل والانتداب، ومفتاح idempotency بمعرّف الطلب وقيد فريد.
- نسخة ملف أجر بتاريخ نفاذ بدل `BaseSalary +=`، وفروق رجعية عبر مسير التصحيح.
- حساب الأثر من المنصب والدرجة الهدف وعرضه قبل الاعتماد؛ إزالة الإنشاء الحر لـ `TransferFinancialImpacts`.
- القبول: AC-MOV-Ba (تطبيق واحد)، AC-MOV-Bb (تكرار/استئناف/إقرار متأخر بلا أثر إضافي)، AC-MOV-Bc (حدود الدرجة والعرض على المعتمد).

**MOV-C — الترفيع الفعلي.**
- الدرجة والمرتبة الحاليتان من المصدر؛ ربط الدرجة بـ `PayGrade`.
- مستهلك Payroll يغيّر الدرجة والأساس بتاريخ نفاذ؛ HR.Core يحفظ الدرجة في سجل منظم لا في `Notes`.
- قواعد الأهلية (المدة، التقييم، الشاغر) من الحزمة القانونية؛ رفض وإلغاء؛ دخول الترفيع في بوابة التعارض.
- القبول: AC-MOV-Ca (أثر في النظامين)، AC-MOV-Cb (رجعي مرة واحدة)، AC-MOV-Cc (أهلية سالبة)، AC-MOV-Cd (رفض/إلغاء)، AC-MOV-Ce (درجة «من» مخالفة ← رفض).

**MOV-D — النقل والملاك وتاريخ النفاذ والتنقل الداخلي.**
- منصب هدف شاغر ممول، وإخلاء/إشغال ذريان مع `FundedPositionVersion`، وسجل وظيفي يرث بيانات الوظيفة.
- تاريخ نفاذ في النموذج والأحداث، و«اليوم» بتوقيت الجهة، ومهمة يومية للتنفيذ المجدول.
- الموظف يجب أن يكون في وحدة المصدر.
- مسار التنقل الداخلي من التوظيف إلى نقل كامل قابل للتنفيذ.
- القبول: AC-MOV-Da، Db (السجل)، Dc (مستقبلي آلي)، Dd (رجعي)، De (تنقل داخلي)، Df (مصدر مخالف ← رفض).

**MOV-E — الـ saga والتعويض وإنهاء الحركات المؤقتة.**
- الأطراف تنفّذ الأثر ثم تُقرّ؛ حالة `Executing` حتى الحدث النهائي؛ فشل مستهلك نهائي أو انتقال نظام = تدخل.
- تعويض حقيقي لكل طرف، أو «حركة عكسية» بقرار.
- أحداث إنهاء للانتداب والإعارة والندب بمستهلكات تعيد الأثر؛ إنهاء من `Active` فقط؛ مهمة `EndDate`؛ تمديد.
- شاشة تدخل بنطاق وسبب وفاعل، و`Resolved` بعد نجاح الاستئناف لا قبله.
- القبول: AC-MOV-Ea، Eb، Ec، Ed، Ee، Ef (شاشة التدخل وسجلها).

**MOV-F — النطاق وفصل المهام والتزامن.**
- نطاق على كل قائمة وتفصيل وإجراء؛ نماذج عرض بدل كيانات EF؛ `RequestedBy` من التوكن دائماً؛ منع اعتماد من عدّل.
- `Reject` بسبب محفوظ لكل الأنواع، وأزرار إلغاء في الواجهة.
- `xmin` وانتقالات ذرية وفهرس «طلب مفتوح واحد»؛ تعليم خطوات الأطراف من إقراراتها.
- إن اختير محرك المسارات في D-MOV-1: نحو 3 أيام إضافية.
- القبول: AC-MOV-Fa، Fb، Fc، Fd، Fe.

**MOV-G — الربط القانوني والقرار الإداري ونموذج الحركات المؤقتة.**
- حزمة قواعد الحركات مربوطة بالسجل التشريعي، بلا قيم افتراضية في الإنتاج.
- قرار إداري مرقّم وPDF من HR.Contracts، وأنواع معاملات خدمة للحركات، ونشرة مجمّدة لكل الأنواع.
- جهة مستضيفة من سجل الجهات، ومعالجة مالية للإعارة والانتداب بحسب D-MOV-6.
- القبول: AC-MOV-Ga، Gb، Gc، Gd (تمديد ضمن الحد، وسالب فوقه).

**MOV-H — الخدمة الذاتية والإشعارات والتقارير.**
- «حركاتي» في ESS، وإشعارات للموظف والوحدتين.
- تقارير بنطاق وتصدير، وسجل حركات في ملف الموظف، ومجاميع إلى Dev.Analytics.
- القبول: AC-MOV-Ha، Hb (إشعار عند الاعتماد والتنفيذ)، Hc (تقرير مطابق ومحصور بالنطاق).

**MOV-V — التحقق والتوثيق.**
- AC-06 ينفّذ نقلاً فعلياً ويثبت انقلاب الوصول.
- رحلة E2E شبيهة بالإنتاج: نقل ثم ترفيع ثم انتداب وإنهاؤه، على الصور المبنية وبشخصيات بلا `admin`.
- اختبارات تكامل بأطراف حقيقية (Payroll وHR.Core) بدل الطرف الوهمي.
- تصحيح `FUN_003_*` و`ADR-FUN-003` و`04-P3` و`05-TRACEABILITY` لتطابق الشيفرة، وحالة البطاقة «In progress» حتى §7.
- القبول: AC-MOV-Va، Vb، Vc (كل مجلد أدلة بالعناصر الثمانية، ولا نجاح دون فحص).

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين: نتيجة تشغيل الحاوية (G-MOV-02)، وقرارات D-MOV-1 وD-MOV-3 وD-MOV-6.

---

## 7. معيار «اكتمال حركات الموظفين» المقترح

- [ ] **1. تُغلق كل فجوات P0 وP1** (G-MOV-01…G-MOV-14 وG-MOV-20)، أو يؤجَّل ما يؤجَّل بقرار موثّق.
- [ ] **2. تنجح رحلة E2E على الصور المبنية بإعدادات Production** (`IsLabScale=false`، بلا `admin`): `a-movements` ينشئ نقلاً بتاريخ نفاذ ← `a-management-approver` يعتمد ← تنفيذ في التاريخ ← منصب المصدر شاغر والهدف مشغول، والوحدة والسجل في HR.Core، وملف الأجر مرة واحدة، والدليل، وانقلاب وصول مديري الفرعين ← نقل ثانٍ للموظف نفسه ينجح.
- [ ] **3. الترفيع والانتداب لهما أثر قابل للقياس:** ترفيع بتاريخ ماضٍ يغيّر الدرجة في HR.Core والدرجة والأساس في Payroll مع فروق رجعية مرة واحدة؛ وانتداب يُفعَّل ثم يُنهى فيعود الدليل والرواتب إلى حالهما.
- [ ] **4. توجد أدلة `acceptance-evidence/AC-MOV-*` لكل سيناريو موجب وسالب في MOV-A..F، ولا اختبار قبول ينجح دون فحص؛ ويُعاد AC-06 بنقل فعلي.**
- [ ] **5. تُصحَّح `FUN_003_*` و`ADR-FUN-003` و`completion-backlog/04` و`05` لتطابق الشيفرة، ولكل ادعاء دليل.**
