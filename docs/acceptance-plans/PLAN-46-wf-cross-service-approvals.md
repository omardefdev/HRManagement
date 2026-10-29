# PLAN-46 — الاعتماد العابر للخدمات: العقد العام، وربط البطاقة والتأديب والفصل (WF-C)

**Scope:** WF-001 — [دراسة الفجوات](../WF_001_GAP_STUDY.md): G-WF-01, G-WF-17 (ويحتوي الأثر المالي لـ G-ER-01 بمنع نشر الإجراء التأديبي قبل الاعتماد)  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-40 (النشر قبل آخر حفظ)، PLAN-43 (البدء الخارجي المقيّد، `WorkflowApprovalStartRequest`، فرض الدور افتراضياً، `EnsureVersionAsync`)، PLAN-45 (صفحة الاعتماد العامة، وتعميم الصندوق للأنواع الخارجية، و`ExternalApprovalGate`)  
**Related cards:** WF-001، WF-002، FUN-005 (البطاقة)، FUN-004 / HCM-006 (التأديب)، FUN-002 (الفصل)، **PLAN-60** (يستهلك هذا العقد لسجل الفصل؛ §0.1 فيه)

## Goal

أن يصير اعتماد أي قرار تملكه خدمة أخرى اعتماداً حقيقياً في محرك HR.Core، بعقد واحد مكتوب:

- عقد عام مشترك: عميل (`IWorkflowApprovalClient`)، وطلب بدء، وحدث نتيجة (`IWorkflowOutcomeDecided`)، وقواعد الـ idempotency والفشل والتعويض، وعنصر الصندوق، وفرض دور الخطوة على الخادم.
- **إصدار البطاقة** لا يتم إلا بعد اعتماد شخص غير طالبها.
- **الإجراء التأديبي** لا يُنشر إلى الرواتب إلا بعد اعتماد الامتثال ثم صاحب الصلاحية.
- **الفصل:** جهة HR.Core جاهزة (السجل والفرض والصندوق)، ويربط PLAN-60 سجل الفصل نفسه بهذا العقد.
- لا تعريف منشور بلا مالك حقيقي، والحارس المعماري يفحص المستهلك بالانعكاس لا بجملة نصية.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-WF-1 سلسلة البطاقة | `ID-CARD-ISSUANCE` بإصدار جديد بخطوة واحدة «مراجعة مكتب البطاقات» لدور `idcard`، والطالب مستبعد. الدور `security` في الإصدار الحالي غير موجود في الكتالوج ([WorkflowPackSeeder.cs:35-37](../../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)) | §3 |
| D-WF-1 / D-ER-2 قناة اعتماد التأديب | **المحرك** (وفق توجيه المنسّق، بدل السلسلة المحلية التي أوصت بها D-ER-2): `DISCIPLINARY-APPROVAL` بإصدار جديد: 1 «مراجعة امتثال» `compliance`، 2 «اعتماد صاحب الصلاحية» `hr-management-approver`، و`RequireDistinctApprovers=true`. سلّم العقوبات والتحقيق ← الموجة 2 (ER) | §4 |
| سلسلة الفصل | يملكها PLAN-60 (D-SEP-4): `SEPARATION-APPROVAL` و`SEPARATION-APPROVAL-SENSITIVE`. هذه الخطة تسجّل الرمزين فقط | §5 |
| تقديم طلب البطاقة الآلي عند التعيين | لا: `IdCard:Approval:AutoSubmitOnHire=false`. الطلب الآلي من `EmployeeCreatedConsumer` يبقى مسودة `Pending` حتى يقدّمه موظف `idcard` | §3 |
| السجلات القائمة | الإجراءات التأديبية المنشورة سابقاً ← `Approved` بعلامة `ApprovedBeforeEngine=true`، ولا إعادة نشر. طلبات البطاقة `Pending` تبقى مسودات تحتاج تقديماً. و`Processed` بلا تغيير | §3، §4 |
| مصالحة المالك | كل `WorkflowApproval:Sweep:IntervalMinutes=10` للسجلات `PendingApproval` الأقدم من `StaleAfterMinutes=15`. في ملف القبول 1 و2 | §1 |
| التعريفات اليتيمة | تُسحب `HIRE-WIZARD` و`TRANSFER-APPROVAL` و`IT-TICKET` و`SEPARATION-WIZARD` (نمط `RetiredCodes`). وقواعد السياسة التي تطلقها ← PLAN-49 | §6 |

كل افتراض خلف إعداد (`IdCard:Approval`، `Compliance:Disciplinary:Approval`، `WorkflowApproval:Sweep`) أو في بذرة مختبَرة.

---

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-46-wf-cross-service-approvals` | — |
| 0.2 | التحقق من التبعيات | PLAN-40 (حارس النشر)، PLAN-43 (`ExternalApprovalService`، `WorkflowApprovalStartRequest`، `ActingUserScope`)، PLAN-45 (`WorkflowApprovalsController`، `WorkInboxAudience`، `ExternalApprovalGate`) | مدموجة، وإلا توقف وأبلغ |
| 0.3 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 أخطاء |
| 0.4 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "IdCard\|Compliance\|Disciplinary\|WorkflowDefinitionsHaveHandlers\|Wf043\|Wf045"` و`dotnet test src/Tests/HRManagement.IntegrationTests --filter "IdCard\|Compliance\|Wf043\|Wf045"` | أخضر أو موثق في `reports/plan46-baseline.md` |
| 0.5 | جرد البيانات | على قاعدة القبول: `CardRequests` حسب الحالة، و`IssuedCards` بلا طلب، و`DisciplinaryActions`، ونسخ `CardRequest`/`DisciplinaryAction`/`SeparationRecord`/`ServiceTransaction`/`TransferRequest`/`Ticket` في HR.Core | الأرقام في `reports/plan46-baseline.md` |
| 0.6 | مواءمة PLAN-60 | قراءة §0.1 في PLAN-60 مقابل §1 هنا | الفروق موثقة في التقرير (انظر «مواءمة PLAN-60» في §5) |

---

## In scope

### 1. العقد العام العابر للخدمات (G-WF-01)

**المشكلة:**
- الآلية العابرة الوحيدة POST خام خاص بالعهدة، بلا ربط حالة ([WorkflowTriggerService.cs:17-72](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs)).
- `PublishWorkflowOutcomeAsync` لا يعرف إلا العهدة والفقدان وأنواع HR.Core ([WorkflowInstanceActionService.cs:171-206](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)). ولا حدث نتيجة عاماً.
- لذلك يُنفَّذ الفصل والبطاقة والتأديب بلا اعتماد (G-WF-01).

**التصميم — الأنواع (`src/Shared/HRManagement.ServiceDefaults/Workflow/CrossService/`)، بأسماء PLAN-60 §0.1:**

```csharp
public interface IWorkflowApprovalClient
{
    Task<WorkflowApprovalStartResult> StartAsync(WorkflowApprovalStartRequest request, CancellationToken ct);
    Task<WorkflowApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct);
    Task<WorkflowApprovalStatusDto?> GetStatusAsync(Guid instanceId, CancellationToken ct);
}

// معرّف في PLAN-43 (جسم السلك). المعامل الأخير اختياري ولا يكسر استدعاءات PLAN-60.
public sealed record WorkflowApprovalStartRequest(
    string EntityType, Guid EntityId, string WorkflowCode, string IdempotencyKey,
    IReadOnlyCollection<Guid> ExcludedApproverUserIds, Guid? OrgUnitId,
    string DisplayTitle, string DetailsUrl, IReadOnlyDictionary<string, string>? Facts,
    Guid? SubjectEmployeeId = null);

public sealed record WorkflowApprovalStartResult(
    bool Success, Guid? InstanceId, bool AlreadyActive, string? ErrorCode, string? Error);

public sealed record WorkflowApprovalCancelResult(
    bool Success, bool AlreadyTerminal, string? FinalOutcome, string? ErrorCode, string? Error);

public sealed record WorkflowApprovalStatusDto(
    Guid InstanceId, string EntityType, Guid EntityId, string WorkflowCode, int DefinitionVersion,
    string Status,                       // InProgress | Approved | Rejected | Cancelled
    int? CurrentStepLevel, string? CurrentStepName, string? CurrentStepRole,
    IReadOnlyList<WorkflowStepDecisionDto> Decisions,
    DateTime StartedAt, DateTime? CompletedAt,
    string ApprovalUrl);                 // /ui/hr-core/WorkflowApprovals/Details/{id}

public static class WorkflowApprovalErrorCodes
{
    public const string DefinitionMissing = "DefinitionMissing"; // 422
    public const string PolicyDenied      = "PolicyDenied";      // محجوز؛ لا يُعاد في هذا الإصدار
    public const string EngineUnavailable = "EngineUnavailable"; // مهلة، 5xx، شبكة، عنوان غير مضبوط
    public const string Forbidden         = "Forbidden";         // 401/403: ليست المالكة أو بلا نطاق
    public const string Conflict          = "Conflict";          // 409: نسخة جارية للكيان بمفتاح آخر
    public const string Invalid           = "Invalid";           // 400: حقول مرفوضة (العنوان، الرابط، الطالب)
}
```

**حدث النتيجة (`src/Shared/HRManagement.MessageContracts/Events/IWorkflowOutcomeDecided.cs`)، كما في PLAN-60 §0.1:**

```csharp
public interface IWorkflowOutcomeDecided
{
    Guid? TenantId { get; }
    string EntityType { get; }
    Guid EntityId { get; }
    Guid InstanceId { get; }
    string WorkflowCode { get; }
    string Outcome { get; }            // Approved | Rejected | Cancelled
    Guid? DecidedByUserId { get; }     // صاحب الخطوة الأخيرة، أو من ألغى
    DateTime DecidedAt { get; }
    string? Reason { get; }            // سبب الرفض أو الإلغاء
    IReadOnlyList<WorkflowStepDecisionDto> Decisions { get; }
    string? CorrelationId { get; }     // = IdempotencyKey الذي بدأ النسخة
    int SchemaVersion => 1;
}

// src/Shared/HRManagement.MessageContracts/Events/WorkflowStepDecisionDto.cs
public sealed record WorkflowStepDecisionDto(int Level, string RoleRequired, Guid? UserId, string Action, DateTime At);
// Action: Approved | Rejected | Withdrawn
```

| Artifact | المسار | الغرض |
|----------|--------|-------|
| الأنواع أعلاه | `ServiceDefaults/Workflow/CrossService/IWorkflowApprovalClient.cs`، `WorkflowApprovalResults.cs`، `WorkflowApprovalErrorCodes.cs`؛ و`MessageContracts/Events/IWorkflowOutcomeDecided.cs`، `WorkflowStepDecisionDto.cs` | العقد |
| `HttpWorkflowApprovalClient` | `ServiceDefaults/Workflow/CrossService/HttpWorkflowApprovalClient.cs` | `POST api/workflow-instances` و`POST api/workflow-instances/{id}/cancel` (PLAN-43)، و`GET api/workflow-instances/{id}/external-status` (§2). يحوّل الرموز: 200 ← نجاح، 400 ← `Invalid`، 401/403 ← `Forbidden`، 409 ← `Conflict`، 422 ← `DefinitionMissing`، والباقي والاستثناءات ← `EngineUnavailable`. **لا يرمي** إلا عند الإلغاء (`OperationCanceledException`) |
| `AddWorkflowApprovalClient` | `ServiceDefaults/Workflow/CrossService/WorkflowApprovalClientExtensions.cs` | `AddServiceAuthenticatedClient("hr-core-workflow", Integration:HrCoreBaseUrl, forwardActingUser: true)` ([ServiceIdentityClient.cs:141-163](../../src/Shared/HRManagement.ServiceDefaults/ServiceIdentityClient.cs)). غياب العنوان ← كل استدعاء يعيد `EngineUnavailable` مع سجل خطأ، ولا يُفترض النجاح |
| `WorkflowOutcomeGuard` | `ServiceDefaults/Workflow/CrossService/WorkflowOutcomeGuard.cs` | للمستهلكين: `ShouldApply(entityInstanceId, isPending, message)` (الارتباط والحالة و`SchemaVersion`)، و`HasExcludedDecider(message, excluded)` (دفاع في العمق)، ومقياس `workflow_outcome_ignored_total{entity_type,reason}` |
| `WorkflowApprovalSweepJob<TStore>` + `IWorkflowApprovalOwnerStore` | `ServiceDefaults/Workflow/CrossService/WorkflowApprovalSweepJob.cs` | مصالحة جهة المالك: يسرد المخزن السجلات `PendingApproval` الأقدم من العتبة، فيستدعي `GetStatusAsync`، فإن كانت منتهية يطبّق النتيجة بالدالة نفسها التي يستعملها المستهلك (`ApplyOutcomeAsync`) |
| `FakeWorkflowApprovalClient` | `src/Tests/HRManagement.TestSupport/Workflow/FakeWorkflowApprovalClient.cs` | يسجّل الطلبات، ويحاكي الأخطاء، ويصنع `IWorkflowOutcomeDecided` يدوياً (يطلبه PLAN-60) |

**القواعد — العقد:**
1. **الفاعل:** الطالب لا يُرسل في الجسم. يُشتق على الخادم من الهوية المُمرَّرة (`X-Acting-User`، أو تأكيد PLAN-42)، ولا يُقبل إلا من الخدمة المالكة للنوع. والمهام الخلفية تمرّر الطالب المخزّن عبر `ActingUserScope` (PLAN-43).
2. **Idempotency:** المفتاح يختاره المالك ويُثبّته لكل تقديم (`"{EntityType}:{Id}:{SubmissionNo}"`). المفتاح نفسه يعيد النسخة نفسها في أي حالة. وإن كانت منتهية (`AlreadyActive=false`) يطبّق المالك نتيجتها عبر `GetStatusAsync`. ونسخة جارية للكيان بمفتاح آخر ← `Conflict`.
3. **فشل البدء:** `Success=false` لا يغيّر حالة السجل: يبقى مسودة، وتُعرض رسالة عربية بحسب `ErrorCode`. ولا `PendingApproval` إلا مع `InstanceId` في الحفظ نفسه (قاعدة PLAN-25).
4. **فشل الحفظ بعد بدء ناجح:** يستدعي المالك `CancelAsync(instanceId, "فشل الحفظ المحلي")` بأفضل جهد ويسجّل. وإن فشل الإلغاء، فإعادة التقديم بالمفتاح نفسه تستعيد النسخة نفسها.
5. **الإلغاء:** `CancelAsync` على نسخة جارية ← `Success` ويُنشر `Cancelled`. وعلى نسخة منتهية ← `AlreadyTerminal=true` و`FinalOutcome`. عندئذ **لا** يعود المالك إلى المسودة، بل يطبّق النتيجة النهائية.
6. **التسليم:** الحدث يُنشر عبر الـ Outbox في معاملة الاعتماد نفسها (PLAN-40، PLAN-44)، فالتسليم مرة على الأقل. والمستهلك idempotent: يطبّق فقط إن كان `entity.WorkflowInstanceId == InstanceId` والحالة `PendingApproval`. حالة المالك النهائية (ملغى، معالَج) تغلب أي نتيجة متأخرة، ويُسجَّل التجاهل ويُعدّ.
7. **الحدث المفقود:** إن فات الحدث (مثلاً وصل قبل حفظ المالك `PendingApproval`)، تلتقطه مصالحة المالك (`WorkflowApprovalSweepJob`). **كل مالك يسجّل المصالحة لنوعه.**
8. **المستهلك والنشر الثانوي:** المستهلك الذي ينشر حدثاً لاحقاً (التأديب) يعمل بمخرج المستهلك المعاملي الذي فعّله PLAN-40 لكل الخدمات ذات الـ outbox (`AddPostgresOutbox<TDbContext>(consumerOutbox: true)`، ومنها Compliance وIDCard)، فلا نشر مزدوج ولا ضائع. وكل حدث جديد أو ناشر منقول هنا (`IWorkflowOutcomeDecided` من HR.Core، و`IDisciplinaryActionTaken` من المستهلك) يُضاف إلى `EVENT_PUBLISHER_INVENTORY.md` (قاعدة R4 في حارس PLAN-40).
9. **الإصدار:** `SchemaVersion=1`. والمستهلك يرفض ما فوق المدعوم (`EventSchemaVersion.Supports`).

### 2. جهة HR.Core: السجل والناشر والحالة والصندوق والفرض (G-WF-01)

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| مدخلات السجل | `ExternalApprovalTypes.cs` (PLAN-43) | `CardRequest` (المالك `svc-idcard`، الرموز `ID-CARD-ISSUANCE`، الجمهور `idcard`، البادئة `/ui/idcard/`)؛ `DisciplinaryAction` (`svc-compliance`، `DISCIPLINARY-APPROVAL`، `compliance`، `/ui/compliance/`)؛ `SeparationRecord` (`svc-hr-contracts`، `SEPARATION-APPROVAL` و`SEPARATION-APPROVAL-SENSITIVE`، `compliance`، `/ui/hr-contracts/`). الثلاثة `OutcomeEvent = Generic` |
| `ExternalOutcomePublisher` | `HR.Core.Infrastructure/Services/Workflow/External/ExternalOutcomePublisher.cs` | ينشر `IWorkflowOutcomeDecided` عند الاكتمال والرفض (من `PublishWorkflowOutcomeAsync`) وعند الإلغاء (من `ExternalApprovalService.CancelAsync`)، داخل المعاملة وقبل الحفظ الأخير. `Decisions` من `StepHistoryJson` |
| `HandledEntityTypes` | [WorkflowInstanceActionService.cs:59-70](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs) | يُضاف إليه كل نوع `Generic` في السجل |
| `GET api/workflow-instances/{id}/external-status` | `WorkflowInstancesController.cs` | `WorkflowApprovalStatusDto`. للخدمة المالكة للنوع فقط (`SourceClientId == caller`)، وإلا 404 |
| حدود الأنواع | `WorkflowGuardrails.cs` (PLAN-43) | `CardRequest`: خطوة ≥ 1، كلها `idcard` أو أعلى. `DisciplinaryAction`: ≥ 2، الأولى `compliance` والأخيرة `hr-management-approver`. `SeparationRecord`: ≥ 2 والأخيرة `hr-management-approver`، والرمز `-SENSITIVE` يتضمن `compliance` |
| فرض دور الخطوة | لا إعداد: الفرض افتراضي منذ PLAN-43، و`ExemptEntityTypes=[]` منذ PLAN-45 | اختبار معماري `Wf046RegistryEnforcedTests`: كل نوع في السجل غير معفى في `appsettings.Production.json` و`appsettings.json` و`docker-compose.acceptance-prodlike.yml`، ولكل رمز مسموح تعريف بذرة يجتاز `WorkflowDefinitionValidator` |
| السياسات | `HrManagementAuthorizationExtensions.cs` | `idcard` و`compliance` إلى `WorkflowStepAction`، و`idcard` إلى `HrCoreWorkInbox` ([:105-112](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)، و`compliance` فيه أصلاً). لا تغيير في `ModuleHrCore` |
| نطاقات الخدمات | `deploy/wiring/service-wiring.json` (PLAN-37) | `hr-core.workflow` و`Integration__HrCoreBaseUrl` لـ `svc-idcard` و`svc-compliance` و`svc-hr-contracts` موجودة في المصفوفة منذ PLAN-37، ويولّدها منها compose وk8s وrealm (PLAN-39). هذه الخطة تضيف فقط مدخلات `calls[]` لـ `POST api/workflow-instances` و`cancel` و`external-status` بسياسة `WorkflowExternalStart` للعملاء الثلاثة، ولا تعدّل قوالب Keycloak ولا ملفات compose يدوياً. `ProductionWiringMatrixTests` يحكم |
| الملف الشبيه بالإنتاج | `docker-compose.acceptance-prodlike.yml` | مدخلات `idcard-web` و`compliance-web` بالقيم أعلاه و`IdCard__Approval__AutoSubmitOnHire: "false"` |

**القواعد — عنصر الصندوق وفرض الدور:**
1. العنصر يظهر لحامل دور الخطوة الحالية فقط، ولا يظهر للطالب ولا للمستبعَدين ولا للموظف المعني (`WorkflowSodRules`). ويُقيَّد بـ `OrgUnitId` للأدوار الفرعية (`ExternalApprovalGate`).
2. العنوان `DisplayTitle` والرابط `ApprovalUrl` إلى الصفحة العامة (PLAN-45)، وفيها رابط `DetailsUrl` إلى صفحة المالك.
3. الاعتماد والرفض يمران بالمقيِّم نفسه: البنية ← دور الخطوة ← قواعد الفصل ← `ExternalApprovalGate`. ولا مسار يعتمد بلا دور الخطوة، ولا حساب خدمة يعتمد (PLAN-43).

### 3. ربط إصدار البطاقة (G-WF-01)

**المشكلة:**
- `ProcessRequest` يُنشئ بطاقة `Active` مباشرة ([CardRequestsController.cs:123-170](../../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs))، ويتجاهل نتيجة السياسة (`:132-139`). وشرط المعالجة الوحيد `Pending` ([CardIssuanceWorkflow.cs:7-8](../../src/Services/IDCard/IDCard.Domain/Services/CardIssuanceWorkflow.cs)).
- `IssuedCards/Create` يُصدر بطاقة وينشر `ICardIssued` دون طلب ([IssuedCardsController.cs:69-104](../../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs))، و`Renew` كذلك (`:270-322`).
- `Edit` يغيّر `EmployeeId` في أي حالة ([CardRequestsController.cs:83-104](../../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| حالات | `IDCard.Domain/Enums/CardRequestStatus.cs` | تُضاف في النهاية: `PendingApproval` («بانتظار الاعتماد»)، `Approved` («معتمد»). و`Pending` يُعرض «مسودة» |
| حقول | `IDCard.Domain/Entities/CardRequest.cs` | `WorkflowInstanceId`، `SubmissionNo`، `SubmittedBy/At`، `DecidedBy/At`، `RejectionReason`، و`xmin` |
| ترحيل | `IDCard.Infrastructure/Migrations/<ts>_WF046_CardRequestApproval.cs` | الأعمدة و`xmin`. لا ملء: `Pending` تبقى مسودات |
| `CardIssuanceWorkflow` | الملف القائم | `CanProcess(status) => status == Approved`؛ و`CanSubmit` (`Pending`)، و`CanEdit` (`Pending`)، و`CanCancel` (`Pending`، `PendingApproval`، `Approved`) |
| `Submit` / `Withdraw` | `CardRequestsController` (+ `IDCard.Infrastructure/Services/CardRequestApprovalService.cs`؛ لا طبقة Application في الخدمة) | التقديم يستدعي `StartAsync`: المستبعَدون `{CreatedBy, submitter}`، والموظف المعني `EmployeeId`، والمفتاح `CardRequest:{id}:{SubmissionNo}`، والعنوان «بطاقة تعريف — <الاسم> — <الغرض>». والسحب `CancelAsync` ← `Pending` |
| `CardRequestApprovalOutcomeConsumer` | `IDCard.Infrastructure/Consumers/CardRequestApprovalOutcomeConsumer.cs` | يرشّح `EntityType == "CardRequest"`، ويحرس بـ `WorkflowOutcomeGuard`. `Approved` ← `Approved`، و`Rejected` ← `Rejected` + السبب، و`Cancelled` ← `Pending` |
| `ProcessRequest` | [CardRequestsController.cs:123-170](../../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs) | يشترط `Approved` و`WorkflowInstanceId`. ونتيجة السياسة إشارة فقط (لا تبدأ نسخة بعد PLAN-43) |
| `IssuedCards/Create` | [IssuedCardsController.cs:69-104](../../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs) | يُحصر في `CardRequestId` لطلب `Approved` غير معالج للموظف نفسه، ويحوّله إلى `Processed`. وبلا طلب ← رفض «الإصدار يتطلب طلباً معتمداً» |
| `Renew` | [IssuedCardsController.cs:270-322](../../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs) | لا يُصدر. ينشئ طلباً بالغرض `Replacement` ويحوّل إلى صفحته للتقديم. والبطاقة القديمة بلا تغيير حتى المعالجة |
| `Edit` | [CardRequestsController.cs:83-104](../../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs) | في `Pending` فقط |
| المصالحة | `IDCard.Infrastructure/Services/CardRequestApprovalStore.cs` + `WorkflowApprovalSweepJob<…>` | §1 القاعدة 7 |
| التسجيل | `IDCard.Infrastructure/DependencyInjection.cs` | `AddWorkflowApprovalClient` |
| بذرة التعريف | `WorkflowPackSeeder` (`EnsureVersionAsync`) | `ID-CARD-ISSUANCE` بالسلسلة في جدول القرارات، وسحب الإصدار الحالي |
| الواجهة | `IDCard.Web/Views/CardRequests/Details.cshtml`، `Index.cshtml` | الحالة، وأزرار تقديم/سحب/معالجة بحسب الحالة، ورابط «عرض الاعتماد» (`ApprovalUrl`) |
| شخصية | `testdata/acceptance/personas.json` و`AcceptancePersonaCatalog` (العدد مشتق منذ PLAN-41) | `a-idcard-2` (`idcard`، الجهة A) |

**القواعد:**
1. لا مسار في IDCard يُنشئ `IssuedCard` إلا من طلب `Approved`. اختبار معماري يفحص كل `IssuedCards.Add` ومواضع نشر `ICardIssued`.
2. المعالِج يجوز أن يكون أي حامل `idcard`، فالعينان تحققتا: الطالب ثم المعتمد.
3. إلغاء طلب `PendingApproval` يمر بـ `CancelAsync` أولاً (§1 القاعدة 5).

### 4. ربط الإجراء التأديبي (G-WF-01، احتواء G-ER-01)

**المشكلة:**
- `DisciplinaryActionsController.Create` يحفظ وينشر `IDisciplinaryActionTaken` فوراً ([ComplianceExtendedControllers.cs:156-224](../../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)، النشر `:207-219`). وهو الناشر الوحيد للحدث، ويطبّقه Payroll ([PayrollEventConsumers.cs:306-320](../../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs))، و`Dismissal` يعطّل ملف الأجر ([InboundEventHandlers.cs:610-614](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- `Edit` يغيّر الموظف والنوع بعد النشر ([ComplianceExtendedControllers.cs:264-279](../../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)). ولا حالة اعتماد في الكيان ([DisciplinaryAction.cs:8-36](../../src/Services/Compliance/Compliance.Domain/Entities/DisciplinaryAction.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `DisciplinaryApprovalStatus` | `Compliance.Domain/Enums/DisciplinaryApprovalStatus.cs` | `Draft, PendingApproval, Approved, Rejected` |
| حقول | `DisciplinaryAction.cs` | `ApprovalStatus`، `WorkflowInstanceId`، `SubmissionNo`، `SubmittedBy/At`، `DecidedBy/At`، `RejectionReason`، `OutcomePublishedAt`، `ApprovedBeforeEngine`، و`xmin` |
| ترحيل | `Compliance.Infrastructure/Migrations/<ts>_WF046_DisciplinaryApproval.cs` | الأعمدة. الصفوف القائمة ← `Approved` + `ApprovedBeforeEngine=true` + `OutcomePublishedAt = CreatedAt` |
| `Create` | [ComplianceExtendedControllers.cs:156-224](../../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs) | يحفظ `Draft` **بلا نشر**، ثم يقدّم (`StartAsync`): المستبعَدون `{CreatedBy}`، والموظف المعني `EmployeeId`، والمفتاح `DisciplinaryAction:{id}:{SubmissionNo}`. النجاح ← `PendingApproval`، والفشل ← `Draft` برسالة، والتقديم ممكن لاحقاً من `Details` |
| `Submit` / `Withdraw` | المتحكم + `Compliance.Infrastructure/Services/DisciplinaryApprovalService.cs` (لا طبقة Application في الخدمة) | التقديم من `Draft`/`Rejected` (يزيد `SubmissionNo`)، والسحب `CancelAsync` ← `Draft` |
| `DisciplinaryApprovalOutcomeConsumer` | `Compliance.Infrastructure/Consumers/DisciplinaryApprovalOutcomeConsumer.cs` + تعريف نقطته بمخرج المستهلك | `Approved` ← `Approved` + نشر `IDisciplinaryActionTaken` (الحمولة الحالية نفسها، `PenaltyDays=0` كما هو) + `OutcomePublishedAt`، في معاملة واحدة. `Rejected` ← `Rejected` + السبب. `Cancelled` ← `Draft` |
| `Edit` | [ComplianceExtendedControllers.cs:264-279](../../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs) | في `Draft`/`Rejected` فقط |
| `Appeal` / `DecideAppeal` | `:281-311` | لإجراء `Approved` فقط |
| اختبار معماري | `UnitTests/Architecture/Wf046DisciplinaryPublishTests.cs` | لا ينشر `IDisciplinaryActionTaken` إلا `DisciplinaryApprovalOutcomeConsumer` |
| المصالحة | `Compliance.Infrastructure/Services/DisciplinaryApprovalStore.cs` + `WorkflowApprovalSweepJob<…>` | §1 القاعدة 7 |
| بذرة التعريف | `WorkflowPackSeeder` (`EnsureVersionAsync`) | `DISCIPLINARY-APPROVAL` بالسلسلة في جدول القرارات |
| الواجهة | `Compliance.Web/Views/DisciplinaryActions/Details.cshtml`، `Index.cshtml` | الحالة، وأزرار تقديم/سحب، ورابط «عرض الاعتماد» |

**القواعد:**
1. لا أثر مالي ولا وظيفي لإجراء تأديبي قبل `Approved`.
2. المستهلك يرفض `Approved` إن كان المنشئ بين `Decisions` (`HasExcludedDecider`)، ويسجّل.
3. النشر الثانوي بمخرج المستهلك المعاملي (§1 القاعدة 8)، فإعادة التسليم لا تكرر الحدث.

### 5. الفصل: جهة HR.Core والتسليم إلى PLAN-60 (G-WF-01)

**المشكلة:** `SeparationWizardController.Step1` يسجّل المنشئ معتمداً ([SeparationWizardController.cs:55-87](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs))، و`Step3Execute` يبدأ الـ saga بلا اعتماد (`:153-165`). والتعريف `SEPARATION-WIZARD` منشور ولا يبدؤه أحد ([WorkflowPackSeeder.cs:32-34](../../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)).

**تقسيم العمل (بلا تكرار):**

| البند | هذه الخطة | PLAN-60 |
|-------|-----------|---------|
| العقد والعميل والحدث وبديل الاختبار | ✔ (§1) | يستهلك |
| مدخل السجل `SeparationRecord`، والفرض، والصندوق لـ `hr-core` و`compliance` و`hr-management-approver` | ✔ (§2) | — |
| سحب `SEPARATION-WIZARD` | ✔ (§6) | يتحقق فقط |
| بذر `SEPARATION-APPROVAL` و`-SENSITIVE` | — (السجل يسمح بهما؛ غيابهما ← `DefinitionMissing`) | ✔ عبر `EnsureVersionAsync` |
| آلة الحالات، والتقديم والسحب، و`SeparationApprovalOutcomeConsumer`، وحارس التنفيذ | — | ✔ |
| مصالحة المالك لـ `SeparationRecord` | الأساس المشترك (§1) | **مطلوب منه** تسجيل `WorkflowApprovalSweepJob` لنوعه |
| مدخل قائمة السماح بنوع المستهلك | الحارس الجديد (§6) | يضيف `SeparationRecord → SeparationApprovalOutcomeConsumer` |

**مواءمة PLAN-60 §0.1 (نتيجة القراءة؛ الفروق 3–5 عولجت في نص PLAN-60 الحالي، فتبقى هنا للتتبع فقط):**
1. الأسماء والأشكال متطابقة: `IWorkflowApprovalClient` بالدوال الثلاث، و`WorkflowApprovalStartRequest` بالحقول والترتيب، و`WorkflowApprovalStartResult`، و`IWorkflowOutcomeDecided` بحقوله.
2. إضافات متوافقة: معامل أخير اختياري `SubjectEmployeeId`. ورمزا خطأ `Conflict` و`Invalid`. وتعريف `WorkflowApprovalCancelResult` و`WorkflowApprovalStatusDto` و`WorkflowStepDecisionDto`، ولم يحدد PLAN-60 حقولها.
3. **فرق:** الضمانة 1 في PLAN-60 تطلب إضافة `SeparationRecord` إلى `Workflow:EnforceStepRoleEntityTypes`. هذا المفتاح يُلغى في PLAN-43 لصالح قائمة إعفاء فارغة، فالفرض يتحقق دون أي إعداد. على PLAN-60 حذف هذا البند.
4. **فرق:** الضمانة 5 (مرة على الأقل) تتطلب من المالك أيضاً مصالحة دورية (§1 القاعدة 7). وهذا غير مذكور في PLAN-60.
5. **فرق:** `WithdrawSeparationCommand` يجب ألا يعيد السجل إلى `Draft` إن ردّ `CancelAsync` بـ `AlreadyTerminal`، بل يطبّق `FinalOutcome` (§1 القاعدة 5).
6. **ملاحظة:** في AC-SEP-60c يُرفض POST الاعتماد من `a-contracts-maker` بـ 403 من السياسة (`WorkflowStepAction` لا تشمل `hr-contracts`) قبل رسالة المحرك. وكلاهما رفض.

### 6. التعريفات اليتيمة وقائمة السماح (G-WF-17)

**المشكلة:** قائمة السماح تبرر ستة أنواع بجمل نصية، أربعة منها بلا اعتماد أصلاً ([WorkflowDefinitionsHaveHandlersTests.cs:15-23](../../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs)). والتعريفات `HIRE-WIZARD` و`TRANSFER-APPROVAL` (`hr-core` مرتين، خلافاً للواقع) و`IT-TICKET` (`it-support` غير موجود) منشورة بلا مستدعٍ ([WorkflowPackSeeder.cs:29-31، 38-40، 47-49](../../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `RetiredCodes` | [WorkflowPackSeeder.cs:81-84](../../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs) | `HIRE-WIZARD`، `TRANSFER-APPROVAL`، `IT-TICKET`، `SEPARATION-WIZARD`. والنسخ القائمة تبقى قابلة للحل على تعريفها المثبّت. و`RetireAsync` (`:125-141`) يسحب المنشور في القواعد القائمة |
| الحارس الجديد | `WorkflowDefinitionsHaveHandlersTests.cs` | `OutcomeOwnedElsewhere` يصبح `EntityType → (ServiceName, ConsumerTypeName)`. لكل بند: النوع موجود بالانعكاس في مجمّع البنية التحتية للخدمة، ويطبّق `IConsumer<IWorkflowOutcomeDecided>`. والقائمة الآن: `CardRequest → CardRequestApprovalOutcomeConsumer`، `DisciplinaryAction → DisciplinaryApprovalOutcomeConsumer`، ويضيف PLAN-60 `SeparationRecord`. ولا جمل نصية |
| اختبار السجل | `Wf046RegistryConsistencyTests` | كل نوع `Generic` في `ExternalApprovalTypes` له بند في الحارس، وكل بند في الحارس مسجَّل |

**القواعد:**
1. لا تعريف منشور في البذور إلا لنوع في `HandledEntityTypes` أو في الحارس بنوع مستهلك موجود.
2. قواعد السياسة التي تطلق الرموز المسحوبة (`R-TRF`، `R-SEP-START`، و`HIRE-WIZARD`، و`IT-TICKET` في [PolicyEngineDomainSeeds.cs](../../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)) لا تنشئ نسخاً بعد السحب، لأن `WorkflowStarter` لا يحيي تعريفاً غير منشور ([WorkflowStarter.cs:110-112](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs)). أما إزالتها فلـ PLAN-49.

---

## Out of scope

| البند | الخطة |
|-------|-------|
| النشر بعد آخر حفظ (G-WF-03) | PLAN-40 |
| ثقة الخدمات (الأسرار، توقيع الفاعل، فشل عميل السياسات المفتوح) | PLAN-42 |
| البدء الخارجي والسجل وفرض الدور افتراضياً | PLAN-43 |
| التزامن والفهرس الفريد للنسخ | PLAN-44 |
| صفحة الاعتماد العامة وتعميم الصندوق | PLAN-45 |
| آلة حالات الفصل، ومستهلكه، وحارس تنفيذه، وبذور تعريفيه | PLAN-60 |
| إصلاحات رحلة البطاقة (نشر `ICardIssued` من `ProcessRequest`، G-IDA-03؛ أفعال الواجهة، G-IDA-04) | الموجة 2 (IDA) |
| نموذج التأديب: التحقيق، وسلّم العقوبات، و`PenaltyDays` (G-ER-01) | الموجة 2 (ER) |
| فصل الخدمة بقرار تأديبي وتعطيل الأجر (G-ER-02) | PLAN-61 (الاحتواء) |
| إزالة قواعد السياسة اليتيمة | PLAN-49 |
| ترحيل `WorkflowTriggerService` في AdminLogistics إلى العميل المشترك | الموجة 2 (السلك واحد) |

## Acceptance scenarios

على الملف الشبيه بالإنتاج (`docker-compose.acceptance-prodlike.yml` بمدخلات §2)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، في `AcWf46CrossServiceApprovalTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-WF-46", …)`، `AcceptanceGate.RequireData` لموظف ببطاقة غير مصدرة في الجهة A). سيناريوهات الفصل حيّاً في AC-SEP-60 (PLAN-60).

| ID | Positive | Negative |
|----|----------|----------|
| AC-WF-46a | `a-idcard` ينشئ طلب بطاقة ويقدّمه ← `PendingApproval` بنسخة ← يظهر في صندوق `a-idcard-2` ← يعتمد من الصفحة العامة ← الطلب `Approved` خلال 30 ثانية ← `a-idcard` يعالجه ← بطاقة `Active` | — |
| AC-WF-46b | — | `a-idcard` (الطالب) يعتمد خطوته ← رفض «لا يجوز لطالب الإجراء…». و`ProcessRequest` لطلب `PendingApproval` ← رفض |
| AC-WF-46c | `Renew` ينشئ طلب `Replacement` بلا بطاقة جديدة | `IssuedCards/Create` بلا طلب معتمد ← رفض، ولا `ICardIssued` |
| AC-WF-46d | `a-compliance` ينشئ إنذاراً ← `PendingApproval`، ولا سجل معالجة في Payroll ← `a-compliance-2` يعتمد الخطوة 1 ← `a-management-approver` الخطوة 2 ← `Approved` وسجل معالجة `DisciplinaryActionTakenConsumer` في Payroll خلال 60 ثانية | — |
| AC-WF-46e | — | `a-compliance` (المنشئ) يعتمد الخطوة 1 ← رفض. و`central-a` يعتمد خطوة `compliance` ← رفض الدور. و`a-compliance-2` يعتمد الخطوة 2 بعد الخطوة 1 ← رفض الدور |
| AC-WF-46f | رفض الخطوة 2 بسبب ← `Rejected` بالسبب، ولا حدث تأديبي | — |
| AC-WF-46g | `a-compliance` يسحب إجراءً معلّقاً ← النسخة `Cancelled` والإجراء `Draft`، والتعديل متاح | تعديل إجراء `PendingApproval` ← رفض |
| AC-WF-46h | — | `IWorkflowOutcomeDecided(Approved)` لطلب بطاقة بنسخة غير مرتبطة (من أداة الاختبار) ← تجاهل مسجّل، والحالة باقية |
| AC-WF-46i | إيقاف `hr-core-web` ← تقديم البطاقة يعيد رسالة «تعذّر الوصول إلى محرك الاعتماد» والطلب مسودة ← تشغيله ← إعادة التقديم ← نسخة واحدة | لا `PendingApproval` بلا نسخة (فحص قاعدة) |
| AC-WF-46j | `GET api/workflow-definitions` يُظهر `HIRE-WIZARD` و`TRANSFER-APPROVAL` و`IT-TICKET` و`SEPARATION-WIZARD` غير منشورة | `PolicyEngine:IdCard=Enforce` (مؤقتاً للسيناريو) مع `R-ID-PROCESS` ← لا نسخة جديدة في HR.Core |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
foreach ($p in "IDCard/IDCard.Infrastructure;IDCard/IDCard.Web", "Compliance/Compliance.Infrastructure;Compliance/Compliance.Web") {
  $i, $s = $p.Split(";"); dotnet ef migrations has-pending-model-changes -p "src/Services/$i" -s "src/Services/$s"
}
dotnet test src/Tests/HRManagement.UnitTests --filter "Wf046|WorkflowDefinitionsHaveHandlers|IdCard|Disciplinary|Gateway"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Wf046|Wf043|Wf045"
# قبول حي
pwsh scripts/prepare-keycloak-import.ps1      # a-idcard-2 ونطاقا الخدمتين
pwsh scripts/acceptance-reset.ps1
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --build --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcWf46|FullyQualifiedName~AcWf45|FullyQualifiedName~Ac11"
```

**اختبارات جديدة:**
- **Unit:**
  - `Wf046HttpWorkflowApprovalClientTests`: تحويل كل رمز HTTP، والمهلة، وغياب العنوان، وعدم الرمي.
  - `Wf046OutcomeGuardTests`: نسخة غير مطابقة، وحالة غير معلّقة، ومنشئ بين المقررين، وإصدار أعلى.
  - `Wf046CardApprovalTests` و`Wf046DisciplinaryApprovalTests`: الانتقالات، والتقديم بفشل ونجاح عبر `FakeWorkflowApprovalClient`.
  - `Wf046RegistryEnforcedTests`، `Wf046RegistryConsistencyTests`، `Wf046DisciplinaryPublishTests`، والحارس الجديد في `WorkflowDefinitionsHaveHandlersTests`.
- **Integration على PostgreSQL** (Testcontainers، `[Collection("IntegrationSerial")]`):
  - `Wf046ExternalOutcomePostgresTests` (HR.Core):
    - البدء بالمفتاح نفسه مرتين، ومن مالك خطأ (403)، وبمفتاح آخر (409).
    - الاعتماد الأخير ← صف `OutboxMessage` لـ `IWorkflowOutcomeDecided` بـ `Decisions` كاملة في المعاملة نفسها.
    - الإلغاء ← `Cancelled` في الـ Outbox، والإلغاء بعد الاكتمال ← `AlreadyTerminal`.
  - `Wf046DisciplinaryConsumerPostgresTests` (Compliance): المستهلك بمخرج المستهلك ← `Approved` وحدث واحد في الـ Outbox. وإعادة التسليم ← لا حدث ثانٍ. و`xmin` يمنع سحباً متزامناً مع النتيجة.

## Definition of done

- العقد العام منشور بالأسماء في PLAN-60 §0.1 وبإضافات متوافقة، وموثق في `WF_001_WORKFLOW_ENGINE.md` (قسم «الاعتماد العابر للخدمات»).
- لا بطاقة تصدر ولا إجراء تأديبي يُنشر قبل اعتماد في المحرك من غير الطالب، بالأدلة الحية.
- جهة HR.Core للفصل جاهزة، ويُغلق PLAN-60 جهة HR.Contracts.
- قائمة السماح تشير إلى أنواع مستهلكين موجودة، ولا تعريف يتيم منشور.
- AC-WF-46a..j = Pass في `acceptance-evidence/AC-WF-46/`، وانحدار AC-WF-45 وAC-11 أخضر.
- سطر «الحالة» لـ G-WF-01 وG-WF-17 في الدراسة محدّث (G-WF-01 «مغلق للبطاقة والتأديب، والفصل مع PLAN-60»). وتُحدَّث ADR-FUN-004 (ادعاء «التأديب لا يُصدر خارج workflow» صار صحيحاً) و`SOD_MATRIX.md`.
- الالتزامات بلا أسرار (القوالب تبقى بالعناصر النائبة `__SVC_SECRET__`/`${…}`)، ولا `global.json`، ولا `testdata/*/out/*.json` أعاد الاختبار كتابتها. ولا دفع إلى البعيد.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-46-wf-cross-service-approvals.md` |
| Shared | `Workflow/CrossService/` (الواجهة، النتائج، الرموز، `HttpWorkflowApprovalClient`، `WorkflowApprovalClientExtensions`، `WorkflowOutcomeGuard`، `WorkflowApprovalSweepJob`)، `IWorkflowOutcomeDecided.cs`، `WorkflowStepDecisionDto.cs`، السياسات |
| HR.Core | مدخلات `ExternalApprovalTypes`، `ExternalOutcomePublisher.cs`، `external-status`، الحدود، البذور (`ID-CARD-ISSUANCE`، `DISCIPLINARY-APPROVAL`، `RetiredCodes`) |
| IDCard | الحالات والحقول، `CardIssuanceWorkflow`، `CardRequestApprovalService.cs`، `CardRequestApprovalOutcomeConsumer.cs`، `CardRequestApprovalStore.cs`، تحديث المتحكمين والعروض، الترحيل `WF046_CardRequestApproval` |
| Compliance | `DisciplinaryApprovalStatus.cs`، الحقول، `DisciplinaryApprovalService.cs`، `DisciplinaryApprovalOutcomeConsumer.cs` وتعريف نقطته، `DisciplinaryApprovalStore.cs`، تحديث المتحكم والعروض، الترحيل `WF046_DisciplinaryApproval` |
| Config / Infra | مدخلات `calls[]` في `deploy/wiring/service-wiring.json`، و`docker-compose.acceptance-prodlike.yml` (`IdCard__Approval__AutoSubmitOnHire`)، وصفوف `IWorkflowOutcomeDecided` و`IDisciplinaryActionTaken` في `EVENT_PUBLISHER_INVENTORY.md` |
| Tests | الاختبارات أعلاه، `FakeWorkflowApprovalClient.cs`، `AcWf46CrossServiceApprovalTests.cs`، الشخصية `a-idcard-2` |
| Docs | `WF_001_WORKFLOW_ENGINE.md`، `WF_002_WORK_INBOX.md`، `SOD_MATRIX.md`، `ADR-FUN-004`، `FUN_005_*` (البطاقة)، وسطور «الحالة» في الدراسة |
| CI | `tests/acceptance/ci-scope.json`: تسجيل كل صنف قبول جديد في هذه الخطة (مطلوباً أو مستبعداً بمالك)، وإلا تفشل وظيفة القبول (قاعدة PLAN-41) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| الخطة في سقف 10 أيام | الترتيب: العقد (§1-2) ← البطاقة ← التأديب ← الحارس. وإن تجاوز التقدير، تُفصل §4 في خطة تالية، ويبقى نشر التأديب الفوري مسجّلاً خطراً مقبولاً مؤقتاً |
| PLAN-60 يبني على أسماء مختلفة | الأسماء مطابقة لـ §0.1، والفروق الثلاثة في §5 مُبلغة للمنسّق |
| موظف `idcard` واحد في جهة صغيرة لا يجد معتمداً ثانياً | الإعداد يسمح بإضافة دور آخر للخطوة بإصدار تعريف جديد (حوكمة PLAN-43)، لا بإلغاء الاعتماد |
| تأخر الحدث يبقي السجل معلّقاً | المصالحة في جهة المالك، ومقياس `workflow_outcome_ignored_total` |
| تعارض مع PLAN-37 على نطاق `svc-hr-contracts` | هذه الخطة لا تعدّله، وتتحقق منه في المرحلة 0 لـ PLAN-60 |
| رسائل `IDisciplinaryActionTaken` قديمة مكررة في Payroll | Payroll يتجاهل المكرر (`ShouldSkipAsync`/`TryRecordAsync`)، والصفوف القائمة لا يُعاد نشرها |

## Effort estimate

10 أيام عمل (أعلى المدى): المرحلة 0 (0.5)، العقد والعميل والمصالحة (2)، جهة HR.Core (1.5)، البطاقة (2)، التأديب (2)، الحارس والسحب (0.5)، التكامل والقبول والتوثيق (1.5).
