# PLAN-60 — فتح رحلة إنهاء الخدمة وحوكمة القرار (SEP-A)

**Scope:** SEP / FUN-002 — [دراسة الفجوات](../FUN_002_SEPARATION_GAP_STUDY.md): G-SEP-01, G-SEP-04, G-SEP-11  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-46 (الاعتماد العابر للخدمات؛ العقد في §0.1)، ومن خلاله PLAN-40/42/43. يستهلك عميل دورة حياة الموظف المصادَق عليه من PLAN-42.  
**Related cards:** FUN-002، DOM-001، INT-003 (completion-backlog 01/02/04)، WF-001 (G-WF-01، G-WF-17)، PLAN-25 (منسق بدء الاعتماد والقاعدة «لا حالة معلّقة بلا نسخة»)، PLAN-29 (نمط القرار الإداري `DEC-APPT`)

## Goal

أن يُنفَّذ إنهاء الخدمة **من الواجهة وبإعدادات الإنتاج**، وألا يُنفَّذ إلا بقرار معتمد:

- سجل الفصل يملك آلة حالات صريحة على الخادم: مسودة ← بانتظار الاعتماد ← معتمد ← قيد التنفيذ ← مكتمل/ملغى.
- الاعتماد يمر بمحرك سير العمل في HR.Core عبر آلية PLAN-46، ولا يعتمد المنشئ أبداً.
- لا تنفيذ دون قرار إداري مرقّم (`DEC-SEP-YYYY-#####`) وتبليغ الموظف.
- قائمة الإخلاء قابلة للإكمال من الواجهة: بنود HR يدوية بدليل، وبنود الأطراف تكتمل بإقرار الطرف لا بيد HR.
- لا فصل لموظف غير موجود أو منتهية خدمته، ولا سجلان مفتوحان للموظف نفسه، ولا عقد لموظف آخر، ولا تعديل للجوهر بعد التقديم.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-SEP-4 سلسلة الاعتماد لكل نوع | `SEPARATION-APPROVAL`: خطوة 1 `hr-core` (مراجعة HR)، خطوة 2 `hr-management-approver` (صاحب الصلاحية). `SEPARATION-APPROVAL-SENSITIVE` للأنواع في `Separation:Approval:SensitiveTypes` (افتراضياً `Disciplinary`, `Judicial`): `hr-core` ← `compliance` ← `hr-management-approver`. المنشئ مستبعد من كل الخطوات | §1، §2، البذور |
| مُصدر القرار الإداري | أي صاحب `HrContractsApprove` غير المنشئ (`Separation:Decision:IssuerMustDifferFromCreator=true`) | §3 |
| تبليغ الموظف شرط للتنفيذ | نعم: بند يدوي `EmployeeNotified` بدليل (`Separation:Checklist:RequireEmployeeNotification=true`) | §4 |
| D-SEP-6 (جزئي) طريقة التنفيذ | يدوية: زر «تنفيذ» بعد تاريخ النفاذ بتوقيت دمشق. التنفيذ الآلي المجدول ← الموجة 2 (SEP-G) | §5 |
| الإلغاء في هذه الخطة | من `Draft` و`PendingApproval` و`Approved` فقط. إلغاء ما بعد التنفيذ يبقى مرفوضاً حتى PLAN-63 | §5 |

كل افتراض خلف إعداد في `HR.Contracts.Web/appsettings.json` (قسم `Separation`)، فلا يتوقف التنفيذ على المالك.

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | التحقق من عقد PLAN-46 | وجود `IWorkflowApprovalClient` و`IWorkflowOutcomeDecided` و`WorkflowOutcomeGuard` و`WorkflowApprovalSweepJob<TStore>` ومدخل السجل `SeparationRecord` (انظر «عقد مطلوب من PLAN-46») | موجودة بالأسماء أو بمحوّل موثق في `reports/plan60-progress.md`؛ وإلا توقف وأبلغ |
| 0.2 | فرع | `git switch -c feature/plan-60-sep-unblock-decision-governance` | — |
| 0.3 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 أخطاء |
| 0.4 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "Fun002\|Separation\|DOM001"` و`dotnet test src/Tests/HRManagement.IntegrationTests --filter "Fun002\|Int003\|G3Separation\|ContractSeparationChain"` | أخضر أو إخفاقات موثقة في `reports/plan60-baseline.md` |
| 0.5 | جرد البيانات | على قاعدة القبول: عدد `SeparationRecords` حسب (`IsCancelled`, `SeparationEventPublished`)، والتكرارات لكل `EmployeeId` مفتوح، والسجلات بـ `EmployeeId = Guid.Empty` | الأرقام في `reports/plan60-baseline.md` (تغذي ترحيل §6) |

### 0.1 عقد مطلوب من PLAN-46

تستهلك هذه الخطة آلية [PLAN-46](PLAN-46-wf-cross-service-approvals.md) ولا تبنيها. **المرجع الملزم للأسماء والأشكال هو PLAN-46 §1**، والملخص هنا للمواءمة فقط (مطابق له حتى 2026-10-30).

**الواجهة (`src/Shared/HRManagement.ServiceDefaults/Workflow/CrossService/`):**

```csharp
public interface IWorkflowApprovalClient
{
    Task<WorkflowApprovalStartResult> StartAsync(WorkflowApprovalStartRequest request, CancellationToken ct);
    Task<WorkflowApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct);
    Task<WorkflowApprovalStatusDto?> GetStatusAsync(Guid instanceId, CancellationToken ct); // العرض والمصالحة
}

public sealed record WorkflowApprovalStartRequest(
    string EntityType,                 // "SeparationRecord"
    Guid EntityId,
    string WorkflowCode,               // "SEPARATION-APPROVAL" | "SEPARATION-APPROVAL-SENSITIVE"
    string IdempotencyKey,             // $"SeparationRecord:{id}:{submissionNo}"
    IReadOnlyCollection<Guid> ExcludedApproverUserIds, // المنشئ ومقدّم الطلب
    Guid? OrgUnitId,                   // لنطاق صندوق العمل
    string DisplayTitle,               // «إنهاء خدمة — <الاسم> — <النوع>» بلا رقم وطني
    string DetailsUrl,                 // /ui/hr-contracts/SeparationRecords/Details/{id}
    IReadOnlyDictionary<string, string>? Facts,
    Guid? SubjectEmployeeId = null);   // تمرره هذه الخطة دائماً: record.EmployeeId

public sealed record WorkflowApprovalStartResult(bool Success, Guid? InstanceId, bool AlreadyActive, string? ErrorCode, string? Error);
public sealed record WorkflowApprovalCancelResult(bool Success, bool AlreadyTerminal, string? FinalOutcome, string? ErrorCode, string? Error);
// WorkflowApprovalStatusDto: Status (InProgress|Approved|Rejected|Cancelled)، الخطوة الحالية ودورها، Decisions، ApprovalUrl — كما في PLAN-46
// WorkflowApprovalErrorCodes: DefinitionMissing | PolicyDenied (محجوز) | EngineUnavailable | Forbidden | Conflict | Invalid
```

**حدث النتيجة (`src/Shared/HRManagement.MessageContracts/Events/IWorkflowOutcomeDecided.cs`)، يُنشر عبر Outbox في HR.Core:**

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
    string? Reason { get; }
    IReadOnlyList<WorkflowStepDecisionDto> Decisions { get; } // Level, RoleRequired, UserId, Action, At
    string? CorrelationId { get; }     // = IdempotencyKey
    int SchemaVersion => 1;
}
```

**ما يضمنه PLAN-46 (جهة HR.Core للفصل):**

1. فرض دور الخطوة على الخادم **افتراضياً** لكل الأنواع (أُلغي `Workflow:EnforceStepRoleEntityTypes` في PLAN-43/46، فلا تضيف هذه الخطة أي إعداد).
2. مدخل السجل `SeparationRecord` (المالك `svc-hr-contracts`، الرمزان، الصندوق لـ `hr-core` و`compliance` و`hr-management-approver`)، وحدود الخطوات (≥ 2، الأخيرة `hr-management-approver`، و`-SENSITIVE` يتضمن `compliance`).
3. `ExcludedApproverUserIds` مفروض في الاعتماد والرفض، ونسخة جارية واحدة لكل `(EntityType, EntityId)`، والمفتاح نفسه يعيد النسخة نفسها، ونسخة جارية بمفتاح آخر ← `Conflict`.
4. الحدث عبر Outbox (مرة على الأقل)، و`WorkflowOutcomeGuard` للمستهلكين، و`WorkflowApprovalSweepJob<TStore>` الأساسي للمصالحة.
5. سحب `SEPARATION-WIZARD` (`RetiredCodes`). وتتحقق منه هذه الخطة فقط.
6. `FakeWorkflowApprovalClient` في `src/Tests/HRManagement.TestSupport/Workflow/`.

**ما تملكه هذه الخطة (جهة HR.Contracts):**

1. بذر `SEPARATION-APPROVAL` و`SEPARATION-APPROVAL-SENSITIVE` عبر `EnsureVersionAsync` (PLAN-43) وفق D-SEP-4.
2. آلة الحالات، والتقديم، والسحب، و`SeparationApprovalOutcomeConsumer` (باستعمال `WorkflowOutcomeGuard`)، وحارس التنفيذ.
3. **تسجيل المصالحة لنوعها:** `SeparationApprovalOwnerStore : IWorkflowApprovalOwnerStore` و`WorkflowApprovalSweepJob<SeparationApprovalOwnerStore>` في `HR.Contracts.Web/Program.cs`. يلتقط السجلات `PendingApproval` الأقدم من `WorkflowApproval:Sweep:StaleAfterMinutes` (مفتاح PLAN-46 المشترك: 15، و2 في ملف القبول؛ لا مفتاح خاص بالفصل)، ويطبّق النتيجة المنتهية بالدالة نفسها التي يستعملها المستهلك (`ApplyOutcomeAsync`).
4. مدخل قائمة السماح `SeparationRecord → SeparationApprovalOutcomeConsumer` في حارس PLAN-46.

**قواعد الاستهلاك (من PLAN-46 §1):**
- الفاعل يُشتق على الخادم من الهوية المُمرَّرة، لا من جسم الطلب.
- `Success=false` يبقي `Draft` مع رسالة عربية بحسب `ErrorCode`.
- فشل الحفظ بعد بدء ناجح ← `CancelAsync` بأفضل جهد.
- `AlreadyActive=false` لنسخة منتهية بالمفتاح نفسه ← تُطبَّق نتيجتها عبر `GetStatusAsync`.

---

## In scope

### 1. آلة حالات سجل الفصل (G-SEP-04)

**المشكلة:** لا حالة للسجل. يوجد علمان فقط: `SeparationEventPublished` و`IsCancelled` ([SeparationRecord.cs:36, 47](../../src/Services/HRContracts/HR.Contracts.Domain/Entities/SeparationRecord.cs)). وتُدفع المعاملة إلى `Executing` بمستخدم `null` ([SeparationRecordHandlers.cs:200-211](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `SeparationStatus` | `HR.Contracts.Domain/Enums/SeparationStatus.cs` | `Draft, PendingApproval, Approved, Rejected, Executing, AwaitingIntervention, Completed, Cancelled` (قيم بأسماء عربية في `Display`) |
| حقول `SeparationRecord` | `HR.Contracts.Domain/Entities/SeparationRecord.cs` | `Status`، `WorkflowInstanceId`، `WorkflowCode`، `SubmissionNo`، `SubmittedBy/At`، `DecidedBy/At`، `RejectionReason`، `DecisionId` (FK إلى `AdministrativeDecision`)، `ExecutedBy/At`، `CompletedAt`. `ApprovedBy` يبقى للتاريخ ولا يُكتب بعد الآن |
| `SeparationWorkflowRules` | `HR.Contracts.Domain/Services/SeparationWorkflowRules.cs` | جدول انتقالات صريح `CanTransition(from, to)` و`Transition(record, to, actor, reason)`؛ و`IsOpen(status)` |
| `SeparationStatusSync` | `HR.Contracts.Application/Features/SeparationRecords/SeparationStatusSync.cs` | يحرّك `ServiceTransaction` المربوطة مع كل انتقال، بفاعل حقيقي |
| `SeparationRecordSupport` | الملف نفسه | حذف التحريك الآلي `Draft → … → Executing` |

**القواعد:**

1. الانتقالات المسموحة فقط:
   - `Draft → PendingApproval | Cancelled`
   - `PendingApproval → Approved | Rejected | Draft (سحب) | Cancelled`
   - `Rejected → Draft (إعادة فتح) | Cancelled`
   - `Approved → Executing | Cancelled`
   - `Executing ↔ AwaitingIntervention`
   - `Executing → Completed`
2. `Executing` و`AwaitingIntervention` و`Completed` لا تُلغى في هذه الخطة. رسالة: «إلغاء فصل قيد التنفيذ يتطلب مسار التعويض (PLAN-63)».
3. `SeparationEventPublished` يُشتق: `true` لكل حالة من `Executing` فما بعد. يبقى العمود لأن مستهلكين قائمين يقرؤونه.
4. كل انتقال يكتب صفاً في `ServiceTransactionStateHistory` بفاعل غير فارغ، إلا انتقالات المستهلكين (الفاعل هو `DecidedByUserId` من الحدث).
5. `SeparationRecord` يحمل رمز تزامن `xmin` (`ConfigureOptimisticConcurrency`، نمط `LeaveRequest` في [ContractsDbContext.cs:135](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs)).

### 2. الاعتماد عبر المحرك (G-SEP-04)

**المشكلة:**
- `SEPARATION-WIZARD` يتيم ([WorkflowPackSeeder.cs:32-34](../../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)).
- `ApprovedBy` هو المنشئ ([SeparationWizardController.cs:68](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs)، [SeparationRecordsController.cs:91, 209-210](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)).
- `HrContractsApprove` هو دور المنشئ نفسه ([HrManagementAuthorizationExtensions.cs:126-128](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- الحارس المعماري يبرر الاستثناء بسبب غير صحيح ([WorkflowDefinitionsHaveHandlersTests.cs:18](../../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `SubmitSeparationCommand` + Handler | `HR.Contracts.Application/Features/SeparationRecords/SeparationApprovalHandlers.cs` | يتحقق (§6)، ويختار رمز المسار (`SeparationApprovalRouting`)، ويستدعي `IWorkflowApprovalClient.StartAsync` مع `SubjectEmployeeId = record.EmployeeId`. لا يحفظ `PendingApproval` إلا مع `InstanceId` في الحفظ نفسه. الفشل يعيد خطأً عربياً بحسب `ErrorCode` (ومنها `Conflict` و`Invalid`) ويبقي `Draft` |
| `WithdrawSeparationCommand` | الملف نفسه | `CancelAsync`: إن نجح ← `PendingApproval → Draft`. وإن ردّ `AlreadyTerminal=true` ← لا عودة إلى المسودة، بل تُطبَّق `FinalOutcome` (`Approved`/`Rejected`/`Cancelled`) عبر `ApplyOutcomeAsync` |
| `SeparationApprovalRouting` | `HR.Contracts.Application/Services/SeparationApprovalRouting.cs` | النوع ضمن `Separation:Approval:SensitiveTypes` ← `SEPARATION-APPROVAL-SENSITIVE`، وإلا `SEPARATION-APPROVAL` |
| `SeparationApprovalOutcomeConsumer` | `HR.Contracts.Infrastructure/Consumers/SeparationApprovalOutcomeConsumer.cs` | يرشّح `EntityType == "SeparationRecord"` ويستعمل `WorkflowOutcomeGuard` (PLAN-46)، ويفوّض التطبيق إلى `ApplyOutcomeAsync` المشتركة مع المصالحة. يرفض ويسجّل إن اختلف `InstanceId` عن `record.WorkflowInstanceId` أو لم تكن الحالة `PendingApproval`. يرفض `Approved` إن ظهر المنشئ أو المقدّم بين `Decisions` (دفاع في العمق). ثم ينقل الحالة إلى `Approved` أو `Rejected` (+`RejectionReason`) أو `Draft` عند `Cancelled` |
| بذور التعريفين | `EnsureVersionAsync` (PLAN-43) | `SEPARATION-APPROVAL` و`SEPARATION-APPROVAL-SENSITIVE` بنوع `SeparationRecord` وفق D-SEP-4 وحدود PLAN-46. وسحب `SEPARATION-WIZARD` يملكه PLAN-46، ويُتحقق منه هنا فقط |
| المصالحة | `HR.Contracts.Infrastructure/Workflow/SeparationApprovalOwnerStore.cs` + تسجيل `WorkflowApprovalSweepJob<SeparationApprovalOwnerStore>` في `HR.Contracts.Web/Program.cs` | تلتقط نتيجة فاتها الحدث (§0.1، ما تملكه الخطة 3) |
| قائمة السماح | `WorkflowDefinitionsHaveHandlersTests.cs` | `SeparationRecord` يشير إلى `nameof(SeparationApprovalOutcomeConsumer)` ويُفحص وجود النوع بالانعكاس |

**القواعد:**

1. `ExcludedApproverUserIds = { record.CreatedBy, submitter }`، ويُرفض التقديم إن كان أحدهما فارغاً.
2. `IdempotencyKey` يتضمن `SubmissionNo`، ويزيد عند كل إعادة فتح بعد رفض.
3. المستهلك والمصالحة idempotent: نتيجة لحالة غير `PendingApproval` أو لنسخة غير مرتبطة تُتجاهل مع سجل وعدّاد `workflow_outcome_ignored_total{entity_type="SeparationRecord"}` (من `WorkflowOutcomeGuard`).
4. لا يملك HR.Contracts زر «اعتماد». الاعتماد في صندوق العمل في HR.Core فقط.

### 3. القرار الإداري لإنهاء الخدمة (G-SEP-04)

**المشكلة:** `Documents` نص حر ([SeparationRecord.cs:33](../../src/Services/HRContracts/HR.Contracts.Domain/Entities/SeparationRecord.cs))، ولا رابط بـ `AdministrativeDecision`. أما التعيين فيملك هذا الرابط بنمط [IssueAppointmentDecision.cs:29-80](../../src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/IssueAppointmentDecision.cs).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `IssueSeparationDecisionCommand` + Handler | `HR.Contracts.Application/Features/SeparationRecords/IssueSeparationDecision.cs` | على نمط `IssueAppointmentDecision`: `CodeGenerationRequests.AdministrativeDecision("SEP")` ← `DEC-SEP-YYYY-#####`، و`Subject` «قرار إنهاء خدمة: <الاسم> — <النوع> — نافذ <التاريخ>»، و`RelatedEmployeeId`، وربط `DecisionId`، وإكمال البند `DecisionIssued` |

**القواعد:**

1. يُصدر القرار فقط لسجل `Approved`، ومرة واحدة (الاستدعاء الثاني يعيد القرار نفسه).
2. المُصدر ≠ المنشئ ما دام `IssuerMustDifferFromCreator=true`.
3. لا يُعدَّل القرار بعد الإصدار من شاشة `AdministrativeDecisions/Edit`: يُرفض التعديل لقرار له `SeparationRecord` مرتبط.
4. وثيقة PDF للقرار خارج النطاق (الموجة 2، G-SEP-16).

### 4. قائمة الإخلاء: نمط الإكمال والواجهة (G-SEP-01)

**المشكلة:**
- لا أداة في الواجهة لإكمال أي بند ([Step2.cshtml:55-59](../../src/Services/HRContracts/HR.Contracts.Web/Views/SeparationWizard/Step2.cshtml)). الإجراءان `Step2CompleteStep` (`SeparationWizardController.cs:105-122`) و`OverrideChecklistStep` (`SeparationRecordsController.cs:178-200`) بلا أي مستدعٍ.
- النشر يشترط البنود الست كلها ([SeparationRecordHandlers.cs:152-161](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs))، ومنها بنود لا تقع إلا بعد التنفيذ.
- `CompletedBy = null` يمر (`:240`).
- الاستثناء يأخذ المعتمد الثاني GUID من النموذج (`:274-277`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `SeparationStepCompletionMode` | `HR.Contracts.Domain/Enums/` | `Manual` (يكمله HR بدليل قبل التنفيذ)، `System` (تكمله الخدمة نفسها)، `PartyAck` (يكمله إقرار الطرف بعد التنفيذ) |
| قيم جديدة في `SeparationStep` | `HR.Contracts.Domain/Enums/SeparationStep.cs` | تُضاف في النهاية: `DecisionIssued`، `EmployeeNotified`، `ItAccessRevoked`، `HrCoreUpdated` |
| حقول `SeparationChecklist` | `HR.Contracts.Domain/Entities/SeparationChecklist.cs` | `CompletionMode`، `PartyName`، `ExceptionRequestedBy/At/Reason`، `ExceptionApprovedBy/At` |
| `SeparationChecklistDefaults` | الملف القائم ([:8-20](../../src/Services/HRContracts/HR.Contracts.Domain/Services/SeparationChecklistDefaults.cs)) | `DecisionIssued`=System؛ `EmployeeNotified`=Manual؛ `ContractTerminated`=System عند التنفيذ؛ `PayrollSettled`/`CardRevoked`/`AccountDisabled`/`AssetsReturned`/`Archived`/`ItAccessRevoked`/`HrCoreUpdated`=PartyAck باسم الطرف من `ProcessParties` |
| `CompleteManualStepCommand` | يحل محل `CompleteSeparationChecklistStepCommand` | للبنود `Manual` فقط. الدليل إلزامي (مرجع ≥ 5 أحرف أو `FileId`)، و`CompletedBy` = المستخدم الحالي (الفراغ يُرفض) |
| `RequestStepException` / `ApproveStepException` | `HR.Contracts.Application/Features/SeparationRecords/SeparationChecklistException.cs` | طلبان من جلستين: الأول يسجل السبب (≥ 20 حرفاً). الثاني من مستخدم آخر بـ `HrContractsApprove`، ليس الطالب ولا المنشئ. يُحذف المعامل `approvedBy` من النموذج |
| `SeparationPartyAckChecklistConsumer` | يعمّم `SeparationPartyCompletedChecklistConsumer` ([SagaSupportConsumers.cs:97-119](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs)) | لكل إقرار تنفيذ (لا تعويض) يكمل بند `PartyAck` المطابق للطرف: `IsCompleted`، `EvidenceRef`، `SourceSystem = Party`، `CompletedBy = null` مسموح هنا فقط |
| الواجهة | `Views/SeparationWizard/Step2.cshtml`، `Views/SeparationRecords/Details.cshtml` | جدول البنود: النمط، المالك، الحالة، الدليل. نموذج إكمال لكل بند `Manual`، وطلب استثناء واعتماده، وشريط الحالة ورابط نسخة الاعتماد (`GetStatusAsync`)، وأزرار حسب الحالة: تقديم، سحب، إصدار القرار، تنفيذ، إلغاء |
| `SeparationStepTemplateBackfill` | ترحيل §6 | يضبط `CompletionMode`/`PartyName` للصفوف القائمة، ويضيف البنود الجديدة للسجلات المفتوحة |

**القواعد:**

1. شرط التنفيذ: كل بند `Manual` و`System` قبل التنفيذ مكتمل أو مستثنى باعتماد ثانٍ. أي `DecisionIssued` و`EmployeeNotified`.
2. بنود `PartyAck` لا تُكمَل يدوياً أبداً، ولا تُستثنى من HR.Contracts. تعثّرها يمر بالتدخل البشري (`ProcessInterventions`).
3. يُحذف `Step2CompleteStep`، ويُستبدل `OverrideChecklistStep` بالطلبين المنفصلين.
4. السجل `Completed` يعني أن كل الأطراف الإلزامية أقرّت (يُفحص في اختبار). أما بنود العهدة المفتوحة وحالة براءة الذمة فتُتابَع منفصلة في PLAN-62، ولا تمنع `Completed`.

### 5. التنفيذ وسياسات الإجراءات (G-SEP-04، G-SEP-11)

**المشكلة:**
- `Step3Execute` بلا سياسة على مستوى الخدمة ([SeparationWizardController.cs:153-165](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs)). البوابة وحدها تحصره ([GatewayPathAuthorizationMiddleware.cs:222-226](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
- `Create`/`Edit`/`Delete` بلا سياسة ([SeparationRecordsController.cs:85-144](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)).
- العقد يُنهى عند إكمال خطوة يدوية قبل النفاذ، وبلا تاريخ نهاية ([SeparationRecordHandlers.cs:243-245](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)، [ContractWorkflowRules.cs:33-38](../../src/Services/HRContracts/HR.Contracts.Domain/Services/ContractWorkflowRules.cs)).
- «اليوم» بتوقيت UTC ([SeparationWorkflowRules.cs:23-27](../../src/Services/HRContracts/HR.Contracts.Domain/Services/SeparationWorkflowRules.cs)).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `ExecuteSeparationCommand` | يحل محل `PublishSeparationCommand` | الترتيب: الحالة `Approved` ← القرار موجود ← البنود قبل التنفيذ مكتملة ← تاريخ النفاذ ≤ اليوم بتوقيت دمشق (`ICalendarService.ToLocalTime`) ← الحجز القانوني (عميل PLAN-42، fail-closed) ← نظام الخدمة. ثم في معاملة واحدة مع الـ Outbox: إنهاء العقد (`EndDate = EffectiveDate`) + `ContractTerminated` (System) + `Executing` + `IStartSeparationSaga` |
| `ContractWorkflowRules.Terminate(contract, endDate)` | `HR.Contracts.Domain/Services/ContractWorkflowRules.cs` | يضبط `EndDate` ويرفض تاريخاً قبل `StartDate` |
| سياسات صريحة | `SeparationRecordsController`، `SeparationWizardController` | كل POST: `[Authorize(Policy = HrContractsApprove)]`. القراءة: `ModuleHrContracts`. إن نفّذ PLAN-51 ذلك مسبقاً فيُتحقق منه فقط |
| اختبار معماري | `UnitTests/Architecture/HrContractsSeparationPostPoliciesTests.cs` | كل POST في المتحكمين يحمل سياسة صريحة |
| `CancelSeparationCommand` | الملف القائم | من `Draft`/`PendingApproval`/`Approved` فقط، بسبب إلزامي، ويحدّث المعاملة. ومن `PendingApproval` يستدعي `CancelAsync` أولاً: إن ردّ `AlreadyTerminal` تُطبَّق `FinalOutcome` ويُعاد تقييم الطلب على الحالة الناتجة |
| `SeparationInterventionStatusConsumer` | `HR.Contracts.Infrastructure/Consumers/` | `IProcessInterventionRequested` (نوع Separation) ← `AwaitingIntervention`. والاستئناف من `ProcessInterventionsController.Resume` ← `Executing` |
| `SeparationSagaFinalizedConsumer` | [SagaSupportConsumers.cs:56-94](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs) | `Success=true` ← `Completed`، والمعاملة بـ `SeparationRecordId` لا «آخر معاملة للموظف» |

**القواعد:**

1. لا مسار يبدأ الـ saga إلا `ExecuteSeparationCommand`. اختبار معماري يمنع أي استدعاء آخر لـ `StartSeparationSagaAsync`.
2. تاريخ النفاذ في المستقبل يرفض برسالة تذكر التاريخ بتوقيت دمشق.
3. فحص نظام الخدمة يبقى بمنطقه الحالي هنا. نقله إلى الإنشاء وكتالوج الأنواع ← PLAN-61. شحن الحزمة وعنوان HR.Core ← PLAN-37.

### 6. سلامة السجل (G-SEP-11)

**المشكلة:**
- الإنشاء لا يتحقق من الموظف ولا من سجل مفتوح آخر ([SeparationRecordHandlers.cs:56-88](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)، [SeparationWizardController.cs:54-87](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationWizardController.cs)).
- لا فهرس فريد ([ContractsDbContext.cs:107-120](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs)).
- العقد لا يُتحقق من انتمائه ([SeparationRecordSupport.cs:21-29](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordSupport.cs))، وقائمة العقود آخر 500 لكل الموظفين ([ContractLookupHandlers.cs:12-19](../../src/Services/HRContracts/HR.Contracts.Application/Features/Lookup/ContractLookupHandlers.cs)).
- التعديل يغيّر الموظف والعقد بعد إكمال البنود ([SeparationRecordHandlers.cs:90-109](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)).
- المعاملة غير مربوطة بالسجل ([ServiceTransaction.cs:26-54](../../src/Services/HRContracts/HR.Contracts.Domain/Entities/ServiceTransaction.cs))، و`Advance` يحرّكها يدوياً ([ServiceTransactionsController.cs:58-89](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/ServiceTransactionsController.cs)).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `SeparationRecordFactory` | `HR.Contracts.Application/Features/SeparationRecords/SeparationRecordFactory.cs` | مسار إنشاء واحد للمتحكمين والمعالج والمدخل الوارد (`ProcessSeparationRequestHandler`): التحقق، ثم البذر، ثم المعاملة المربوطة |
| تحقق الموظف | عميل دورة الحياة من PLAN-42 (`api/internal/employees/{id}/lifecycle-flags`) | الموظف موجود، وحالته غير نهائية (`EmploymentStatusRules.IsTerminal`). الفشل أو عدم التوفر يرفض (fail-closed) |
| تحقق العقد | `SeparationRecordFactory` | `ContractId` عقد للموظف نفسه بحالة `Active`/`Pending`. وإن وُجد عقد نشط واحد يُختار تلقائياً، ومع أكثر من عقد يصبح الاختيار إلزامياً |
| `GetEmployeeContractsLookupQuery` | `Features/Lookup/ContractLookupHandlers.cs` | عقود الموظف المختار فقط، ويستبدل القائمة العامة في نماذج الفصل |
| فهرس فريد جزئي | `ContractsDbContext` + ترحيل | `IX_SeparationRecords_Tenant_Employee_Open` على `(TenantId, EmployeeId)` حيث `Status IN ('Draft','PendingApproval','Approved','Executing','AwaitingIntervention')`. الانتهاك ← رسالة «يوجد سجل فصل مفتوح لهذا الموظف» |
| قفل الجوهر | `UpdateSeparationRecordHandler` | التعديل في `Draft` فقط. وبعد اكتمال أي بند يُقفل `EmployeeId` و`ContractId` |
| `ServiceTransaction.SeparationRecordId` | الكيان + فهرس فريد مشروط | الربط الصريح، وكل البحث به. و`ServiceTransactionsController.Advance` يرفض معاملات `Separation` («الحالة يملكها سجل الفصل») |
| الترحيل | `HR.Contracts.Infrastructure/Migrations/<ts>_SEP060_SeparationStatusGovernance.cs` | الأعمدة، والفهارس، و`xmin`، وملء الحالة (أدناه)، وإلغاء التكرارات قبل إنشاء الفهرس |

**ملء البيانات القائمة في الترحيل (SQL حتمي):**

1. `IsCancelled` ← `Cancelled`.
2. `SeparationEventPublished` مع حالة الـ saga `Completed` ← `Completed`. ومع `AwaitingIntervention` ← `AwaitingIntervention`. ومع `Cancelled` ← `Cancelled`. وغير ذلك ← `Executing`.
3. الباقي ← `Draft` (يجب أن يمر بالاعتماد).
4. التكرارات المفتوحة لكل `(TenantId, EmployeeId)`: يبقى الأحدث، والباقي `Cancelled` بسبب «PLAN-60: سجل مكرر».
5. ربط المعاملات القائمة: أقرب معاملة `Separation` للموظف بفارق إنشاء ≤ 5 دقائق. والتقرير بما لم يُربط في `scripts/sep060-report-unlinked.ps1`.

---

## Out of scope

| البند | الخطة |
|-------|-------|
| آلية الاعتماد العابر للخدمات نفسها (API البدء، الحدث، فرض الدور والاستبعاد، الصندوق)، وجهة HR.Core للفصل (مدخل السجل وسحب `SEPARATION-WIZARD`)، والأساس المشترك للمصالحة | PLAN-46 |
| عميل الحجز القانوني المصادَق عليه وfail-closed (G-SEP-12) | PLAN-42 |
| شحن `service-regimes.json` وعنوان HR.Core ونطاقه لـ HR.Contracts | PLAN-37 |
| كتالوج الأنواع لكل نظام، ونوع الوفاة، وفحص النوع عند الإنشاء، والمدخل الواحد | PLAN-61 |
| صدق إقرارات الأطراف (Keycloak، العهدة، التأمين) | PLAN-62 |
| إلغاء ما بعد التنفيذ، والتعويض، وإخلاء المنصب، وإعادة التعيين | PLAN-63 |
| التسوية النهائية | PLAN-64 |
| PDF القرار، وتنفيذ مجدول آلي، وESS | الموجة 2 (SEP-G) |
| نطاق الوحدة على شاشات الفصل | PLAN-52 (IAM-D) |

## Acceptance scenarios

على الملف الشبيه بالإنتاج (`docker-compose.acceptance-prodlike.yml` بعد PLAN-37). الشخصيات: `a-contracts-maker` (منشئ)، و`a-contracts-checker` (مُصدر القرار والمنفّذ)، و`central-a` (`hr-core`)، و`a-management-approver`، و`a-compliance`، وموظف مخصص جديد `a-leaver` (`hr-employee`، سجل مُبذَر بعقد نشط).

**عزل بيانات الفصل (يلزم PLAN-61..65):** `a-leaver` وكل `a-leaver-*` لاحقاً **قوالب** لا موظفون يُستهلكون مرة. مساعد جديد `AcceptanceLeaverFixture.CreateAsync(template)` في `src/Tests/HRManagement.AcceptanceTests/` ينشئ لكل سيناريو يفصل موظفاً نسخة جديدة من القالب (موظف وعقد نشط ونظام خدمة) عبر واجهات HR.Core وHR.Contracts، ويعيد معرّفها. فلا يتصادم سيناريو مع «سجل فصل مفتوح» أو «حالة نهائية» تركها سيناريو أو خطة سابقة، ويُعاد التشغيل على المكدّس نفسه بلا إعادة ضبط (PLAN-65، AC-V1-65c). وتوسّعه PLAN-61..64 بالتجهيزات التي تحتاجها، ولا تعدّل الشخصية المشتركة.

| ID | Positive | Negative |
|----|----------|----------|
| AC-SEP-60a | `a-contracts-maker` ينشئ فصلاً بالاستقالة ويقدّمه ← `PendingApproval` و`WorkflowInstanceId ≠ null` ← `central-a` يعتمد الخطوة 1 من صندوق العمل ← `a-management-approver` يعتمد الخطوة 2 ← الحالة `Approved` خلال 30 ثانية | — |
| AC-SEP-60b | `a-contracts-checker` يصدر القرار ← `DEC-SEP-2026-#####` ← يكمل `EmployeeNotified` بدليل من الواجهة ← ينفّذ ← `Executing`، والعقد `Terminated` بتاريخ نهاية = النفاذ | التنفيذ قبل القرار ← رفض. وتنفيذ بتاريخ نفاذ غداً ← رفض يذكر التاريخ |
| AC-SEP-60c | — | `a-contracts-maker` يرسل POST اعتماد خطوته الخاصة في HR.Core ← رفض المحرك (مستبعد). و`a-contracts-maker` يصدر القرار لسجله ← رفض |
| AC-SEP-60d | — | عبر البوابة: `a-management-approver` يرسل POST `Step3Execute` ← 403. وفي اختبار تكامل للخدمة وحدها (`WebApplicationFactory`، دون البوابة) بالدور نفسه ← 403 من سياسة الإجراء، لا من البوابة |
| AC-SEP-60e | — | سجل ثانٍ مفتوح للموظف نفسه ← رفض. سجل بعقد موظف آخر ← رفض. وتعديل `EmployeeId` بعد التقديم ← رفض |
| AC-SEP-60f | طلب استثناء على `EmployeeNotified` من الصانع واعتماده من المدقق ← البند مستثنى ويُسمح بالتنفيذ | اعتماد الاستثناء من الطالب نفسه ← رفض |
| AC-SEP-60g | رفض الخطوة 2 بسبب ← `Rejected` بالسبب، ولا saga، ولا تغيير في Payroll | — |
| AC-SEP-60h | مسار حساس (`Disciplinary`) ← ثلاث خطوات منها `a-compliance` | `central-a` يحاول اعتماد خطوة `compliance` ← رفض الدور |
| AC-SEP-60i | نتيجة فاتها الحدث (حذف رسالة النتيجة من الطابور في التجهيز) ← المصالحة تطبّقها خلال `WorkflowApproval:Sweep:StaleAfterMinutes` + `IntervalMinutes` (قيم ملف القبول). وسحب طلب انتهى اعتماده ← `Approved` لا `Draft` | نشر `IWorkflowOutcomeDecided(Approved)` بنسخة غير مرتبطة (من أداة الاختبار) ← تجاهل مسجل، والحالة تبقى `PendingApproval` |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "Sep060|SeparationWorkflowRules|SeparationChecklist|HrContractsSeparationPostPolicies|WorkflowDefinitionsHaveHandlers"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Sep060|Fun002|Int003|G3Separation"
dotnet ef migrations has-pending-model-changes -p src/Services/HRContracts/HR.Contracts.Infrastructure -s src/Services/HRContracts/HR.Contracts.Web
# قبول بالملف الشبيه بالإنتاج
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --build --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcSep60"
```

**اختبارات جديدة:**
- **Unit:**
  - `Sep060SeparationStateMachineTests`: كل انتقال مسموح وممنوع.
  - `Sep060ChecklistModeTests`: لا إكمال يدوي لبند `PartyAck`، والاستثناء من جلستين.
  - `Sep060ApprovalRoutingTests`.
  - `Sep060OutcomeConsumerTests`: نسخة غير مطابقة، وتكرار، و`Cancelled`.
  - `Sep060SweepAndWithdrawTests`: المصالحة تطبّق نتيجة منتهية مرة واحدة، والسحب مع `AlreadyTerminal` يطبّق `FinalOutcome`.
  - `HrContractsSeparationPostPoliciesTests`.
- **Integration على PostgreSQL** (`PostgreSqlFixture`)، `Sep060SeparationPostgresIntegrationTests`:
  - الفهرس الجزئي يرفض سجلاً مفتوحاً ثانياً، ويقبله بعد الإلغاء.
  - `xmin`: تقديمان متزامنان ينجح أحدهما فقط.
  - التنفيذ يحفظ إنهاء العقد و`Executing` ورسالة `IStartSeparationSaga` في صف Outbox واحد أو لا شيء.
  - ترحيل الملء على بيانات مزروعة بالحالات الأربع والتكرار.

## Definition of done

- لا مسار في الشيفرة يضع سجلاً في `PendingApproval` بلا `WorkflowInstanceId`، ولا مسار يبدأ الـ saga إلا `ExecuteSeparationCommand`.
- رحلة الفصل تكتمل من الواجهة حتى `Executing` بإعدادات الإنتاج، دون أي POST مصنوع.
- لا يظهر `approvedBy` في أي نموذج، و`ApprovedBy` لا يُكتب.
- AC-SEP-60a..i = Pass، ومجلد `acceptance-evidence/AC-SEP-60/` بالعناصر الثمانية.
- سطور «الحالة» لـ G-SEP-01 وG-SEP-04 وG-SEP-11 في الدراسة محدّثة بالخطة والدليل.
- العمل في commits محلية على الفرع: لا دفع إلى المستودع البعيد (PLAN-35 للمالك)، ولا أسرار ولا `global.json` ولا `testdata/*/out/*.json` معاد كتابتها.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-60-sep-unblock-decision-governance.md` |
| Domain | `SeparationStatus.cs`، `SeparationStepCompletionMode.cs`، تحديث `SeparationRecord.cs`/`SeparationChecklist.cs`/`SeparationStep.cs`/`SeparationWorkflowRules.cs`/`SeparationChecklistDefaults.cs`/`ContractWorkflowRules.cs` |
| Application | `SeparationApprovalHandlers.cs`، `IssueSeparationDecision.cs`، `SeparationChecklistException.cs`، `SeparationRecordFactory.cs`، `SeparationApprovalRouting.cs`، `SeparationStatusSync.cs` |
| Infrastructure | `SeparationApprovalOutcomeConsumer.cs`، `SeparationInterventionStatusConsumer.cs`، تعميم مستهلك الإقرار، ترحيل `SEP060_SeparationStatusGovernance` |
| HR.Core | بذور `SEPARATION-APPROVAL` و`SEPARATION-APPROVAL-SENSITIVE` عبر `EnsureVersionAsync` (سحب `SEPARATION-WIZARD` في PLAN-46) |
| Web | المتحكمان والصفحات `Step1..4`، `Details`، `Index` (عمود الحالة) |
| Config | `Separation:Approval`، `Separation:Decision`، `Separation:Checklist` في `appsettings.json`؛ ومدخل `calls[]` لـ `svc-hr-contracts` ← `api/workflow-instances` (`WorkflowExternalStart`، بتسجيل `AddWorkflowApprovalClient`) في `deploy/wiring/service-wiring.json` (PLAN-37) إن لم يضفه PLAN-46 |
| Tests | الاختبارات أعلاه، و`AcSep60SeparationGovernanceTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، و`AcceptanceLeaverFixture.cs`، والشخصية `a-leaver` في `testdata/acceptance/personas.json` و`AcceptancePersonaCatalog.cs` (+1 على عدد الشخصيات وقت التنفيذ في مولّد realm) |
| Scripts | `scripts/sep060-validate.ps1`، `scripts/sep060-report-unlinked.ps1` |
| Docs | `FUN_002_JOURNEY_MATRIX.md` و`fun-separation/journey-matrix.json` (البنود وأنماطها)، `FUN_002_EXCEPTION_PATHS.md` (الإلغاء حسب الحالة)، `ADR-FUN-002` (ملحق القرار)، `PROCESS_SAGAS.md` (§DOM-001 gate)، وسطور «الحالة» في الدراسة |

## Risks

| الخطر | التخفيف |
|-------|---------|
| انحراف لاحق في أسماء PLAN-46 عن §0.1 (مطابقة حتى 2026-10-30) | PLAN-46 هو المرجع؛ محوّل رقيق عند الحاجة، والقرار في سجل التقدم. لا يُعاد بناء الآلية هنا |
| سجلات قائمة تصبح `Draft` وتحتاج اعتماداً | تقرير المرحلة 0.5، ورسالة واضحة في القائمة «سجل قديم — يتطلب اعتماداً» |
| `compliance` لا يرى عنصر الفصل في صندوق العمل | الضمانة 2 في العقد (PLAN-46: الصندوق لـ `compliance`). وإن غابت تُسجَّل فرقاً في عقد PLAN-46 في `reports/plan60-progress.md`، ويُشغَّل AC-SEP-60h بما يتاح |
| تغيير `SeparationStep` يكسر قيماً مخزّنة نصاً | الإضافة في نهاية التعداد، والتخزين `HasConversion<string>` قائم |
| البوابة تفرض `HrContractsApprove` على كل POST فتحجب أدواراً أخرى | كل أفعال هذه الخطة لحاملي `hr-contracts`. الاعتماد في HR.Core، فلا تغيير في البوابة |

## Effort estimate

9–10 أيام عمل: آلة الحالات والترحيل والملء (2)، ربط الاعتماد والمستهلك والبذور (2)، القرار (0.5)، القائمة والواجهة والاستثناء (2)، التنفيذ والسياسات (1)، سلامة السجل (1)، القبول والأدلة والتوثيق (1.5).
