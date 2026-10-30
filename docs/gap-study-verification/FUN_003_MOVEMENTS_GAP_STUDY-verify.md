# تحقق مستقل — FUN_003_MOVEMENTS_GAP_STUDY

**النطاق:** كل فجوات P0 (G-MOV-01..05) وأول ثلاث فجوات P1 (G-MOV-06..08). قراءة ثابتة لـ `/home/claude/hrm` @ `f9eab1a`.

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-MOV-01 | P0 | مؤكد | المرشح العام `ModuleHrMovements` لا يستثني أي متحكم حركات (HrModuleAuthorizationFilter.cs:27-46). `HrManagementApprove` = `admin`/`hr-management-approver`. لا شخصية ولا مستخدم realm يحمل الدورين معاً (فحصت personas.json وrealm-hrmanagement-dev وrealm-qa004-e2e). لا أدوار مركّبة، ولا قاعدة بوابة خاصة بـ `hr-movements`. واختبار E2E للنقل يفحص الوصول وتمايز الشخصيات فقط، لا الاعتماد. |
| G-MOV-02 | P0 | مؤكد | الحزمة يُنسخها `HR.Core.Infrastructure.csproj:16` فقط. مشاريع HR.Movements لا تشير إلى HR.Core.Infrastructure، وServiceDefaults وSharedKernel لا ينسخان `service-regimes.json`. الـ Dockerfile ينسخ `/app/publish` فقط، ولا volume في compose ولا في `deploy/`. كل نقاط الاستدعاء صحيحة. الحسم النهائي: تشغيل إنشاء نقل على الصورة المبنية. |
| G-MOV-03 | P0 | مؤكد | `HasConcurrentMovementConflict` يعدّ `Executed` تعارضاً بلا تاريخ. `Executed` حالة نهائية: الإرجاع الوحيد (SagaSupportConsumers.cs:189) مشروط بتاريخ مستقبلي يستحيل بعد التنفيذ. الاختبار الوحيد إيجابي. |
| G-MOV-04 | P0 | مؤكد | الطرف (PayrollEventConsumers.cs:119-123) والحدث النهائي (:216-225) يستدعيان المعالج نفسه، والـ saga ترسل `SalaryChange` في الاثنين (TransferSecondmentStateMachines.cs:234, 280). المعالج بلا فحص علامة. لا تجاوز لـ `MandatoryParties` في appsettings. ونمط الانتداب مماثل. |
| G-MOV-05 | P0 | مؤكد | `Publish` بعد آخر `SaveChangesAsync` في الترفيع والندب، مع `UseBusOutbox()`، والتعليق في EssPortalController.cs:117-119 يقرّ بأن الرسالة تُهمل بلا حفظ لاحق. مستهلك الترفيع الوحيد في HR.Core يكتب `Notes` فقط. لا Reject ولا Cancel. الحسم النهائي: تشغيل لرؤية صف outbox. |
| G-MOV-06 | P1 | مؤكد | لا حقل منصب في `TransferRequest`. لا مستهلك `ITransferExecuted` يستدعي `VacatePosition`؛ الاستدعاء الوحيد في `RecruitmentHireClosureService` لمسار التوظيف. وطرف HR.Core إقرار فارغ. |
| G-MOV-07 | P1 | مؤكد | شاشتا الإنشاء والتعديل بلا `EffectiveDate` (المتحكم يضبطه في GET:76 ولا يُعرض). الرسائل تحمل `ExecutedAt`، ولا `HostedService` في HR.Movements. |
| G-MOV-08 | P1 | مؤكد | كل الأسطر صحيحة. لا شاشة للتدخلات في أي خدمة. بند `SchemaVersion` موسوم في الدراسة «يحتاج تحقق»، وهو كذلك: يتوقف على كيف يملأ MassTransit عضو الواجهة الافتراضي، فيحسمه تشغيل نقل ورؤية سجل التحذير في Admin.Logistics. |

## التعديلات على الدراسة
- لا تعديلات؛ كل الفجوات المفحوصة مؤكدة بأدلتها وأسطرها.

## ملاحظات جديدة
- لا جديد خطير. ملاحظة جانبية: `PromotionRequestsController.Execute` (MovementsExtendedControllers.cs:993-1050) لا يستدعي أي فحص نطاق، وهذا داخل G-MOV-10 أصلاً.
