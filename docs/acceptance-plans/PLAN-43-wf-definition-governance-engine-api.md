# PLAN-43 — حوكمة تعريفات المسارات وتقوية واجهة المحرك وفصل المهام في كل أنواعه (WF-A)

**Scope:** WF-001 — [دراسة الفجوات](../WF_001_GAP_STUDY.md): G-WF-04, G-WF-05, G-WF-06  
**Status:** Proposed — 2026-10-30  
**Priority:** P0 (G-WF-04) / P1 (G-WF-05, G-WF-06)  
**Depends on:** PLAN-42 (ثقة الخدمات: أسرار لكل خدمة، وفاعل يُشتق على الخادم في `policies/enforce`)  
**Related cards:** WF-001، IAM-003، IAM-004، PLAN-03 (AC-11)، PLAN-25 (المقيِّم والبوابات ودور الخطوة)

## Goal

أن تصبح ضمانات PLAN-25 غير قابلة للإلغاء، وأن تمتد إلى كل أنواع المحرك:

- لا يكتب تعريف مسار ولا ينشره إلا دور مخصص، ولا يُنشر إلا بعينين وبعد تحقق من حدود النوع. والبدء يحترم فترة سريان التعريف.
- واجهة البدء العامة لا يستدعيها إلا حساب الخدمة المالك للنوع، وتسجّل الطالب البشري الحقيقي. ولا يعتمد حساب خدمة أي خطوة.
- حدث نتيجة العهدة يحمل معرّف النسخة، والمستهلك يرفض النسخة غير المرتبطة.
- دور الخطوة مفروض افتراضياً لكل الأنواع. والطالب والمستبعَدون والموظف المعني لا يعتمدون. والتعريفات التي تشترط معتمدين مختلفين تمنع الفاعل نفسه من خطوتين.

هذه الخطة تبني **اللب الخادمي** للبدء الخارجي (`ExternalApprovalService`). أما **العقد العام** العابر للخدمات (المسار الجديد، والعميل المشترك، وحدث النتيجة العام) فيعرّفه PLAN-46 فوق هذا اللب.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-WF-4 من يكتب التعريفات وينشرها | دور جديد `workflow-admin`، و`hr-admin` أيضاً. الكتابة والنشر من الجهة الوطنية (`TenantDefaults.DefaultTenantId`) فقط في v1. الناشر ≠ المنشئ ≠ مقدّم طلب النشر. التخصيص لكل جهة ← الموجة 2 (WF-H) | §1 |
| حدود الأنواع (guardrails) | الجدول في §1. القيم إعداد في الشيفرة يُراجَع مع المالك، لا قيمة قانونية | §1 |
| D-WF-8 من هو «المنشئ» الممنوع | للنسخ التي بدأتها خدمة: الطالب البشري ∪ `ExcludedApproverUserIds` ممنوعون من كل الخطوات اعتماداً ورفضاً، والموظف المعني (`SubjectEmployeeId`) كذلك. أما قواعد التوظيف والملاك فتبقى في بواباتها كما هي | §3 |
| معتمدون مختلفون عبر الخطوات | عمود `RequireDistinctApprovers`: `true` لكل تعريف جديد ولـ `ASSET-ASSIGN-APPROVAL`، و`false` للتعريفات الوطنية القائمة للتوظيف والملاك. سبب `false`: AC-HCM-25d يعتمد فيه `br1-mgr` الخطوتين 1 و2 من اللجنة، وقواعد التوظيف الخاصة في بواباتها | §3 |
| إعفاء مؤقت من فرض الدور | `Workflow:StepRole:ExemptEntityTypes = ["AssetLossReport"]` في كل البيئات حتى PLAN-45. السبب: دور خطوته (`admin-logistics`) لا يصل إلى واجهة الاعتماد قبله | §3 |
| ثقة الطالب في البدء الخارجي | الطالب يُشتق على الخادم من هوية المستخدم المُمرَّرة وفق اتفاقية PLAN-42، **لا من جسم الطلب** (متوافق مع PLAN-60 §0.1). إلى أن تتوفر: ترويسة `X-Acting-User` من `ForwardActingUserHandler`، تُقبل **فقط** من حساب الخدمة المالك للنوع (إقرار الخدمة). وفي المهام الخلفية (إعادة المحاولة في PLAN-45) تملؤها الخدمة من الطالب المخزَّن عبر `ActingUserScope` | §2 |

كل افتراض خلف إعداد في `HR.Core.Web/appsettings*.json` (قسم `Workflow`) أو في جدول ثابت مختبَر، فلا يتوقف التنفيذ على المالك.

---

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-43-wf-definition-governance-engine-api` | — |
| 0.2 | التحقق من PLAN-42 | `PoliciesController.Enforce` لا يقبل `Mode`/`ActorUserId` من الجسم | مدموج، وإلا توقف وأبلغ |
| 0.3 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 أخطاء (أو NU1903 الموثق فقط إن لم يُغلقه PLAN-34) |
| 0.4 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "Workflow\|Wf0\|Hcm025\|Est024\|Architecture"` و`dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm025\|Workflow\|Ac33"` | أخضر أو إخفاقات موثقة في `reports/plan43-baseline.md` |
| 0.5 | جرد التعريفات | SQL على قاعدة القبول: كل `WorkflowDefinitions` (الرمز، الإصدار، `IsPublished`، `DomainEntityType`، الأدوار في `StepsJson`)، و`PolicyWorkflowBindings` بإصداراتها المثبّتة | الجدول في `reports/plan43-baseline.md`، مع وسم الأدوار غير الموجودة في الكتالوج (`security`، `it-support`) |
| 0.6 | جرد المستدعين | `rg "api/workflow-instances"` و`rg "EnforceStepRoleEntityTypes"` في `src/` و`tests/` و`scripts/` | قائمة في التقرير؛ كل مستدعٍ له بند في هذه الخطة |

---

## In scope

### 1. حوكمة دورة حياة التعريف (G-WF-04)

**المشكلة:**
- `WorkflowDefinitionsController` كله تحت `HrCoreWrite` ([WorkflowDefinitionsController.cs:14](../../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowDefinitionsController.cs)). تشمل هذه السياسة `hr-core` وحسابات الخدمة بنطاق `hr-core.workflow` ([HrManagementAuthorizationExtensions.cs:73-77](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- `Create` (`:46-70`) و`Publish` (`:97-114`) بلا موافقة ثانية، وبلا تحقق من الأدوار أو عدد الخطوات. `GuardrailRolesJson` لا يُقرأ.
- كل بدء يأخذ أعلى إصدار منشور، ويتجاهل `ValidFrom`/`ValidTo` ([WorkflowStarter.cs:116-124](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs)). والخطوة ذات الدور الفارغ تمر ([WorkflowStageGuard.cs:119-120](../../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs)).
- استثناء: الربط في `PolicyWorkflowBindings` يثبّت v1 لطلب الشاغر والعرضين ([WorkflowStarter.cs:93-113](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs)). أما الملاك واللجنة والاستئناف والمسابقة والنتائج فتأخذ أي إصدار جديد فوراً.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `WorkflowDefinitionStatus` | `HR.Core.Domain/Enums/WorkflowDefinitionStatus.cs` | `Draft, PendingPublish, Published, Retired` |
| حقول `WorkflowDefinition` | `HR.Core.Domain/Entities/Workflow/WorkflowDefinition.cs` | `Status`، `CreatedBy`، `SubmittedBy/At`، `PublishedBy/At`، `RetiredBy/At`، `ChangeReason` (≤ 500)، `RequireDistinctApprovers`. `IsPublished` يبقى ويُضبط مع `Status` للمستهلكين القائمين |
| ترحيل | `HR.Core.Infrastructure/Migrations/<ts>_WF043_DefinitionGovernanceExternalStart.cs` | الأعمدة أعلاه مع أعمدة §2، و`xmin` على `WorkflowDefinition` (`ConfigureOptimisticConcurrency`) لأن انتقالات الحالة تتسابق. تعبئة: كل منشور ← `Published`، وغير المنشور ← `Retired` إن كان في `RetiredCodes` وإلا `Draft`. `RequireDistinctApprovers=true` لـ `ASSET-ASSIGN-APPROVAL` فقط |
| `WorkflowGuardrails` | `HR.Core.Domain/Services/Workflow/WorkflowGuardrails.cs` | جدول ثابت لكل `DomainEntityType`: أدنى عدد خطوات، ودور الخطوة الأخيرة، والأدوار الإلزامية (الجدول أدناه) |
| `WorkflowDefinitionValidator` | `HR.Core.Domain/Services/Workflow/WorkflowDefinitionValidator.cs` | يعيد قائمة أخطاء عربية: مستويات فريدة ومتصلة من 1؛ اسم ودور لكل خطوة؛ الدور ضمن `HrManagementRoles.All` (جديد)؛ `DomainEntityType` معروف (`WorkflowInstanceActionService.HandledEntityTypes` ∪ سجل §2)؛ حدود النوع؛ شروط بمشغّلات معروفة |
| `HrManagementRoles.WorkflowAdmin` و`All` | `src/Shared/HRManagement.ServiceDefaults/HrManagementRoles.cs` | الدور `workflow-admin`، وقائمة كل أسماء الأدوار للتحقق |
| سياسة `WorkflowDefinitionAdmin` | `HrManagementAuthorizationExtensions.cs` | `hr-admin` أو `workflow-admin`، ولا حسابات خدمة |
| `WorkflowDefinitionLifecycleService` | `HR.Core.Infrastructure/Services/Workflow/WorkflowDefinitionLifecycleService.cs` | `CreateDraft`، `UpdateDraft`، `SubmitForPublish`، `Publish`، `ReturnToDraft`، `Retire`، كلٌّ بقواعد الحالة والفاعل |
| `WorkflowDefinitionsController` | الملف القائم | سياسة الصنف `WorkflowDefinitionAdmin`؛ القراءة `HrCoreReadOnly`؛ مسارات جديدة `POST {id}/submit`، `POST {id}/publish`، `POST {id}/return`، `POST {id}/retire`؛ ويُحذف النشر بخطوة واحدة |
| قاعدة البوابة | `GatewayPathAuthorizationMiddleware.cs` (فرع `hr-core` المتغيّر، [:180-191](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)) | `/api/workflow-definitions` المتغيّر ← `WorkflowDefinitionAdmin` |
| `WorkflowStarter` | [WorkflowStarter.cs:93-124](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs) | يرشّح بـ `ValidFrom <= now` و`ValidTo >= now` في الربط والبحث معاً |
| `WorkflowPackSeeder.EnsureVersionAsync` | `HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs` | مسار نظامي لترقية تعريف وطني: إن اختلفت خطوات آخر منشور عن الهدف المكتوب في الشيفرة، يُنشر إصدار جديد (`PublishedBy=null`، `ChangeReason="seed:<plan>"`) وتُسحب الإصدارات الأقدم (النسخ المثبّتة تبقى). تستعمله PLAN-45/46 |
| `WorkflowDefinitionHealthReport` | `HR.Core.Infrastructure/Services/Workflow/WorkflowDefinitionHealthReport.cs` | عند الإقلاع: يشغّل المحقِّق على كل منشور، ويسجّل تحذيراً ومقياس `workflow_definition_invalid_total`. لا يُفشل الإقلاع (الحالات القائمة مثل `security` تعالجها PLAN-46) |
| شخصيتان والدور | `testdata/acceptance/personas.json` و`AcceptancePersonaCatalog` (عدد الشخصيات مشتق منذ PLAN-41)، والدور `workflow-admin` في `docker/keycloak/realm-manifest.json` (PLAN-39) | `p-workflow-admin` و`p-workflow-admin-2` في `PLATFORM` بدور `workflow-admin` |

**حدود الأنواع (الافتراضي):**

| `DomainEntityType` | أدنى خطوات | دور الخطوة الأخيرة | ملاحظة |
|--------------------|-----------|---------------------|--------|
| `RecruitmentRequisition`، `EstablishmentChangeRequest`، `RecruitmentCompetition`، `CompetitionResultVersion` | 2 | `hr-management-approver` | — |
| `OfferLetter` | 2 (و3 لـ `RECRUITMENT-OFFER-OVERRIDE-APPROVAL`) | `hr-management-approver` | — |
| `SelectionCommitteeSession` | 3 | `hr-management-approver` | — |
| `RecruitmentAppeal` | 1 | `compliance` | — |
| `AssetAssignment` | 2 | `hr-management-approver` | تشدّها PLAN-45 (الخطوة 1 `admin-logistics`) |
| `AssetLossReport` | 1 | — | تشدّها PLAN-45 إلى 2 |
| أي نوع آخر معروف | 1 | — | الأدوار من الكتالوج وغير فارغة |

**القواعد:**
1. لا تعديل لتعريف إلا في `Draft`. و`Delete` للمسودة فقط.
2. `SubmitForPublish` يشغّل المحقِّق، فإن وُجد خطأ رُفض بقائمة الأخطاء ولا تتغير الحالة.
3. `Publish` يتطلب حالة `PendingPublish` وفاعلاً ≠ `CreatedBy` و≠ `SubmittedBy`، ويعيد تشغيل المحقِّق. الرسالة: «لا يجوز لمنشئ التعريف أو مقدّمه نشره».
4. الإصدار المنشور لا يُعدَّل أبداً. التغيير = إصدار جديد. والنسخ الجارية تبقى على إصدارها المثبّت (قائم).
5. إن كان الرمز مربوطاً في `PolicyWorkflowBindings` بإصدار آخر، يعيد `Publish` تحذيراً صريحاً: «الربط يثبّت الإصدار N؛ الإصدار الجديد لن يُستعمل في مسار السياسة حتى يُحدَّث الربط». إدارة الربط (تحديث الإصدار المثبّت) ← الموجة 2 (POL)؛ وPLAN-47 يتحقق فقط من وجود الربط عند نشر قاعدة `TriggerWorkflow`، وPLAN-49 يبقي روابط الرموز المحكومة.
6. الكتابة من جهة غير الجهة الوطنية تُرفض: «إدارة التعريفات وطنية في هذا الإصدار».
7. `Retire` يتطلب سبباً (≥ 10 أحرف). ويُرفض إن كان آخر إصدار منشور لنوع يبدؤه منسّق أو خدمة، إلا بإصدار بديل منشور.
8. كل انتقال يُدقَّق آلياً عبر `AuditSaveChangesInterceptor` (`EntityAuditLog`)، والحقول تحفظ الفاعل.

### 2. البدء الخارجي المقيّد واعتماد الخدمات (G-WF-05)

**المشكلة:**
- `Start` تحت `HrCoreWrite` يقبل أي نوع غير مفروض، بأي `EntityId` و`ContextJson`، ويجعل المستدعي `InitiatedBy` ([WorkflowInstancesController.cs:48-101](../../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)).
- AdminLogistics تستدعيه بـ client credentials دون الفاعل الحقيقي ([ServiceIdentityClient.cs:141-145](../../src/Shared/HRManagement.ServiceDefaults/ServiceIdentityClient.cs)، [DependencyInjection.cs:25-27](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/DependencyInjection.cs))، فيصير `InitiatedBy` حساب الخدمة.
- `Approve`/`Reject` تحت `HrCoreBranchOperation` (`:103-132`)، وهي تقبل حساب خدمة بنطاق `hr-core.workflow` ([HrManagementAuthorizationExtensions.cs:91-97](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). والإنتاج يمنح هذا النطاق لـ `svc-admin-logistics` ([docker-compose.production.yml:439-441](../../docker-compose.production.yml)).
- النتيجة تُقرأ من `ContextJson` ([WorkflowInstanceActionService.cs:215-224](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)). والمستهلك يفعّل التسليم بمعرّفه دون مقارنة النسخة ([AssetAssignmentWorkflowConsumers.cs:23-42](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/AssetAssignmentWorkflowConsumers.cs)، [AssetLossWorkflowConsumers.cs:27-28، 60-61](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/AssetLossWorkflowConsumers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `ExternalApprovalTypes` | `HR.Core.Infrastructure/Services/Workflow/External/ExternalApprovalTypes.cs` | سجل ثابت: `EntityType`، `OwnerClientId` (من `ServiceClientIds`)، `AllowedWorkflowCodes`، `DefaultWorkflowCode`، `OutcomeEvent` (`AssetLegacy` الآن؛ `Generic` في PLAN-46)، `AudienceRoles` (أدوار من خارج HR.Core تصل إلى الصندوق)، `DetailsUrlPrefix` (مثل `/ui/admin-logistics/`). المدخلان الأوّلان: `AssetAssignment` و`AssetLossReport` (المالك `svc-admin-logistics`) |
| `WorkflowApprovalStartRequest` (جسم السلك) | `src/Shared/HRManagement.ServiceDefaults/Workflow/CrossService/WorkflowApprovalStartRequest.cs` | بالاسم والترتيب الواردين في PLAN-60 §0.1: `EntityType`، `EntityId`، `WorkflowCode`، `IdempotencyKey`، `ExcludedApproverUserIds`، `OrgUnitId?`، `DisplayTitle`، `DetailsUrl`، `Facts?`، ومعامل اختياري أخير `SubjectEmployeeId? = null`. **لا** يحمل الطالب. يضيف PLAN-46 العميل والحدث والحالة حوله في المجلد نفسه |
| `ActingUserScope` | `src/Shared/HRManagement.ServiceDefaults/ActingUserScope.cs` + `ForwardActingUserHandler` ([ServiceIdentityClient.cs:169-189](../../src/Shared/HRManagement.ServiceDefaults/ServiceIdentityClient.cs)) | نطاق `AsyncLocal` يحمل `(UserId, DisplayName)` للمهام الخلفية. المعالج يستعمله إن غاب مستخدم الطلب، ويضيف `X-Acting-User-Name` (مرمّزاً، للعرض فقط). يُنسَّق مع PLAN-42 إن غيّر المعالج |
| `ExternalApprovalService` | `HR.Core.Infrastructure/Services/Workflow/External/ExternalApprovalService.cs` | `StartAsync(request, caller)` و`CancelAsync(instanceId, caller, reason)` بالقواعد أدناه |
| أعمدة `WorkflowInstance` | `HR.Core.Domain/Entities/Workflow/WorkflowInstance.cs` + ترحيل §1 | `RequestedByUserId`، `RequestedByName`، `SourceClientId`، `IdempotencyKey` (≤ 200)، `ScopeOrgUnitId`، `SubjectEmployeeId`، `ExcludedApproverUserIdsJson`، `DisplayTitle` (≤ 300)، `DetailsUrl` (≤ 500) |
| `WorkflowEngine.Withdraw` | `ServiceDefaults/Workflow/WorkflowEngine.cs` + `IWorkflowEngine.cs` | ينهي نسخة جارية بحالة `Cancelled` وقيد تاريخ `Withdrawn` |
| `WorkflowStepStatus.Withdrawn` | `SharedKernel/Workflow/WorkflowStepStatus.cs` | قيمة تُضاف في النهاية |
| سياسة `WorkflowExternalStart` | `HrManagementAuthorizationExtensions.cs` | حساب خدمة بنطاق `hr-core.workflow` فقط. ملكية النوع تفحصها الخدمة لا السياسة |
| سياسة `WorkflowStepAction` | الملف نفسه | Admin \| HrCore \| BranchManager \| HrManagementApprover، **بلا** حسابات خدمة. تضيف إليها PLAN-45 `admin-logistics`، وPLAN-46 `idcard`/`compliance` |
| `WorkflowInstancesController` | الملف القائم | `Start` ← `WorkflowExternalStart` + `ExternalApprovalService` بجسم `WorkflowApprovalStartRequest`؛ `POST {id}/cancel` جديد (المالك فقط)؛ `Approve`/`Reject` ← `WorkflowStepAction` |
| قواعد البوابة | `GatewayPathAuthorizationMiddleware.cs:180-191` | `/api/workflow-instances/*/approve\|reject` ← `WorkflowStepAction`؛ و`POST /api/workflow-instances` ← `WorkflowExternalStart` (المستخدم لا يعبر البوابة) |
| `WorkflowTriggerService` | [WorkflowTriggerService.cs:17-130](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs) + `DependencyInjection.cs:25-27` | الجسم الجديد (`IdempotencyKey = "AssetAssignment:{id}"`، العنوان، الرابط، `SubjectEmployeeId`)، و`forwardActingUser: true`. إعادة بناء التدفق نفسه ← PLAN-45 |
| `PolicyOrchestrator.TryStartWorkflowAsync` | [PolicyOrchestrator.cs:158-178](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs) | لا يبدأ نسخة لنوع مسجَّل في `ExternalApprovalTypes` (يعيد `null` مع إبقاء أثر `TriggerWorkflow` في القرار). فالمالك وحده يبدأ عبر `Start`، ولا نسخة بفاعل من جسم `policies/enforce`. أثر ذلك على PLAN-42: يُحذف `AssetAssignment` من `PolicyWorkflowTriggerAllowlist` (فتفرغ القائمة)، ويصير إيجابي AC-S2S-42c «200 وأثر `TriggerWorkflow` في القرار بلا نسخة»، وAC-11 بصيغة PLAN-42 يتحقق من القرار ثم يبدأ النسخة بـ `Start` بهوية `svc-admin-logistics` و`X-Acting-User`، إلى أن يعيد PLAN-45 كتابته رحلةً من الواجهة |
| أحداث العهدة v2 | `MessageContracts/Events/IAssetAssignmentApproved.cs` و`…Rejected`، `IAssetLossApproved` و`…Rejected` | عضو جديد `Guid? WorkflowInstanceId => null` و`SchemaVersion => 2`. وتُملأ في [WorkflowInstanceActionService.cs:208-266](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs) من `instance.Id` لا من `ContextJson` |
| المستهلكون الأربعة | `Admin.Logistics.Infrastructure/Consumers/AssetAssignmentWorkflowConsumers.cs`، `AssetLossWorkflowConsumers.cs` | `supportedMax: 2`. التطبيق فقط إن كان `WorkflowInstanceId == assignment.WorkflowInstanceId`، وإلا تحذير ومقياس `workflow_outcome_ignored_total`. الأصل من صف التسليم لا من الرسالة |

**القواعد (`ExternalApprovalService.StartAsync`):**
1. المستدعي حساب خدمة، و`GetClientId` يساوي `OwnerClientId` للنوع في السجل. وإلا 403 «هذه الخدمة لا تملك بدء هذا النوع».
2. `WorkflowCode` (أو الافتراضي) ضمن `AllowedWorkflowCodes`، وإلا 400.
3. الطالب (`RequestedByUserId`) يُشتق على الخادم من `X-Acting-User` (أو من تأكيد PLAN-42 حين يتوفر)، ولا يُقرأ من الجسم أبداً. إن غاب ← 400 «تعذّر تحديد طالب الإجراء». و`RequestedByName` من `X-Acting-User-Name` للعرض فقط.
4. `IdempotencyKey` إلزامي (≤ 200). البحث أولاً بـ `(TenantId, SourceClientId, IdempotencyKey)` في كل الحالات:
   - وُجدت نسخة ← تعاد هي نفسها مع حالتها (`AlreadyActive = Status == InProgress`، 200)، حتى لو انتهت. فإعادة المحاولة بعد مهلة لا تنشئ نسخة ثانية، والمالك يطبّق النتيجة إن كانت منتهية.
   - لا نسخة بالمفتاح، وتوجد نسخة جارية للكيان نفسه بمفتاح آخر ← 409 «يوجد مسار جارٍ لهذا الكيان» مع معرّفها.
5. `DisplayTitle` (≤ 300) بلا تسلسل أرقام ≥ 8 (حماية الرقم الوطني)، و`DetailsUrl` مسار نسبي يبدأ بـ `DetailsUrlPrefix` للنوع، و`Facts` ≤ 20 بنداً (المفتاح ≤ 50 والقيمة ≤ 200). وإلا 400.
6. يُختار آخر إصدار منشور ضمن فترة السريان للرمز، دون الربط في `PolicyWorkflowBindings`. وإن غاب: 422 «لا يوجد تعريف مسار اعتماد منشور لهذا الإجراء» (`ApprovalStartCoordinator.MissingDefinitionMessage`).
7. `InitiatedBy = RequestedByUserId`، و`SourceClientId` = المستدعي. تُحفظ الحقول الإضافية كما هي.
8. `CancelAsync`: المالك فقط، والنسخة جارية ← `Cancelled` بقيد `Withdrawn` وسبب إلزامي. وإن كانت منتهية ← 409 «لا يمكن سحب مسار منتهٍ» مع حالتها النهائية. لا يُنشر حدث نتيجة للإلغاء في هذه الخطة (PLAN-46 يضيف `Cancelled` إلى `IWorkflowOutcomeDecided`).
9. حساب الخدمة لا يعتمد ولا يرفض أي خطوة (`WorkflowStepAction`)، ولا يكتب تعريفاً (`WorkflowDefinitionAdmin`).
10. مسار البدء الوحيد لنوع مسجَّل هو `ExternalApprovalService.StartAsync`. محرك السياسات يشير فقط («يتطلب اعتماداً»)، والمالك يبدأ.

### 3. فصل المهام في كل أنواع المحرك (G-WF-06)

**المشكلة:**
- `EnforceStepRoleEntityTypes` قائمة إدراج تستثني العهدة والفقدان وكل نوع جديد ([WorkflowStageGuard.cs:14-34](../../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs)، [appsettings.Production.json:260-271](../../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)). ولا بوابة لهذين النوعين ([WorkflowApprovalEvaluator.cs:42-52](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/WorkflowApprovalEvaluator.cs)).
- المحرك يمنع الفاعل نفسه في **الخطوة نفسها** فقط ([WorkflowEngine.cs:54-60](../../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowEngine.cs))، والمنشئ في الخطوات بعد الأولى فقط ([WorkflowStageGuard.cs:104-107](../../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs)). فمستخدم واحد يحمل `hr-core` يعتمد خطوتي `ASSET-ASSIGN-APPROVAL` كلتيهما.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `WorkflowStepRoleOptions` | `ServiceDefaults/Workflow/WorkflowStageGuard.cs:9-35` | دلالة جديدة: `IsEnforced(type) = !ExemptEntityTypes.Contains(type)`. يُقرأ من `Workflow:StepRole:ExemptEntityTypes`، ويُتجاهل المفتاح القديم `EnforceStepRoleEntityTypes` مع تحذير إقلاع |
| `WorkflowSodRules` | `ServiceDefaults/Workflow/WorkflowSodRules.cs` | دوال نقية مشتركة بين المقيِّم ولوحة الصندوق: `Excluded` (الطالب ∪ المستبعَدون)، `Subject` (`actor.EmployeeId == SubjectEmployeeId`)، `DistinctApprover` (فاعل اعتمد مستوى آخر في النسخة نفسها و`RequireDistinctApprovers=true`) |
| `WorkflowApprovalEvaluator` | [WorkflowApprovalEvaluator.cs:19-53](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/WorkflowApprovalEvaluator.cs) | الترتيب: البنية ← الدور ← `WorkflowSodRules` ← بوابة الكيان ← قاعدة المنشئ القائمة |
| `WorkflowPanelBuilder.ResolveBlockReason` | [WorkflowPanelBuilder.cs:191-233](../../src/Services/HRCore/HR.Core.Web/Services/WorkflowPanelBuilder.cs) | يستدعي `WorkflowSodRules` نفسها، فيبقى تطابق الصندوق مع الخادم (`Hcm025PanelServerParityTests`) |
| الإعداد | `appsettings.json` و`.Staging.json` و`.Production.json` في HR.Core.Web، و`docker-compose.acceptance-prodlike.yml` | حذف `EnforceStepRoleEntityTypes`، وإضافة `"StepRole": { "ExemptEntityTypes": ["AssetLossReport"] }` |

**القواعد:**
1. الرسائل: «لا يجوز لطالب الإجراء أو المستبعَد منه اعتماده أو رفضه» و«لا يجوز للموظف المعني اعتماد إجراء يخصه» و«هذا المسار يتطلب معتمداً مختلفاً لكل خطوة».
2. `Excluded` و`Subject` تُطبَّقان على الاعتماد والرفض. و`DistinctApprover` على الاعتماد فقط.
3. نسخ التوظيف والملاك لا تحمل `RequestedByUserId`، فلا يتغير سلوكها. AC-HCM-25a..h و`Hcm025PanelServerParityTests` تبقى خضراء دون تعديل.
4. `Workflow:AdminBypass` يبقى `false`، ولا يتجاوز القواعد الثلاث حتى لو صار `true`.

---

## Out of scope

- بدء العهدة الموثوق، وصفحة الاعتماد، وصندوق العهدة، وأدوار D-WF-2، وإلغاء الإعفاء ← PLAN-45
- العقد العام العابر للخدمات (المسار الجديد، و`IWorkflowApprovalClient`، و`IWorkflowOutcomeDecided`) والأنواع الجديدة ← PLAN-46
- رمز التزامن والفهرس الفريد وذرّية النتيجة ← PLAN-44
- نقطة `policies/enforce` وثقة الخدمات والأسرار ← PLAN-42؛ وإدارة `PolicyWorkflowBindings` ← الموجة 2 (POL؛ PLAN-47 يتحقق من وجود الربط فقط)
- شاشة إدارة التعريفات، والتخصيص لكل جهة، والخطوات المتوازية ← الموجة 2 (WF-H، G-WF-16)
- التفويض ← PLAN-50

## Acceptance scenarios

على الملف الشبيه بالإنتاج (`docker-compose.acceptance-prodlike.yml` بعد PLAN-37)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، في `AcWf43DefinitionGovernanceTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-WF-43", …)`، `AcceptanceGate.RequireData`).

| ID | Positive | Negative |
|----|----------|----------|
| AC-WF-43a | `p-workflow-admin` ينشئ مسودة `ESTABLISHMENT-CHANGE-APPROVAL` بخطوتين صالحتين ويقدّمها، و`p-workflow-admin-2` ينشرها ← `Published`، والإصدار يظهر في القائمة | `central-a` (`hr-core`) يرسل `POST /api/hr-core/api/workflow-definitions` ← 403، ولا صف جديد |
| AC-WF-43b | — | مسودة بخطوة واحدة بلا دور ← `submit` يُرفض بقائمة أخطاء عربية. ومسودة صالحة ينشرها مقدّمها نفسه ← رفض «لا يجوز لمنشئ التعريف أو مقدّمه نشره» |
| AC-WF-43c | بعد نشر v2 للملاك: تغيير ملاك جديد يبدأ على v2، ونسخة جارية على v1 تكتمل على v1 | تعريف بـ `ValidTo` ماضٍ لا يُستعمل للبدء (فحص قاعدة) |
| AC-WF-43d | تسليم عهدة مميزة من `a-logistics-maker` ← نسخة `RequestedByUserId` = المستخدم و`SourceClientId = svc-admin-logistics` (فحص قاعدة) | `central-a` يرسل `POST /api/hr-core/api/workflow-instances` ← 403 |
| AC-WF-43e | `a-management-approver` يعتمد الخطوة 2 من نسخة العهدة ← النسخة `Completed` والحدث يحمل `WorkflowInstanceId` | `br1-mgr` (طالب التسليم، يحمل `hr-core`) يعتمد الخطوة 1 ← رفض «لا يجوز لطالب الإجراء…». و`central-a` على الخطوة 2 ← رفض الدور |
| AC-WF-43f | `central-a` يعتمد الخطوة 1 من نسخة عهدة طلبها غيره | — |

تُثبت السوالب التي لا تُبلغ حيّاً (حساب خدمة يعتمد، نسخة غير مرتبطة) في اختبارات التكامل أدناه.

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "Wf043|WorkflowStageGuard|Hcm025|Architecture"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Wf043|Hcm025ProductionApproval|Ac33AssetLoss"
# انحدار
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm021|Hcm022|Wf023|Est024|SoD"
# قبول حي على الملف الشبيه بالإنتاج
pwsh scripts/prepare-keycloak-import.ps1      # الشخصيتان الجديدتان
pwsh scripts/acceptance-reset.ps1
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcWf43|AcHcm25|Ac11Policy"
pwsh scripts/wf043-validate.ps1
```

## Definition of done

- لا مسار في `WorkflowDefinitionsController` يقبل `hr-core` أو حساب خدمة للكتابة. والنشر بعينين ومحقِّق، والإصدار المنشور غير قابل للتعديل.
- `WorkflowStarter` و`ExternalApprovalService` يحترمان فترة السريان.
- لا يبدأ نسخة من خارج المنسّق إلا حساب الخدمة المالك للنوع، ويُسجَّل الطالب البشري. ولا يعتمد حساب خدمة أي خطوة.
- أحداث العهدة v2 تحمل `WorkflowInstanceId`، ومستهلكوها يرفضون النسخة غير المرتبطة.
- فرض الدور افتراضي لكل الأنواع، والإعفاء الوحيد `AssetLossReport` (حتى PLAN-45). والقواعد الثلاث مطبّقة في الخادم واللوحة.
- AC-WF-43a..f = Pass في `acceptance-evidence/AC-WF-43/`. وانحدار AC-HCM-25 وAC-11 أخضر.
- سطر «الحالة» لـ G-WF-04 وG-WF-05 وG-WF-06 في الدراسة محدّث، و`WF_001_WORKFLOW_ENGINE.md` يصف الحوكمة والبدء الخارجي والقواعد.
- الالتزامات بلا أسرار (القوالب تبقى بالعناصر النائبة `__SVC_SECRET__`/`${…}`)، ولا `global.json`، ولا `testdata/*/out/*.json` أعاد الاختبار كتابتها. ولا دفع إلى البعيد.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-43-wf-definition-governance-engine-api.md` |
| Domain | `WorkflowDefinitionStatus.cs`، `WorkflowGuardrails.cs`، `WorkflowDefinitionValidator.cs`، حقول `WorkflowDefinition`/`WorkflowInstance` |
| Infrastructure | `WorkflowDefinitionLifecycleService.cs`، `External/ExternalApprovalTypes.cs`، `External/ExternalApprovalService.cs`، `WorkflowDefinitionHealthReport.cs`، `WorkflowPackSeeder.EnsureVersionAsync`، الترحيل `WF043_DefinitionGovernanceExternalStart` |
| Shared | `HrManagementRoles` (`WorkflowAdmin`، `All`)، السياسات الثلاث، `WorkflowSodRules.cs`، `WorkflowEngine.Withdraw`، `WorkflowStepStatus.Withdrawn`، أحداث العهدة v2 |
| Web | `WorkflowDefinitionsController`، `WorkflowInstancesController`، قواعد البوابة |
| AdminLogistics | `WorkflowTriggerService` (الجسم الجديد)، المستهلكون الأربعة |
| Unit | `Wf043DefinitionLifecycleTests`، `Wf043DefinitionValidatorTests`، `Wf043ExternalStartTests`، `Wf043SodRulesTests`، تحديث `WorkflowStageGuardRoleTests` و`Hcm025ReviewFixTests` (`Start` صار خارجياً) و`GatewayPathAuthorizationMiddlewareTests` |
| Integration (PostgreSQL، Testcontainers) | `Wf043GovernanceIntegrationTests`: نشر بعينين، و`xmin` (نشر وإرجاع متزامنان لتعريف واحد ← ينجح واحد)، فترة السريان، البدء الخارجي (403 لغير المالك، 409 لمفتاح آخر، نفس المفتاح يعيد النسخة ولو منتهية)، `ApprovalStartCoordinator` في معاملته (لا نسخة عند فشل الحفظ)، حساب خدمة يعتمد ← 403، اعتماد الخطوة الأخيرة لنسخة عهدة ← صف `OutboxMessage` بالحدث v2 وفيه `WorkflowInstanceId` (بعد PLAN-40)، ومستهلك العهدة يتجاهل نسخة غير مرتبطة |
| Acceptance | `AcWf43DefinitionGovernanceTests.cs`، وتعديل `Ac11PolicyWorkflowTests.cs` و`AcS2s42TrustBoundaryTests.cs` (42c) و`PolicyWorkflowTriggerAllowlist.cs` بحسب صف `PolicyOrchestrator` في §2 |
| Script | `scripts/wf043-validate.ps1` |
| Docs | `WF_001_WORKFLOW_ENGINE.md` (أقسام الحوكمة والبدء الخارجي والقواعد)، `SOD_MATRIX.md` (القواعد الثلاث)، `WF_001_GAP_STUDY.md` (سطر «الحالة» للفجوات الثلاث) |
| CI | `tests/acceptance/ci-scope.json`: تسجيل كل صنف قبول جديد في هذه الخطة (مطلوباً أو مستبعداً بمالك)، وإلا تفشل وظيفة القبول (قاعدة PLAN-41) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تعريفات قائمة تفشل المحقِّق (`security`، `it-support`) | تقرير لا يُفشل الإقلاع. والتصحيح بالسحب أو بإصدار جديد في PLAN-46 |
| قلب دلالة فرض الدور يغيّر سلوك أنواع لم تُختبر | جرد 0.6، والإعفاء الصريح لـ `AssetLossReport`، وتشغيل كل اختبارات `Workflow` و`Hcm025` و`Est024` |
| `Start` صار للخدمات فقط، فتنكسر اختبارات تستدعيه بمستخدم | الجرد في 0.6. تعديل `Hcm025ReviewFixTests` ليثبت 403. الاختبارات التي تحتاج نسخة تبدؤها عبر المنسّق أو بحساب خدمة في مضيف الاختبار |
| رسائل في الطابور بإصدار 1 للعهدة بعد النشر | لم تغادر الـ Outbox أصلاً قبل PLAN-40 (G-WF-03). المستهلك يتجاهل v1 مع سجل |
| ترويسة `X-Acting-User` غير موقّعة | لا تُقبل إلا من الخدمة المالكة (إقرار الخدمة). والتوقيع ضمن PLAN-42 إن عرّفه |

## Effort estimate

7–8 أيام عمل: المرحلة 0 (0.5)، دورة حياة التعريف والمحقِّق والبوابة (2.5)، البدء الخارجي والسياسات والأحداث v2 (2)، قواعد فصل المهام والتطابق (1)، التكامل والقبول والتوثيق (1–2).
