# PLAN-50 — وصول المعتمد الإداري إلى اعتماداته، والتفويض الفعلي (IAM-A)

**Scope:** IAM — [دراسة الفجوات](../IAM_001_SCOPE_GAP_STUDY.md): G-IAM-01؛ و[G-MOV-01](../FUN_003_MOVEMENTS_GAP_STUDY.md) (الفجوة نفسها في الحركات: المعتمد لا يصل، ودور المنشئ لا يعتمد)؛ وأثر التفويض من [G-WF-11](../WF_001_GAP_STUDY.md)  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-37 (الملف الشبيه بالإنتاج، وعميل HR.Movements ← HR.Core بنطاق `hr-core.lookup` في الإنتاج)  
**Related cards:** IAM-003 (SoD والتفويض المؤقت)، WF-001/WF-002 (التفويض في المحرك والصندوق)، PLAN-25 (المقيِّم والبوابات)، PLAN-28 (نمط استثناء `RecruitmentAppeals`)، D-IAM-1، D-MOV-2، D-WF-5

## Goal

- `hr-management-approver` وحده (بلا `hr-movements`) يفتح قائمة النقل والندب والإعارة والترفيع والتكليف وتفاصيلها، ويعتمدها عبر البوابة. لا ينشئ ولا يعدّل ولا ينفّذ ولا يلغي.
- `hr-movements` (دور المنشئ والمنفّذ) لا يعتمد. ومن أنشأ الطلب لا يعتمده.
- يصل المعتمد عبر البوابة إلى الإجراءات التسعة ذات `HrCore.Approve` وإلى شاشة التفويض.
- للتفويض المؤقت أثر فعلي في محرك المسارات (HR.Core) وفي اعتمادات الحركات:
  - المفوَّض يرى عناصر أصيله ويعتمد خطوة الدور المفوَّض داخل المدة والنطاق، ويُرفض بعدهما.
  - يُسجَّل «بالنيابة عن» في سجل الخطوة وفي التوقيع وفي سجل اعتماد الحركة.
- لا يتجاوز التفويض فصل المهام: لا يعتمد المفوَّض ما أنشأه هو أو أصيله، ولا تفويض متسلسل.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-IAM-1 / D-MOV-2 — من يعتمد الحركات | `hr-management-approver` يقرأ القائمة والتفاصيل ويعتمد ويرفض فقط. الإنشاء والتعديل والتنفيذ والإلغاء لـ `hr-movements`. وقائمة المعتمد تعرض افتراضياً ما ينتظر الاعتماد | لا يُمنح المعتمد `hr-movements`، ولا يحتاج المنشئ دور الاعتماد |
| D-WF-5 — سياسة التفويض | مدة ≤ 90 يوماً (`SodMatrixDefaults.MaxDelegationDays`). `RoleScope` إلزامي، ويجب أن يكون من أدوار الأصيل **المباشرة** في رمزه لحظة الإنشاء. `EntityTypeScope` اختياري من قائمة مغلقة (`Delegation:EntityTypes`). لا تفويض متسلسل. المفوَّض لا يعتمد ما أنشأه هو أو أصيله | الغياب الآلي (out-of-office) والتصعيد خارج الخطة (الموجة 2: WF-F) |
| D-50-1 — الأدوار القابلة للتفويض | `Delegation:DelegableRoles = ["hr-management-approver"]`. أي دور آخر يُرفض عند الإنشاء برسالة عربية | تفويض معتمد الرواتب والعقود والامتثال يُضاف لاحقاً بإضافة الدور وربط مستهلكه (الموجة 2: WF-F) |
| D-50-2 — كيف يُعرَّف المفوَّض | يُختار موظفاً من دليل HR.Core، ويُحفظ `DelegateEmployeeId`، ويُطابَق بمطالبة `employee_id`. والصفوف القديمة التي تحمل `DelegateId` فقط تُطابَق بـ `sub` | يحتاج رمز المفوَّض إلى `employee_id`. كل الشخصيات تحمله. وفي المستخدمين الذين ينشئهم Auth.Directory يُصلَح اسم الخاصية هنا (البند 5، القاعدة 5)، لا في PLAN-62 |
| D-50-3 — وصول المفوَّض إلى الشاشات | في HR.Movements (دور الوحدة فيها دور المنشئ): شاشات الاعتماد الأربع (قائمة، تفاصيل، اعتماد، رفض) تقبل المفوَّض النافذ ولو بلا دور وحدة. وفي HR.Core: يجب أن يحمل المفوَّض دور قراءة HR.Core (`HrCore.ReadOnly`) ليفتح التفاصيل والصندوق. البوابة لا تستعلم عن التفويض: تمرّر مسارات الاعتماد لكل مستخدم موظفين تفاعلي، والخدمة تقرر | القرار في الخدمة وحدها لمسارات الاعتماد المسماة |
| D-50-4 — التفويضات القائمة عند الترحيل | كل صف بلا `RoleScope`، أو بدور غير قابل للتفويض، يُلغى في الترحيل (`IsRevoked=true`، `RevokedReason="PLAN-50 migration"`) | لا يتسع أثر تفويض قديم فجأة |
| D-50-5 — تأخر أثر الإلغاء في الخدمات الأخرى | تخزين مؤقت 60 ثانية لكل مستخدم في HR.Movements (`Delegation:CacheSeconds`). وأي خطأ في الاستعلام = لا تفويض (fail-closed) | الإلغاء يسري في HR.Core فوراً، وفي الحركات خلال دقيقة |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-50-iam-approver-reach-delegation` | — |
| 0.2 | البناء | `dotnet build HRManagement.slnx` | 0 أخطاء |
| 0.3 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Gateway\|FullyQualifiedName~SoD\|FullyQualifiedName~Workflow\|FullyQualifiedName~Hcm025\|FullyQualifiedName~Hcm028"` | أخضر، أو قائمة إخفاقات في `reports/plan50-baseline.md` |
| 0.4 | جرد الوصول | لكل إجراء سياسته `HrManagement.Approve` أو `HrCore.Approve` أو `SoD.ManageDelegations` في كل التجميعات: المسار التقليدي، وسياسة البوابة الحالية له، وهل يصله `hr-management-approver` وحده | جدول في `reports/plan50-baseline.md` يطابق G-IAM-01 (5 حركات + 9 + 2) |
| 0.5 | جرد التفويض | كل قراءة لـ `ApprovalDelegations` و`WorkflowDelegations`، وكل `new SeparationOfDutiesService(` | يطابق الدراسة: قارئ واحد (`DelegationsController`)، و`WorkflowDelegation` بلا مستخدم |
| 0.6 | ما غيّره PLAN-37..49 | إعادة قراءة `GatewayPathAuthorizationMiddleware` و`HrModuleAuthorizationFilter` و`WorkflowInstanceActionService` بعد خطط الموجة السابقة (PLAN-40 يغيّر ترتيب الحفظ والنشر، وPLAN-43/44 يمسّان المحرك) | الملاحظات في `reports/plan50-baseline.md` |

---

## In scope

### 1. سمة صريحة لتجاوز سياسة الوحدة في إجراء بعينه

**المشكلة:** فلتر الوحدة يطبّق سياسة الوحدة على كل إجراء، ولا يُستثنى منه إلا بقائمة أسماء متحكمات ([HrModuleAuthorizationFilter.cs:27-46](../../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs)). وكل HR.Movements خلف `Module.HrMovements` = Admin أو `hr-movements` ([HR.Movements.Web/Program.cs:17-18](../../src/Services/HRMovements/HR.Movements.Web/Program.cs)، [HrManagementAuthorizationExtensions.cs:39, 194-200](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `ModulePolicyOverrideAttribute(string policy)` | `src/Shared/HRManagement.ServiceDefaults/ModulePolicyOverrideAttribute.cs` | على إجراء أو متحكم: الفلتر يقيّم هذه السياسة **بدل** سياسة الوحدة. ويبقى `[Authorize]` الخاص بالإجراء نافذاً فوقها |
| `HrModuleAuthorizationFilter` | الملف نفسه | قبل قائمة الأسماء: إن وُجدت السمة يقيّم سياستها ويخرج. قائمة الأسماء تبقى كما هي هنا (يستبدلها PLAN-51 بالسمة) |

**القواعد:**
1. السمة لا تُوسّع شيئاً بنفسها. هي تستبدل سياسة الوحدة بسياسة مسماة في السمة، ويجب أن تكون السياسة معرّفة (فشل الإقلاع إن لم تكن، عبر `IAuthorizationPolicyProvider`).
2. لا تُستعمل السمة على إجراءات الإنشاء والتعديل والتنفيذ في هذه الخطة.

### 2. وصول المعتمد إلى اعتمادات الحركات، والمنشئ لا يعتمد (G-IAM-01، G-MOV-01)

**المشكلة:**
- `TransferRequests.Approve/Reject` بسياسة `HrManagement.Approve` = Admin أو `hr-management-approver` ([TransferRequestsController.cs:183-186, 251-254](../../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)، [HrManagementAuthorizationExtensions.cs:149-151](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). ومثلها `Approve` في الندب والإعارة والترفيع والتكليف ([MovementsExtendedControllers.cs:333-336, 679-682, 949-952, 1113-1116](../../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
- لكن الفلتر والبوابة يطلبان `Module.HrMovements` قبلها ([GatewayPathAuthorizationMiddleware.cs:22](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). فلا يعتمد إلا `hr-admin`. والشخصية `a-management-approver` تحمل `hr-management-approver` وحده ([personas.json:69-83](../../testdata/acceptance/personas.json)).
- `TransferRequests.Execute` بلا سياسة صريحة ([TransferRequestsController.cs:283-286](../../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| سياسة `HrMovements.ApprovalAccess` | `HrManagementAuthorizationExtensions.cs` + ثابت في `HrManagementPolicies` | Admin \| `hr-movements` \| `hr-management-approver` \| مفوَّض نافذ بدور `hr-management-approver` (البند 5) |
| سياسة `HrMovements.Write` | ↑ | Admin \| `hr-movements` (السلوك الحالي، لكن صريحاً) |
| المتحكمات الخمسة | `TransferRequestsController.cs`، و`SecondmentRequests`/`LoanAssignmentRequests`/`PromotionRequests`/`DelegationRequests` في `MovementsExtendedControllers.cs` | `Index` و`Details` و`Approve` (و`Reject` في النقل): `[ModulePolicyOverride(HrMovements.ApprovalAccess)]`، و`Approve`/`Reject` تبقي `HrManagement.Approve` فوقها. كل إجراء آخر في المتحكمات الخمسة: `[Authorize(Policy = HrMovements.Write)]` صريحاً |
| قائمة المعتمد | `Index` في المتحكمات الخمسة | لمن لا يحمل `hr-movements`: المرشح الافتراضي «بانتظار الاعتماد»، وأزرار الإنشاء والتنفيذ مخفية |
| قاعدة البوابة | `GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy` | لمقطع `hr-movements`: المسارات `/{TransferRequests\|SecondmentRequests\|LoanAssignmentRequests\|PromotionRequests\|DelegationRequests}/{Index\|Details\|Approve\|Reject}` (وجذر المتحكم = `Index`) والملفات الثابتة لهذا المقطع ← `Gateway.ApprovalReach` (البند 4). أي مسار آخر يبقى `Module.HrMovements` |
| واجهة | `Views/*Requests/Details.cshtml` | زر الاعتماد لمن يملكه فقط (`IAuthorizationService` بالسياسة نفسها)، وأزرار التنفيذ والإلغاء لـ `HrMovements.Write` فقط |

**القواعد:**
1. المطابقة في البوابة على المقطعين بعد بادئة الوحدة **بالضبط** (اسم المتحكم ثم الإجراء)، لا بـ `Contains`.
2. فحوص «المنشئ ≠ المعتمد» الحالية تبقى ([TransferRequestsController.cs:205-216](../../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)، ومثلها في الأنواع الأربعة)، وتُضاف إليها قاعدة الأصيل في البند 6.
3. فحص نطاق النقل `EnsureTransferInScopeAsync` يبقى على كل الإجراءات ([TransferRequestsController.cs:37-45](../../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)).
4. `a-movements` يُرفض عند الاعتماد (403 من `HrManagement.Approve`)، و`a-management-approver` يُرفض عند الإنشاء والتنفيذ (403 من `HrMovements.Write`).

### 3. وصول المعتمد إلى إجراءات `HrCore.Approve` والتفويض في البوابة (G-IAM-01)

**المشكلة:**
- البوابة تفرض `HrCore.Write` (Admin، `hr-core`) على كل POST في HR.Core عدا مسارات الاعتماد المسماة ([GatewayPathAuthorizationMiddleware.cs:180-192, 294-298](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). فلا يصل المعتمد إلى:
  - `EmployeeCorrections.Approve/ApproveApi` ([EmployeeCorrectionsController.cs:131-134, 173-176](../../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeCorrectionsController.cs))، والمتحكم نفسه بسياسة `HrCore.Write` على الصنف (`:18`)، فيُرفض المعتمد في الخدمة أيضاً؛
  - `EmploymentRecords.Approve` ([EmploymentRecordsController.cs:187-190](../../src/Services/HRCore/HR.Core.Web/Controllers/EmploymentRecordsController.cs))؛
  - `Competitions.Finalize/Cancel` ([CompetitionsController.cs:450-453, 774-777](../../src/Services/HRCore/HR.Core.Web/Controllers/CompetitionsController.cs))؛
  - `PolicyTemplates.PublishFromUi` و`Policies.Publish` ([PolicyTemplatesController.cs:171-174](../../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)، [PoliciesController.cs:101-103](../../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)). بعد PLAN-47 صارت سياستهما وقاعدة بوابتهما `PolicyApprover` (تشمل `hr-management-approver`)، فلا يُعاد عملهما هنا ويُسجَّلان في الجدول بسياستهما؛
  - `Approvals.Approve/Reject` ([ApprovalsController.cs:102-105, 151-154](../../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs)).
- ولا إلى `Delegations.Create/Revoke` ([DelegationsController.cs:13, 74-76, 110-112](../../src/Services/HRCore/HR.Core.Web/Controllers/DelegationsController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `GatewayHrCoreActionPolicies` | `src/Shared/HRManagement.ServiceDefaults/GatewayHrCoreActionPolicies.cs` | جدول بيانات: (متحكم، إجراء) ومسارات الـ API المقابلة ← السياسة. الإجراءات السبعة ← `HrCore.Approve`، وإجراءا النشر ← `PolicyApprover` (PLAN-47، تُنقل قاعدتها إلى الجدول بلا تغيير)، و`Delegations/Create` و`Delegations/Revoke` ← `SoD.ManageDelegations`. `TryGetFineGrainedPolicy` يستشيره قبل قاعدة `HrCore.Write` |
| `EmployeeCorrectionsController` | `HR.Core.Web/Controllers/` | الصنف `HrCore.ReadOnly`، و`RequestCorrection`/`RequestCorrectionApi` بـ `HrCore.Write` صريحاً، و`Approve`/`ApproveApi` بـ `HrCore.Approve` وحدها |
| اختبار اتساق | `GatewayApproverReachTests` في `src/Tests/HRManagement.UnitTests/Architecture/` | بالانعكاس على HR.Core.Web وHR.Movements.Web (يُضاف مرجع `HR.Movements.Web` إلى مشروع الاختبار): لكل إجراء سياسته `HrCore.Approve` أو `PolicyApprover` أو `HrManagement.Approve` أو `SoD.ManageDelegations` أو `HrMovements.ApprovalAccess`، تعيد البوابة لمساره التقليدي سياسة يجتازها مبدأ يحمل `hr-management-approver` وحده. ومبدأ `hr-core-supervisory` وحده يُرفض على مسارات POST نفسها |

**القواعد:**
1. الجدول هو المصدر الوحيد لاستثناءات البوابة في HR.Core. أي إجراء جديد بسياسة اعتماد بلا صف في الجدول يُفشل الاختبار.
2. الاستثناء يطابق المسار التقليدي `/ui/hr-core/{Controller}/{Action}[/{id}]` ومسار الـ API الصريح فقط.
3. `Competitions.Finalize/Cancel` تبقى مركزية: نطاق التوظيف في `RecruitmentOrgScopeFilter` لا يتغير.

### 4. سياسة «وصول الاعتماد» في البوابة لمسارات الاعتماد المسماة

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| سياسة `Gateway.ApprovalReach` | `HrManagementAuthorizationExtensions.cs` | مستخدم تفاعلي (ليس حساب خدمة) يحمل `tenant_id` صالحاً وأي دور غير `hr-employee` و`hr-exam-marker` و`applicant`. القائمة من `Authorization:NonStaffRoles` |
| قاعدة البوابة لمسارات اعتماد المسارات في HR.Core | `TryGetFineGrainedPolicy` | `POST .../ApproveWorkflow\|RejectWorkflow\|ApproveResultsWorkflow\|RejectResultsWorkflow` (ومنها `/ui/hr-core/WorkflowApprovals/*` من PLAN-45) و`/api/workflow-instances/{id}/approve\|reject` ← `Gateway.ApprovalReach` بدل `HrCore.BranchOperation`/`WorkflowStepAction` (PLAN-43/45)، لأن المفوَّض قد لا يحمل أدوار العمليات (D-50-3)، ولأن حملة `admin-logistics`/`idcard`/`compliance` (PLAN-45/46) لا يحملون `HrCore.ReadOnly`. الخدمة تقرر بـ `WorkflowStepAction` (البند 6) |

**القواعد:**
1. البوابة طبقة خشنة لهذه المسارات فقط. كل إجراء فيها يحمل سياسة صريحة في الخدمة، والاختبار في البند 3 يتحقق من ذلك.
2. لا تُفتح بهذه السياسة أي مسارات أخرى.

### 5. التفويض: كيان واحد، وإنشاء مقيّد، واستعلام عن التفويض النافذ (G-WF-11)

**المشكلة:**
- `Delegations.Create` يحفظ `ApprovalDelegation` بمعرّف مفوَّض يُكتب يدوياً، و`RoleScope` اختياري ([DelegationsController.cs:74-108](../../src/Services/HRCore/HR.Core.Web/Controllers/DelegationsController.cs)، [ApprovalDelegation.cs:9-38](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/ApprovalDelegation.cs)). و`Index` يعرض كل تفويضات الجهة لكل من يملك السياسة (`:26-61`).
- `ResolveDelegate` يتجاهل `RoleScope` ولا مستدعي له ([SeparationOfDutiesService.cs:158-178](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SeparationOfDutiesService.cs)). وكل `new SeparationOfDutiesService()` يُبنى بلا تفويضات (`:13-21`).
- `WorkflowDelegation` معرَّف بلا مستخدم ([WorkflowDelegation.cs:9](../../src/Services/HRCore/HR.Core.Domain/Entities/Workflow/WorkflowDelegation.cs)، [HRCoreDbContext.cs:79, 884](../../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| حقول جديدة على `ApprovalDelegation` | `ApprovalDelegation.cs` | `DelegateEmployeeId` (Guid?)، و`DelegateName`، و`PrincipalEmployeeId`، و`PrincipalName`، و`RevokedReason`. `DelegateId` يصبح nullable (للصفوف القديمة). و`RoleScope` إلزامي للصفوف الجديدة |
| `ApprovalDelegationMatcher` | `src/Shared/HRManagement.SharedKernel/SeparationOfDuties/ApprovalDelegationMatcher.cs` | دالة نقية: من قائمة تفويضات وفاعل (`userId`، `employeeId`) ودور ونوع كيان ووقت ← التفويض المطابق أو لا شيء. تحترم `RoleScope` و`EntityTypeScope` والمدة والإلغاء. ويُعاد بناء `ResolveDelegate`/`IsActingAsDelegate` فوقها |
| `ValidateDelegation` | `SeparationOfDutiesService.cs:180-199` | إضافة: الدور ضمن `Delegation:DelegableRoles`، ومن أدوار الأصيل المباشرة (تُمرَّر من المتحكم)، و`EntityTypeScope` من `Delegation:EntityTypes`، والمفوَّض ≠ الأصيل بالمستخدم وبالموظف |
| `DelegationsController` | `HR.Core.Web/Controllers/` | `Create`: اختيار المفوَّض من منتقي الموظفين (`/api/lookup/employees`)، والدور من قائمة أدوار المستخدم القابلة للتفويض فقط. `Index`: ما أنشأه المستخدم وما فُوِّض إليه، والكل لـ Admin و`platform-admin` فقط. `Revoke` كما هو مع `RevokedReason` |
| ترحيل HR.Core | `HCM050_ApprovalDelegationScope` | الحقول الجديدة؛ إلغاء الصفوف وفق D-50-4؛ حذف جدول `WorkflowDelegations` وكيانه وإعداده |
| `IApprovalDelegationLookup` | `src/Shared/HRManagement.ServiceDefaults/Delegation/IApprovalDelegationLookup.cs` | `GetActiveForAsync(ClaimsPrincipal user, CancellationToken)` ← قائمة `ActiveDelegation(Id, PrincipalUserId, PrincipalEmployeeId, PrincipalName, Role, EntityTypeScope, ActiveUntil)` |
| `DbApprovalDelegationLookup` | `HR.Core.Infrastructure/Services/Delegation/` | من القاعدة مباشرة، بلا تخزين مؤقت |
| نقطة داخلية | `HR.Core.Web/Controllers/ApprovalDelegationsInternalController.cs` (`GET api/internal/approval-delegations/active?userId=&employeeId=`) | سياسة جديدة `Service.ApprovalDelegationRead` (ثابت في `ServicePolicies`، فيجتاز `S2s042InternalRoutesAreServiceOnlyTests` من PLAN-42): حساب خدمة بنطاق `hr-core.lookup` وعميل من قائمة (`svc-hr-movements`). **لا** تقبل المستخدم التفاعلي |
| `HttpApprovalDelegationLookup` | `src/Shared/HRManagement.ServiceDefaults/Delegation/` | للخدمات الأخرى: عميل S2S بنمط `HrCoreLookupClient` (رمز خدمة بنطاق `hr-core.lookup`؛ لا توقيع إثبات بوابة بعد PLAN-42)، تخزين مؤقت (D-50-5)، وfail-closed. ومدخل `calls[]` له في `deploy/wiring/service-wiring.json` (PLAN-37) بالسياسة `Service.ApprovalDelegationRead`، فيبقى `ProductionWiringMatrixTests` أخضر |
| تصحيح خاصية الموظف | [KeycloakUserProvisioningService.cs:74](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs) | `employeeId` ← `employee_id`، الاسم الذي يقرؤه مُخطِّط المطالبة في realm، فيحمل رمز المستخدم الذي ينشئه Auth.Directory المطالبة (D-50-2). اختبار وحدة على حمولة الإنشاء |
| `RoleOrDelegationRequirement` + معالجه | `src/Shared/HRManagement.ServiceDefaults/Delegation/` | ينجح إن حمل المستخدم أحد الأدوار، أو كان له تفويض نافذ بالدور المفوَّض يطابق `[ApprovalEntityType("...")]` على الإجراء إن وُجدت. ويضع التفويض المطابق في `HttpContext.Items`. بلا `IApprovalDelegationLookup` مسجّل = أدوار فقط |
| `IApprovalActingContext` | ↑ | يعيد `(UserId, EmployeeId, OnBehalfOf: ActiveDelegation?)`. الدور المباشر يغلب: `OnBehalfOf = null` إن حمل المستخدم الدور بنفسه |

**القواعد:**
1. لا تفويض متسلسل: `RoleScope` يُتحقق منه مقابل مطالبات الأصيل الأصلية، والدور المفوَّض لا يظهر في المطالبات أصلاً.
2. التفويض لا يمنح دوراً في الرمز ولا في `IsInRole`. أثره فقط في نقاط القرار المسماة في البندين 6 و7.
3. `HrManagement.Approve` تصبح `RoleOrDelegationRequirement([Admin, hr-management-approver], delegated: hr-management-approver)`. في HR.Contracts لا يُسجَّل `IApprovalDelegationLookup`، فيبقى سلوكها كما هو.
4. كل إنشاء وإلغاء يُكتب في `EntityAuditLog`.
5. تصحيح اسم الخاصية `employee_id` في Auth.Directory يُنفَّذ هنا لأنه شرط D-50-2، ولا يتوقف على تفعيل المزامنة (PLAN-62 يفعّلها ويستعمله).

### 6. أثر التفويض في محرك المسارات (HR.Core)

**المشكلة:** المقيِّم يفحص دور الخطوة على أدوار الفاعل فقط ([WorkflowApprovalEvaluator.cs:37-40](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/WorkflowApprovalEvaluator.cs)، [WorkflowStageGuard.cs:113-130](../../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs)). والصندوق يحسب الأهلية بالمبدأ نفسه ([WorkflowInboxEligibilityService.cs:14-47](../../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxEligibilityService.cs)). وسجل الخطوة لا يعرف «بالنيابة عن» ([WorkflowStepHistoryEntry.cs:6-15](../../src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStepHistoryEntry.cs)). و`DelegateItemAsync` يرمي `NotSupportedException` ([HrCoreWorkInboxService.cs:35-36](../../src/Services/HRCore/HR.Core.Web/Services/HrCoreWorkInboxService.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `WorkflowActionActor.Delegations` | [WorkflowActionActor.cs:11-49](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowActionActor.cs) | قائمة `ActiveDelegation` للفاعل، تُملأ في `FromPrincipalAsync(user, lookup)` الجديدة |
| المقيِّم | `WorkflowApprovalEvaluator.GetBlockReasonAsync` | إن فشل الدور المباشر: يبحث عن تفويض للدور `step.RoleRequired` ونوع `instance.EntityType`. إن وُجد: يمرّ فحص الدور، ثم تُشغَّل البوابة وقاعدة مُطلق المسار **مرتين**: بهوية المفوَّض وبهوية الأصيل. أي رفض في أي منهما يُعاد. ويعيد المقيِّم `OnBehalfOf` للمستدعي |
| `WorkflowStepHistoryEntry` | `SharedKernel/Workflow/` | `OnBehalfOfId`، `OnBehalfOfName`، `DelegationId` (اختيارية؛ JSON متوافق مع السجلات القديمة) |
| `WorkflowInstanceActionService` | `ApproveAsync`/`RejectAsync` ([WorkflowInstanceActionService.cs:72-123, 125-169](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)) | يمرّر `OnBehalfOf` إلى سجل الخطوة، وإلى `RecordStepActionAsync` (الحمولة الموقّعة تتضمن `onBehalfOfUserId` و`delegationId`، و`SignerRole` = الدور المفوَّض) |
| سياسة `WorkflowStepAction` (PLAN-43، وسّعتها PLAN-45/46) | `HrManagementAuthorizationExtensions.cs` | تصبح `RoleOrDelegationRequirement(أدوارها الحالية، delegated: أي دور قابل للتفويض)`، بلا حسابات خدمة كما هي. وتُطبَّق أيضاً على `ApproveWorkflow`/`RejectWorkflow` في المتحكمات الخمسة بدل `HrCore.BranchOperation` (و`api/workflow-instances/{id}/approve\|reject` و`WorkflowApprovals` تحملها منذ PLAN-43/45). لا سياسة موازية جديدة. المقيِّم هو القرار الدقيق |
| الصندوق ولوحات التفاصيل | `WorkflowInboxEligibilityService`، `WorkflowPanelBuilder` | يبنيان الفاعل بتفويضاته، فيظهر عنصر الأصيل للمفوَّض بشارة «بالنيابة عن {الأصيل}»، ويُحسب `CanApprove` بالمقيِّم نفسه |
| `DelegateItemAsync` | `HrCoreWorkInboxService.cs` | يبقى غير مدعوم (إعادة إسناد عنصر بعينه ليست تفويضاً)، برسالة تشير إلى شاشة التفويض |

**القواعد:**
1. ترتيب المقيِّم لا يتغير: البنية ← الدور (مباشر ثم مفوَّض) ← البوابة ← مُطلق المسار.
2. الرفض يمر بالقواعد نفسها، ويُسجَّل «بالنيابة عن» أيضاً.
3. أنواع الكيانات في `Workflow:StepRole:ExemptEntityTypes` (PLAN-43؛ فارغة منذ PLAN-45) لا يُفحص دور خطوتها أصلاً، فلا أثر للتفويض فيها، ولا يتغير سلوكها.

### 7. أثر التفويض في اعتمادات الحركات (HR.Movements)

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| تسجيل | `HR.Movements.Web/Program.cs` | `IApprovalDelegationLookup` = `HttpApprovalDelegationLookup`، و`IApprovalActingContext` |
| `[ApprovalEntityType]` | `Approve`/`Reject` في المتحكمات الخمسة | `TransferRequest`، `SecondmentRequest`، `LoanAssignmentRequest`، `PromotionRequest`، `DelegationRequest` |
| فصل المهام | الإجراءات نفسها | يُرفض إن كان `RequestedBy` = المفوَّض أو = الأصيل، برسالة «لا يجوز اعتماد طلب أنشأه المعتمد أو أصيله» |
| «بالنيابة عن» | `TransferApproval` و`MovementChecklist` | عمودان `OnBehalfOfUserId` و`OnBehalfOfName` يُملآن عند الاعتماد بالتفويض. ترحيل `MOV050_ApprovalOnBehalfOf` |
| العرض | `Details.cshtml` للأنواع الخمسة | «اعتمده فلان بالنيابة عن فلان» |

**القواعد:**
1. فشل الاستعلام عن التفويض لا يمنع صاحب الدور المباشر، ويمنع المفوَّض (fail-closed).
2. لا تُغيَّر قواعد الحالة والتاريخ والنظام الحالية في `Approve`.

---

## Out of scope

- استبدال قائمة الأسماء في فلتر الوحدة بالسمة في كل الخدمات، وسياسات كتابة صريحة لبقية متحكمات الحركات (النشرات، خطط التوزيع، سجلات التنفيذ، الآثار المالية، التدخلات) ← **PLAN-51** (G-IAM-08).
- قصر `/ui/ess` على مسارات الخدمة الذاتية (الطريق الجانبي الذي يوصل `hr-employee` + دور HR.Core إلى الإجراءات نفسها) ← **PLAN-51** (G-IAM-05).
- عناوين الخدمات ونطاقاتها وأسرارها في الإنتاج (عميل HR.Movements ← HR.Core) ← **PLAN-37**. توقيع الإثبات وفشل الإغلاق عبر الخدمات ← **PLAN-42**.
- تفعيل مزامنة Keycloak في الإنتاج وتزويد المستخدم عند التعيين ← **PLAN-62** (تصحيح اسم الخاصية `employee_id` نفسه هنا، البند 5). وإلغاء تفويضات المغادر ← الموجة 2 (G-SEP-17، كما في PLAN-63).
- تفويض أدوار أخرى (معتمد الرواتب، العقود، الإجازة)، والتفويض في `HireWizard.Step3Approve` والموافقات القديمة (`Approvals`)، والغياب الآلي، والتصعيد، والإشعار ← الموجة 2: **WF-F** (G-WF-11 الباقي، G-WF-14).
- رفض الندب والإعارة والترفيع والتكليف: تعداداتها بلا حالة `Rejected`، ويبقى الإلغاء بيد المنشئ ← الموجة 2 (MOV).
- «التعديل ثم الاعتماد يتجاوز فصل المهام» (G-MOV-11)، ونطاق الفرع في الحركات (G-MOV-10) ← الموجة 2 (MOV) وPLAN-52 للمرشح العام.

## Acceptance scenarios

على الملف الشبيه بالإنتاج (PLAN-37)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، بالفئة `AcIam50ApproverReachDelegationTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-IAM-50")`). المفوَّض في السيناريوهات هو `central-a2` (`hr-core`، بلا دور اعتماد).

| ID | Positive | Negative |
|----|----------|----------|
| AC-IAM-50a (يغطي AC-MOV-Aa) | `a-movements` ينشئ نقلاً ← `a-management-approver` يفتح القائمة والتفاصيل عبر البوابة (200) ويعتمد ← `Approved`، وصف `TransferApproval` باسمه. ومثله ترفيع واحد | `a-management-approver`: POST `TransferRequests/Create` وPOST `Execute` ← 403 بلا تغيير. `a-movements` يعتمد نقلاً أنشأه غيره ← 403 (دور المنشئ لا يعتمد) |
| AC-IAM-50b | `a-management-approver` يعتمد `EmploymentRecords.Approve` و`EmployeeCorrections.Approve` عبر البوابة (الحالة تتغير) | `a-supervisory-reader` على المسارين نفسيهما ← 403. وطلب التصحيح الذي أنشأه `central-a` لا يعتمده `central-a` إن كان قانونياً (القاعدة الحالية) |
| AC-IAM-50c | `a-management-approver` ينشئ تفويضاً لـ `central-a2` بدور `hr-management-approver` ونوع `RecruitmentRequisition` لسبعة أيام ← يظهر في «تفويضاتي» للطرفين | تفويض بدور `hr-core` لا يحمله الأصيل ← 400. تفويض لـ 120 يوماً ← 400. `central-a2` ينشئ تفويضاً بالدور المفوَّض إليه ← 400 (لا تسلسل) |
| AC-IAM-50d | طلب شاغر أنشأه `central-a` واعتمد `br1-mgr` خطوته الأولى ← يظهر في صندوق `central-a2` بشارة «بالنيابة عن» ← يعتمد الخطوة 2 ← `Approved`، وسجل الخطوة والتوقيع يحملان `OnBehalfOfId` = الأصيل | بعد إلغاء التفويض ← اعتماد طلب آخر يُرفض بـ «هذه الخطوة تتطلب دور «hr-management-approver»» ولا تتغير الحالة. طلب أنشأه `central-a2` نفسه ← رفض فصل المهام حتى مع التفويض |
| AC-IAM-50e | تفويض بنوع `TransferRequest` ← `central-a2` يعتمد نقلاً عبر البوابة ← `TransferApproval.OnBehalfOfUserId` = الأصيل | تفويض بنوع `RecruitmentRequisition` فقط ← اعتماد النقل 403. وبعد الإلغاء وانقضاء 60 ثانية ← 403 |

وخارج القبول الحي:
- اختبار تكامل على PostgreSQL (Testcontainers) `Iam050DelegationPostgreSqlTests`: الترحيل `HCM050` على قاعدة فيها صفوف قديمة (بلا `RoleScope`، وبـ `WorkflowDelegations`) ← تُلغى وفق D-50-4، ويُحذف الجدول.
- اختبار تكامل في المضيف داخل العملية (نمط `Hcm025ScopeAndPolicyTestHost`): `api/internal/approval-delegations/active` يرفض المستخدم التفاعلي (403) ويقبل حساب `svc-hr-movements`.
- اختبارات وحدة لـ `ApprovalDelegationMatcher` و`ValidateDelegation` والمقيِّم (مفوَّض، أصيل منشئ، مفوَّض منشئ، انتهاء، نوع مختلف).

## Verification

```powershell
dotnet build HRManagement.slnx
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Iam050|FullyQualifiedName~GatewayApproverReach|FullyQualifiedName~GatewayPathAuthorization|FullyQualifiedName~SoD|FullyQualifiedName~WorkflowStageGuard|FullyQualifiedName~Hcm025"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Iam050|FullyQualifiedName~Hcm025"
dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web
dotnet ef migrations has-pending-model-changes -p src/Services/HRMovements/HR.Movements.Infrastructure -s src/Services/HRMovements/HR.Movements.Web
# قبول على الملف الشبيه بالإنتاج (أوامر PLAN-37)، ثم:
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --no-deps --force-recreate hr-core-web hr-movements-web hr-gateway
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcIam50|FullyQualifiedName~AcHcm25"
pwsh scripts/iam050-validate.ps1
```

## Definition of done

- `hr-management-approver` وحده يعتمد الأنواع الخمسة من الحركات والإجراءات التسعة عبر البوابة، ولا ينشئ ولا ينفّذ. و`hr-movements` لا يعتمد.
- `GatewayApproverReachTests` أخضر: لا إجراء اعتماد في HR.Core أو HR.Movements بلا مسار بوابة يصله المعتمد.
- التفويض النافذ يعمل في المحرك والحركات، ويُسجَّل «بالنيابة عن» في السجل والتوقيع. وينتهي أثره بالإلغاء والانتهاء، ولا يتجاوز فصل المهام.
- لا كيان `WorkflowDelegation`، ولا تفويض بلا `RoleScope`.
- AC-IAM-50a..e = Pass في `acceptance-evidence/AC-IAM-50/` بعناصر الدليل الثمانية، وانحدار AC-HCM-25 أخضر.
- سطر «الحالة» تحت G-IAM-01 في الدراسة، وتحت G-MOV-01 في دراسة الحركات، محدّث.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-50-iam-approver-reach-delegation.md` |
| ServiceDefaults | `ModulePolicyOverrideAttribute.cs`، `GatewayHrCoreActionPolicies.cs`، `Delegation/*` (`IApprovalDelegationLookup`، `HttpApprovalDelegationLookup`، `RoleOrDelegationRequirement`، `IApprovalActingContext`)، تعديل `HrModuleAuthorizationFilter.cs` و`GatewayPathAuthorizationMiddleware.cs` و`HrManagementAuthorizationExtensions.cs` و`ServiceAuthorizationExtensions.cs` |
| SharedKernel | `ApprovalDelegation.cs` (حقول)، `ApprovalDelegationMatcher.cs`، `SeparationOfDutiesService.cs`، `WorkflowStepHistoryEntry.cs` |
| HR.Core | `DbApprovalDelegationLookup.cs`، `ApprovalDelegationsInternalController.cs`، `DelegationsController.cs` وواجهاته، `WorkflowActionActor.cs`، `WorkflowApprovalEvaluator.cs`، `WorkflowInstanceActionService.cs`، `WorkflowSignatureService.cs`، `WorkflowInboxEligibilityService.cs`، `EmployeeCorrectionsController.cs`، متحكمات `ApproveWorkflow` الخمسة و`WorkflowInstancesController.cs` |
| HR.Movements | `TransferRequestsController.cs`، `MovementsExtendedControllers.cs`، `TransferApproval.cs`، `MovementChecklist.cs`، `Program.cs`، واجهات `Index`/`Details` للأنواع الخمسة |
| Migrations | `HCM050_ApprovalDelegationScope` (HR.Core)، `MOV050_ApprovalOnBehalfOf` (HR.Movements) |
| Auth.Directory | `KeycloakUserProvisioningService.cs` (اسم الخاصية `employee_id`) واختبار وحدته |
| Config | `Delegation:DelegableRoles`، `Delegation:EntityTypes`، `Delegation:CacheSeconds`، `Authorization:NonStaffRoles` في `appsettings.json` للخدمتين والبوابة، ومرآتها في `docker-compose.acceptance-prodlike.yml`؛ ومدخل `calls[]` لاستعلام التفويض في `deploy/wiring/service-wiring.json` (PLAN-37) |
| Unit | `Iam050DelegationMatcherTests.cs`، `Iam050EvaluatorDelegationTests.cs`، `Iam050MovementApprovalPolicyTests.cs`، `GatewayApproverReachTests.cs`، تحديث `GatewayPathAuthorizationMiddlewareTests.cs` |
| Integration | `Iam050DelegationPostgreSqlTests.cs`، `Iam050DelegationInternalApiTests.cs` |
| Acceptance | `AcIam50ApproverReachDelegationTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، وفق قاعدة PLAN-41)؛ تحديث `scenarios` للشخصيات `a-management-approver` و`central-a2` و`a-movements` في `testdata/acceptance/personas.json` |
| Scripts | `scripts/iam050-validate.ps1` |
| Docs | `docs/SOD_MATRIX.md` (التفويض: الأدوار والقيود)، `docs/WF_001_WORKFLOW_ENGINE.md` و`docs/WF_002_WORK_INBOX.md` (أثر التفويض فعلياً)، `docs/HIRE_RBAC_MATRIX.md` (وصول المعتمد)، `docs/USER_GUIDE_AR.md` (شاشة التفويض)، أسطر «الحالة» في `docs/IAM_001_SCOPE_GAP_STUDY.md` (G-IAM-01) و`docs/FUN_003_MOVEMENTS_GAP_STUDY.md` (G-MOV-01) و`docs/WF_001_GAP_STUDY.md` (G-WF-11: «جزئي — الأثر في المحرك والحركات») |

## Risks

| الخطر | التخفيف |
|-------|---------|
| فتح البوابة لمسارات الاعتماد يعتمد على صحة سياسة الخدمة | `GatewayApproverReachTests` يفشل إن وُجد إجراء في المتحكمات المفتوحة بلا سياسة صريحة. وPLAN-51 يضيف السقف العام |
| تغيير `HrManagement.Approve` يمس `HireWizard.Step3Approve` في HR.Contracts | المعالج لا يعمل بلا `IApprovalDelegationLookup` مسجّل، فيبقى السلوك أدواراً فقط. اختبار وحدة يثبت ذلك |
| الاستعلام S2S عن التفويض يبطئ صفحات الحركات | يُستدعى فقط حين يفشل الدور المباشر، ومخزّن 60 ثانية |
| `employee_id` غائب عن مستخدمي الإنتاج الذين ينشئهم Auth.Directory | الإصلاح الجذري هنا (البند 5: اسم الخاصية). والمطابقة الاحتياطية بـ `sub` للصفوف القديمة التي تحمل `DelegateId` (مذكورة في `SOD_MATRIX.md`) |
| تعارض مع تغييرات PLAN-40/43/44 في `WorkflowInstanceActionService` | المرحلة 0.6. والتغيير هنا إضافي (حقول السجل والتوقيع)، لا يمس ترتيب الحفظ والنشر |

## Effort estimate

9–10 أيام عمل: المرحلة 0 (0.5)، السمة ووصول المعتمد في الحركات (1.5)، استثناءات البوابة في HR.Core واختبار الاتساق (1)، كيان التفويض وإنشاؤه والنقطة الداخلية والعميل (2)، الأثر في المحرك والصندوق والتوقيع (2)، الأثر في الحركات (1)، القبول والتكامل والوثائق (1.5).
