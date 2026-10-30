# PLAN-45 — إغلاق حلقة اعتماد العهدة والفقدان (WF-B)

**Scope:** WF-001 / FUN-005 — [دراسة الفجوات](../WF_001_GAP_STUDY.md): G-WF-02 (+ G-IDA-02 في [FUN_005_IDENTITY_ASSETS_IT_GAP_STUDY.md](../FUN_005_IDENTITY_ASSETS_IT_GAP_STUDY.md))  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-40 (نشر النتيجة قبل آخر حفظ، G-WF-03، وحارسه المعماري)، PLAN-43 (البدء الخارجي المقيّد، وأحداث العهدة v2 بمعرّف النسخة، وفرض الدور افتراضياً، و`EnsureVersionAsync`)  
**Related cards:** FUN-005، FUN-005-EXT، WF-001، WF-002، PLAN-03 (AC-11)، PLAN-12 (AC-33)

## Goal

أن يكتمل اعتماد تسليم العهدة المميزة وفقدان الأصل **من الواجهة وبإعدادات الإنتاج**:

- لا يُحفظ طلب `PendingApproval` بلا نسخة. وفشل HR.Core لا يجمّد الأصل: يظهر الطلب «بانتظار بدء الاعتماد» ويُعاد بدؤه آلياً.
- لصاحب الطلب أن يسحبه فيتحرر الأصل.
- يرى مسؤول الأصول والمعتمد الإداري الطلب في صندوق العمل، ويعتمدان من صفحة اعتماد عامة في HR.Core.
- السلسلة وفق D-WF-2: الخطوة 1 لـ `admin-logistics` (غير الطالب)، والخطوة 2 لـ `hr-management-approver`.
- النتيجة تصل إلى AdminLogistics (بعد PLAN-40) فيُفعَّل التسليم أو يُثبَّت الفقدان.

صفحة الاعتماد العامة وتعميم الصندوق للأنواع الخارجية تُبنى هنا، ويعيد PLAN-46 استعمالهما للبطاقة والتأديب والفصل.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-WF-2 / D-IDA-4 سلسلة العهدة والفقدان | `ASSET-ASSIGN-APPROVAL` و`ASSET-LOSS-APPROVAL` بإصدار جديد بخطوتين: 1 «موافقة مسؤول الأصول» `admin-logistics`، و2 «اعتماد إداري» `hr-management-approver`، مع `RequireDistinctApprovers=true`. الإصدارات الأقدم تُسحب، والنسخ الجارية تكمل على إصدارها المثبّت | §4 |
| متى يلزم الاعتماد | دون تغيير: قرار السياسة الحالي (الأصل المميز أو قيمته فوق 10,000، [AssetsController.cs:422-423](../../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)، و`AssetLostRules.RequiresLossApproval`). العتبة نفسها ← الموجة 2 (D-WF-3) | — |
| إعادة البدء الآلية | كل `AssetApproval:Retry:IntervalSeconds=120`، حتى `MaxAttempts=30`، ثم وسم «يتطلب تدخلاً» ومقياس. في ملف القبول `IntervalSeconds=15` | §1 |
| من يسحب الطلب | الطالب، أو أي `admin-logistics` في نطاق الأصل بسبب إلزامي | §1 |
| الطلبات المعلّقة القائمة | سكربت يحوّل `PendingApproval` بلا نسخة إلى `ApprovalStarting` فتلتقطها إعادة البدء. ولا يمس النسخ الجارية على الإصدارات القديمة إلا بخيار صريح | §5 |

كل افتراض خلف إعداد في `Admin.Logistics.Web/appsettings*.json` (قسم `AssetApproval`) أو في بذرة مختبَرة.

---

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-45-wf-asset-custody-approval-loop` | — |
| 0.2 | التحقق من التبعيات | PLAN-40: الحارس `OutboxPublishOrderingArchitectureTests` واختبارات `Msg040` خضراء، و`outbox-publish-allowlist.txt` بلا موقع عهدة. PLAN-43: `ExternalApprovalService` وأحداث العهدة `SchemaVersion=2` | مدموجان، وإلا توقف وأبلغ |
| 0.3 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 أخطاء |
| 0.4 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "Fun005\|Asset\|Wf043"` و`dotnet test src/Tests/HRManagement.IntegrationTests --filter "Ac33\|Asset\|Wf043"` | أخضر أو موثق في `reports/plan45-baseline.md` |
| 0.5 | جرد البيانات | على قاعدة القبول: التسليمات وطلبات الفقدان `PendingApproval` (بنسخة/بلا نسخة)، ونسخها في HR.Core وإصداراتها | الأرقام في `reports/plan45-baseline.md` (تغذي §5) |
| 0.6 | جرد الإصدارات | `ASSET-ASSIGN-APPROVAL` و`ASSET-LOSS-APPROVAL` في `WorkflowDefinitions` (الإصدار، النشر، الأدوار) | الجدول في التقرير، مع حالة v1 القديم من [WorkflowPlan03Seeder.cs:19-28، 47-59](../../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPlan03Seeder.cs) |

---

## In scope

### 1. البدء المحلي أولاً، وإعادة البدء، والسحب في AdminLogistics (G-WF-02، G-IDA-02)

**المشكلة:**
- البدء عبر HTTP يسبق الحفظ، وأي فشل يعيد `null` ([WorkflowTriggerService.cs:26-31، 56-60، 66-71](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs)). فيُحفظ التسليم `PendingApproval` و`WorkflowInstanceId = null` ([AssetsController.cs:455-471](../../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs))، وكذلك الفقدان (`:634-643`).
- لا إجراء لإعادة البدء أو السحب. والتسليم المعلّق حائز مفتوح يمنع أي تسليم آخر ([AssetAssignmentRules.cs:11-14](../../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetAssignmentRules.cs)، والفهرس الفريد على `AssetId` حيث `ReturnedAt IS NULL` في [AdminLogisticsDbContext.cs:66-68](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs)). وطلب الفقدان المعلّق يمنع طلباً جديداً ([AssetLostRules.cs:12-15](../../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetLostRules.cs)).
- النقر المزدوج أو فشل الحفظ بعد البدء يترك نسخاً يتيمة في HR.Core.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| حالات جديدة | `Admin.Logistics.Domain/Enums/AssetAssignmentStatus.cs`، `AssetLossReportStatus.cs` | تُضاف في النهاية: `ApprovalStarting` («بانتظار بدء الاعتماد»)، `Withdrawn` («مسحوب») |
| حقول | `AssetAssignment.cs`، `AssetLossReport.cs` | `ApprovalStartAttempts`، `ApprovalStartLastError` (≤ 500)، `ApprovalStartLastAttemptAt`، `NeedsIntervention`، `WithdrawnAt/By/Reason`. والطالب = `CreatedBy` القائم (`AuditableEntity`). و`xmin` على `AssetLossReport` (التسليم يحمله) |
| ترحيل | `Admin.Logistics.Infrastructure/Migrations/<ts>_WF045_AssetApprovalLoop.cs` | الأعمدة، و`xmin`، وبلا تغيير في الفهرس الفريد (الحالتان الجديدتان مفتوحتان حتى السحب) |
| `AssetApprovalRules` | `Admin.Logistics.Domain/Services/AssetApprovalRules.cs` | انتقالات صريحة: `ApprovalStarting → PendingApproval \| Withdrawn`؛ `PendingApproval → Active/Confirmed \| Rejected \| Withdrawn`. والسحب يضبط `ReturnedAt` للتسليم فيتحرر الأصل |
| `AssetApprovalStarter` | `Admin.Logistics.Infrastructure/Integration/AssetApprovalStarter.cs` (يحل محل استدعاءات `WorkflowTriggerService` من المتحكم) | يبني `WorkflowApprovalStartRequest` (PLAN-43): `IdempotencyKey = "{Type}:{Id}"`، `ExcludedApproverUserIds = {CreatedBy}`، `SubjectEmployeeId` (المستلم أو الحائز)، `OrgUnitId` من `EmployeeOrgUnitSnapshot`، وعنوان بلا رقم وطني، ورابط `/ui/admin-logistics/Assets/Details/{assetId}`. يعيد `(Success, InstanceId, Error)` ولا يرمي |
| `AssetsController.AssignToEmployee/MarkLost` | [AssetsController.cs:355-473، 552-654](../../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs) | يحفظ أولاً بحالة `ApprovalStarting`، ثم يستدعي البادئ، ثم يحفظ `PendingApproval` و`WorkflowInstanceId` معاً. عند الفشل: يبقى `ApprovalStarting` مع الخطأ ورسالة «تعذّر بدء الاعتماد الآن؛ سيُعاد المحاولة تلقائياً». و`[Idempotent]` على الإجراءين |
| إجراءات جديدة | `AssetsController.RetryApprovalStart`، `WithdrawApproval` | إعادة البدء يدوياً لطلب `ApprovalStarting`. والسحب: إن كانت هناك نسخة ← `CancelAsync` في HR.Core أولاً (PLAN-43)، فإن ردّ «منتهٍ» تُطبَّق النتيجة النهائية بدل السحب |
| `AssetApprovalStartRetryJob` | `Admin.Logistics.Infrastructure/Services/AssetApprovalStartRetryJob.cs` + تسجيل في `Program.cs` | أول مهمة خلفية في الخدمة. تلتقط `ApprovalStarting` الأقدم من الفاصل، وتبدأ داخل `ActingUserScope(CreatedBy)` (PLAN-43)، وتطبّق النتيجة. وإن أعاد HR.Core نسخة منتهية بالمفتاح نفسه تُطبَّق نتيجتها |
| المقياس | `Admin.Logistics.Infrastructure/Services/AssetApprovalMetrics.cs` | `asset_approval_starting_total`، `asset_approval_needs_intervention_total` |
| الواجهة | `Views/Assets/Details.cshtml` | شريط الحالة، وزرّا «إعادة المحاولة» و«سحب الطلب»، ورابط «عرض الاعتماد» المصحّح (§3) |

**القواعد:**
1. لا مسار في AdminLogistics يكتب `PendingApproval` بلا `WorkflowInstanceId` في الحفظ نفسه. واختبار معماري يفحص ذلك.
2. `ApprovalStarting` يحجز الأصل كما يحجزه `PendingApproval`، فلا يُسلَّم مرتين.
3. المستهلكون (بعد PLAN-43) يطبّقون النتيجة فقط إن كانت الحالة `PendingApproval` والنسخة مطابقة. والطلب المسحوب لا تغيّره نتيجة متأخرة.
4. السحب لا يحذف شيئاً، ويُسجَّل الفاعل والسبب (≥ 10 أحرف).

### 2. سلسلة الأدوار وفرضها (G-WF-02، D-WF-2)

**المشكلة:**
- الخطوة 1 في `ASSET-ASSIGN-APPROVAL` لـ `hr-core` ([WorkflowPackSeeder.cs:23-25](../../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)). أما `ASSET-LOSS-APPROVAL` فخطوة واحدة لـ `admin-logistics` (`:26-28`)، ولا يصل هذا الدور إلى سياسة الاعتماد ([HrManagementAuthorizationExtensions.cs:91-97](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- قواعد بيانات قديمة تحمل v1 (`admin-logistics` وحده) منشوراً بجانب الإصدار الأحدث ([WorkflowPlan03Seeder.cs:19-28، 47-59](../../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPlan03Seeder.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| الإصدار الجديد | `WorkflowPackSeeder` (`EnsureVersionAsync` من PLAN-43) | الرمزان بالسلسلة في جدول القرارات، و`ChangeReason="seed:PLAN-45"`، وسحب الأقدم |
| `WorkflowPlan03Seeder` | الملف القائم | لا يعيد نشر v1. يتحقق فقط، ويسحبه إن وجده منشوراً |
| حدود النوعين | `WorkflowGuardrails.cs` (PLAN-43) | النوعان: خطوتان، الأولى `admin-logistics`، والأخيرة `hr-management-approver` |
| رفع الإعفاء | `appsettings*.json` في HR.Core.Web و`docker-compose.acceptance-prodlike.yml` | `Workflow:StepRole:ExemptEntityTypes = []` |
| السياسات | `HrManagementAuthorizationExtensions.cs` | `admin-logistics` يُضاف إلى `WorkflowStepAction` (PLAN-43) و`HrCoreWorkInbox` ([:105-112](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). ولا يُضاف إلى `ModuleHrCore` ولا `HrCoreReadOnly` |
| `ExternalApprovalGate` | `HR.Core.Infrastructure/Services/Workflow/Gates/ExternalApprovalGate.cs` | بوابة واحدة لكل أنواع `ExternalApprovalTypes`: نطاق الوحدة على `ScopeOrgUnitId` للأدوار الفرعية (نمط `scope.GetBlockReasonAsync` في [RecruitmentApprovalGates.cs:22-32](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/RecruitmentApprovalGates.cs)). قواعد الطالب والموظف المعني والمعتمد المختلف تأتي من `WorkflowSodRules` (PLAN-43). تُسجَّل في `CreateGates` ([WorkflowApprovalEvaluator.cs:56-65](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/WorkflowApprovalEvaluator.cs)) |

### 3. صفحة الاعتماد العامة والصندوق للأنواع الخارجية (G-WF-02)

**المشكلة:**
- الصندوق يقرأ خمسة أنواع ليس بينها العهدة ([WorkflowInboxQueryService.cs:22-29](../../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs)).
- رابط «عرض الموافقة» يذهب إلى `Approvals/Review?workflowInstanceId=` ([Details.cshtml:288-291](../../src/Services/AdminLogistics/Admin.Logistics.Web/Views/Assets/Details.cshtml))، و`Review(Guid id)` يقرأ الجدول القديم ([ApprovalsController.cs:78-84](../../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs))، فيعيد 404.
- الطريق الوحيد اليوم POST خام، كما في AC-11 ([Ac11PolicyWorkflowTests.cs:78-86](../../src/Tests/HRManagement.AcceptanceTests/Ac11PolicyWorkflowTests.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `WorkflowApprovalsController` | `HR.Core.Web/Controllers/WorkflowApprovalsController.cs` | `GET Details/{instanceId}`، و`POST ApproveWorkflow/{instanceId}`، و`POST RejectWorkflow/{instanceId}` (السبب إلزامي في الرفض). للأنواع المسجّلة في `ExternalApprovalTypes` فقط، عبر `WorkflowInstanceActionService` نفسه |
| العرض | `HR.Core.Web/Views/WorkflowApprovals/Details.cshtml` | `DisplayTitle`، وجدول `Facts`، ورابط `DetailsUrl` إلى صفحة المالك، والتاريخ، ولوحة الإجراء من `WorkflowPanelBuilder` ([:63-233](../../src/Services/HRCore/HR.Core.Web/Services/WorkflowPanelBuilder.cs)) |
| سياسات المسار | `GatewayPathAuthorizationMiddleware.cs` (فرع `hr-core`، بجانب قاعدة الاستئناف [:155-160](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)) | `/ui/hr-core/WorkflowApprovals`: GET ← `HrCoreWorkInbox`، وPOST ← `WorkflowStepAction` |
| استثناء مرشح الوحدة | [HrModuleAuthorizationFilter.cs:41-45](../../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs) | `WorkflowApprovals` ضمن المتحكمات ذات السياسة الخاصة |
| رؤية التفاصيل | المتحكم | GET يتطلب: حاملاً لدور خطوة في التعريف المثبّت، أو الطالب، أو `admin`. وإلا 403. فلا يقرأ حامل `HrCoreWorkInbox` أي نسخة بمعرّفها |
| تعميم الصندوق | `WorkflowInboxQueryService.cs` | `SupportedEntityTypes` = الخمسة ∪ `ExternalApprovalTypes.All`. وسياق الأنواع الخارجية من حقول النسخة: `ScopeOrgUnitId`، والطالب والمستبعَدون (`WorkflowSodRules`)، و`IsAwaitingApproval = Status == InProgress` |
| `WorkInboxAudience` | `HR.Core.Web/Services/WorkInboxAudience.cs` | الأدوار التي تصل إلى الصندوق من خارج HR.Core، من `ExternalApprovalTypes.AudienceRoles`. هنا `admin-logistics`، ويضيف PLAN-46 `idcard` |
| الرابط العميق | `WorkflowDeepLinkResolver.cs` | الأنواع الخارجية ← `/ui/hr-core/WorkflowApprovals/Details/{instanceId}` |
| الأولوية والعنوان | `WorkInboxItemMapper.cs` | العنوان من `DisplayTitle`. والأولوية `Normal` كما هي (المهل ← الموجة 2) |
| رابط AdminLogistics | `Views/Assets/Details.cshtml`، وقائمة التنقل في `Admin.Logistics.Web/Views/Shared/_Layout.cshtml` | الرابط المصحّح، ورابط «صندوق الاعتمادات» إلى `/ui/hr-core/WorkInbox` لحاملي `admin-logistics` |

**القواعد:**
1. الاعتماد والرفض من الصفحة العامة يمران بالمقيِّم نفسه (البنية ← الدور ← قواعد الفصل ← `ExternalApprovalGate`)، فلا مسار أوسع من POST API.
2. `Facts` تُعرض نصاً مرمّزاً (HTML-encoded) ولا تُفسَّر.
3. `DetailsUrl` مسار نسبي يبدأ بـ `/ui/admin-logistics/`، ويُرفض غيره عند البدء (`DetailsUrlPrefix` في السجل، PLAN-43).

### 4. اختبارات القبول القائمة (G-WF-02، ويُغلق جزء العهدة من G-WF-13)

| Artifact | المسار | الغرض |
|----------|--------|-------|
| AC-11 | [Ac11PolicyWorkflowTests.cs:27-86](../../src/Tests/HRManagement.AcceptanceTests/Ac11PolicyWorkflowTests.cs) | بلا `Mode` من العميل وبلا معرّف عشوائي: تسليم حقيقي من `a-logistics-maker`، واعتماد الخطوة 1 من `a-logistics-checker`، والخطوة 2 من `a-management-approver`، ثم `Active` |
| AC-33 | [Ac33AssetLossLiveTests.cs:31](../../src/Tests/HRManagement.AcceptanceTests/Ac33AssetLossLiveTests.cs) | لا يقبل 403. فقدان أصل مميز ← خطوتان ← الأصل `Lost` والتسوية مسجلة |

### 5. ترحيل الطلبات المعلّقة القائمة

| Artifact | المسار | الغرض |
|----------|--------|-------|
| سكربت الإصلاح | `scripts/wf045-repair-asset-approvals.ps1` | تشغيل جاف افتراضي. `-Apply`: `PendingApproval` بلا نسخة ← `ApprovalStarting` (تلتقطها المهمة). `-RestartOnLatest`: نسخ جارية على إصدار مسحوب، عند الخطوة 1 وبلا قرار ← `CancelAsync` ثم بدء جديد. والتقرير في `reports/wf045-repair-<date>.json` |

---

## Out of scope

- نشر النتيجة بعد آخر حفظ (G-WF-03) وحارسه المعماري ← PLAN-40
- البدء الخارجي، والسجل، وأحداث v2، وفرض الدور افتراضياً ← PLAN-43
- التزامن والفهرس الفريد للنسخ الجارية ← PLAN-44
- العقد العام (`IWorkflowApprovalClient`، `IWorkflowOutcomeDecided`) ← PLAN-46. وترحيل `AssetApprovalStarter`/`WorkflowTriggerService` إلى العميل المشترك ← الموجة 2 (السلك واحد، فلا تعارض في الانتظار؛ وPLAN-46 يستبعده صراحة)
- عتبة القيمة لكل جهة (D-WF-3) ← الموجة 2. وإغلاق العهدة الصامت (G-IDA-05): جزء الفصل ← PLAN-62، والباقي (النقل والتعويض) ← الموجة 2 (IDA-D)
- نطاق الوحدة في شاشات AdminLogistics ← PLAN-52

## Acceptance scenarios

على الملف الشبيه بالإنتاج (`docker-compose.acceptance-prodlike.yml`)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، في `AcWf45AssetApprovalLoopTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-WF-45", …)`، `AcceptanceGate.RequireData` لأصل مميز مُبذَر في الجهة A).

| ID | Positive | Negative |
|----|----------|----------|
| AC-WF-45a | `a-logistics-maker` يسلّم أصلاً مميزاً ← `PendingApproval` بنسخة ← يظهر في صندوق `a-logistics-checker` ← يعتمد من الصفحة العامة ← يظهر لـ `a-management-approver` ← يعتمد ← التسليم `Active` والأصل `InUse` خلال 30 ثانية، وصف `OutboxMessage` مُرسَل في HR.Core | — |
| AC-WF-45b | إيقاف `hr-core-web` ← التسليم `ApprovalStarting` برسالة عربية والأصل محجوز ← تشغيله ← المهمة تبدأ النسخة ← `PendingApproval` | في أي لحظة: لا صف `PendingApproval` بلا `WorkflowInstanceId` (فحص قاعدة) |
| AC-WF-45c | `a-logistics-maker` يسحب طلباً معلّقاً ← `Withdrawn`، والنسخة `Cancelled` في HR.Core، والأصل يُسلَّم لموظف آخر | نتيجة متأخرة للنسخة المسحوبة ← لا أثر |
| AC-WF-45d | فقدان أصل مميز من `a-logistics-maker` ← `a-logistics-checker` ثم `a-management-approver` ← الأصل `Lost` | `central-a` (`hr-core`) يعتمد الخطوة 1 ← رفض الدور |
| AC-WF-45e | — | `a-logistics-maker` (الطالب) يعتمد الخطوة 1 ← رفض «لا يجوز لطالب الإجراء…». و`a-logistics-checker` يعتمد الخطوة 2 بعد الخطوة 1 ← رفض الدور |
| AC-WF-45f | — | حدث `IAssetAssignmentApproved` v2 بمعرّف نسخة غير مرتبطة (من أداة الاختبار) ← التسليم لا يتغير، وتحذير مسجّل |
| AC-WF-45g | رابط «عرض الاعتماد» من صفحة الأصل يفتح الصفحة العامة (200) لـ `a-logistics-checker` | `b-logistics` (جهة B) يفتح الرابط نفسه ← 404/403. و`central-a2` (لا دور له في التعريف) ← 403 |
| AC-WF-45h | AC-11 وAC-33 المعاد كتابتهما = Pass | — |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet ef migrations has-pending-model-changes -p src/Services/AdminLogistics/Admin.Logistics.Infrastructure -s src/Services/AdminLogistics/Admin.Logistics.Web
dotnet test src/Tests/HRManagement.UnitTests --filter "Wf045|Fun005|Asset|WorkflowDefinitionsHaveHandlers|Gateway"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Wf045|Ac33|Wf043"
# قبول حي
pwsh scripts/wf045-repair-asset-approvals.ps1            # تشغيل جاف
pwsh scripts/acceptance-reset.ps1
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --build --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcWf45|FullyQualifiedName~Ac11|FullyQualifiedName~Ac33|FullyQualifiedName~Ac32"
```

**اختبارات جديدة:**
- **Unit:** `Wf045AssetApprovalRulesTests` (الانتقالات)، `Wf045NoPendingWithoutInstanceArchitectureTests`، `Wf045ExternalApprovalGateTests`، `Wf045WorkflowApprovalsControllerTests` (رؤية التفاصيل والسياسات)، وتحديث `GatewayPathAuthorizationMiddlewareTests` و`HrModuleAuthorizationFilter` tests.
- **Integration على PostgreSQL** (Testcontainers)، `Wf045AssetApprovalPostgresTests`:
  - HR.Core: اعتماد الخطوة الأخيرة لنسخة عهدة ← صف `OutboxMessage` بالحدث v2 في المعاملة نفسها.
  - AdminLogistics: `xmin` على `AssetLossReport` (سحب متزامن مع مستهلك ← واحد ينجح). والفهرس الفريد يمنع تسليمين مفتوحين حتى مع `ApprovalStarting`.
  - مهمة إعادة البدء مع خادم HR.Core وهمي: فشل ثم نجاح ← `PendingApproval` بنسخة واحدة (المفتاح نفسه).

## Definition of done

- لا يُحفظ `PendingApproval` بلا نسخة في AdminLogistics، وفشل HR.Core يُعالَج بإعادة بدء آلية ويدوية مرئية.
- السحب يحرر الأصل ويلغي النسخة.
- العهدة والفقدان في صندوق العمل وفي صفحة اعتماد عامة، والرابط يعمل.
- السلسلة `admin-logistics` ← `hr-management-approver` بفاعلين مختلفين، والدور مفروض للنوعين، ولا إعفاء.
- AC-WF-45a..h = Pass في `acceptance-evidence/AC-WF-45/`، وAC-11 وAC-33 يختبران الاعتماد فعلاً.
- سطر «الحالة» لـ G-WF-02 في دراسة WF وG-IDA-02 في دراسة FUN-005 محدّث. و`FUN_005_*` و`WF_002_WORK_INBOX.md` يصفان الحلقة والصفحة العامة.
- الالتزامات بلا أسرار (القوالب تبقى بالعناصر النائبة `__SVC_SECRET__`/`${…}`)، ولا `global.json`، ولا `testdata/*/out/*.json` أعاد الاختبار كتابتها. ولا دفع إلى البعيد.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-45-wf-asset-custody-approval-loop.md` |
| AdminLogistics | الحالتان، الحقول، `AssetApprovalRules.cs`، `AssetApprovalStarter.cs`، `AssetApprovalStartRetryJob.cs`، `AssetApprovalMetrics.cs`، تحديث `AssetsController` و`Details.cshtml` والتنقل، الترحيل `WF045_AssetApprovalLoop` |
| HR.Core | `WorkflowApprovalsController.cs` وعرضه، `ExternalApprovalGate.cs`، `WorkInboxAudience.cs`، تحديث `WorkflowInboxQueryService` و`WorkflowDeepLinkResolver` و`WorkInboxItemMapper` و`WorkflowPackSeeder`/`WorkflowPlan03Seeder` |
| Shared | السياسات (`WorkflowStepAction`، `HrCoreWorkInbox`)، قاعدة البوابة، استثناء مرشح الوحدة |
| Config | `AssetApproval:Retry` في AdminLogistics، و`Workflow:StepRole:ExemptEntityTypes=[]` في HR.Core و`docker-compose.acceptance-prodlike.yml` |
| Tests | الاختبارات أعلاه، و`AcWf45AssetApprovalLoopTests.cs`، وإعادة كتابة `Ac11PolicyWorkflowTests.cs` و`Ac33AssetLossLiveTests.cs` |
| Script | `scripts/wf045-repair-asset-approvals.ps1` |
| Docs | `FUN_005_*` (رحلة العهدة)، `WF_002_WORK_INBOX.md` (الأنواع الخارجية والصفحة العامة)، `SOD_MATRIX.md` (سلسلة العهدة)، وسطور «الحالة» في الدراستين |
| CI | `tests/acceptance/ci-scope.json`: تسجيل كل صنف قبول جديد في هذه الخطة (مطلوباً أو مستبعداً بمالك)، وإلا تفشل وظيفة القبول (قاعدة PLAN-41) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| PLAN-40 غير مدموج فلا تصل النتيجة | شرط 0.2. وAC-WF-45a يفحص صف الـ Outbox صراحة |
| مسؤول الأصول يحمل نطاق فرع لا يعرفه `OrgScopeService` | `ExternalApprovalGate` يطبّق النطاق على الأدوار الفرعية كما في بوابات التوظيف. وتُثبت AC-32 أن النطاق لم يتسع |
| إعادة البدء تنشئ نسخاً مكررة | المفتاح الثابت `{Type}:{Id}` في PLAN-43، والفهرس الفريد للنسخ الجارية من PLAN-44 |
| `admin-logistics` في `HrCoreWorkInbox` يفتح صفحات HR.Core أخرى | السياسة لا تُضاف إلى `ModuleHrCore` ولا `HrCoreReadOnly`. واختبار البوابة يثبت 403 على `/ui/hr-core/Employees` |
| تعديل الحالات المخزّنة رقمياً | الإضافة في نهاية التعداد فقط |

## Effort estimate

8–10 أيام عمل: المرحلة 0 (0.5)، الحلقة في AdminLogistics والمهمة والترحيل (3)، السلسلة والبوابة والسياسات (1)، الصفحة العامة والصندوق (2)، AC-11 وAC-33 والسكربت (1)، التكامل والقبول والتوثيق (1.5–2.5).
