# PLAN-40 — نشر الأحداث ذرياً عبر الخدمات وحارس يمنع العودة (MSG)

**Scope:** MSG — [دراسة العمليات](../PRG_002_OPERATIONS_GAP_STUDY.md): G-OPS-08 (النشر بلا حفظ، والنشر غير الذري، والنشر من المستهلكين خارج الـ outbox، وإيصالات HR.Training)؛ [دراسة سير العمل](../WF_001_GAP_STUDY.md): G-WF-03؛ جزء النشر من G-MOV-05 في [دراسة الحركات](../FUN_003_MOVEMENTS_GAP_STUDY.md)؛ تقاطع POL (`PolicyPublished`)؛ تقاطع IDA→OPS في [دراسة الهوية والأصول](../FUN_005_IDENTITY_ASSETS_IT_GAP_STUDY.md) (§4.5: إقرارات الأطراف من `ConsumeContext`)  
**Status:** Proposed — 2026-10-30  
**Priority:** P1 (فقد صامت لأحداث تمس الدرجة والعهدة والسياسات)  
**Depends on:** PLAN-34. يستخدم الملف الشبيه بالإنتاج من PLAN-37 للقبول، وإضافة الرسائل المؤجلة في RabbitMQ من PLAN-38  
**Related cards:** INT-001، INT-002، INT-003 (completion-backlog 01/02)، PLAN-05 (AC-15..18)

## Goal

ألا يُفقد حدث مجال واحد بصمت، وألا يُنشر حدث لحالة لم تُلتزم:

- كل `Publish` في خدمة ذات outbox يُكتب في `OutboxMessage` **في المعاملة نفسها** التي تلتزم الحالة.
  - لا نشر بعد آخر `SaveChanges`.
  - ولا «حفظ ← نشر ← حفظ» بلا معاملة واحدة.
- ما ينشره المستهلكون والـ sagas (إقرارات الأطراف، أحداث الأرشيف) يمر بـ outbox المستهلك. فلا يصل إقرار عن عمل لم يُلتزم، ولا يضيع إقرار عن عمل التُزم.
- حارسان يمنعان العودة:
  - **ثابت** يفحص الشيفرة في الاختبارات؛
  - **وقت التشغيل** يكشف أي `OutboxMessage` أُضيف ولم يُحفظ عند نهاية الطلب.
- ما فُقد قبل الإصلاح يُعاد نشره بأداة قابلة للتشغيل التجريبي، حيث يمكن ذلك بأمان.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-40-1 آلية الذرية في المستهلكين | outbox المستهلك من MassTransit (`UseEntityFrameworkOutbox<TDbContext>`) على كل نقاط استقبال الخدمات التي تنشر من مستهلك أو saga | §4 |
| D-40-2 أسلوب إصلاح «حفظ ← نشر ← حفظ» | تعديل ← نشر ← حفظ واحد، لأن المعرّفات تُولَّد في العميل ([BaseEntity.cs:8](../../src/Shared/HRManagement.SharedKernel/Entities/BaseEntity.cs)). معاملة صريحة عبر `OutboxUnitOfWork` حيث تلزم قيمة من القاعدة (تسلسل، `ExecuteUpdate`) | §3 |
| D-40-3 معرّف رسالة حتمي للأحداث المصلحة والمعاد نشرها | `MessageId = UUIDv5(namespace, "<EventType>:<AggregateId>:v1")`، فالإعادة وإيصالات Inbox تمنعان التكرار | §2، §6 |
| D-40-4 نطاق الإعادة | الترفيع، والندب، ونتائج العهدة والفقد، والوحدات التنظيمية. `PolicyPublished` لا يُعاد (ذاكرة السياسات تُبنى عند الإقلاع). و`IAgencyOnboarded` لا مستهلك له. الافتراض تشغيل تجريبي | §6 |
| D-40-5 سلوك حارس وقت التشغيل | يرمي في Development وAcceptance والاختبارات (`Messaging:FailOnUnsavedOutbox=true`)، ويسجّل خطأً ومقياساً في Production | §5 |
| D-40-6 الإقرار الفاشل في قاعدة الأطراف | الفشل التجاري ينشر `*PartyFaulted` ويُلتزم دون إعادة رمي. الفشل العابر يُعاد رميه دون نشر ما بقيت محاولات، وفي المحاولة الأخيرة ينشر `*PartyFaulted` مرة ويعود. فيبقى عقد PLAN-62 (Keycloak متوقف ← `Faulted` ← تدخل) قائماً بعد المحاولات | §4 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-40-msg-atomic-event-publishing` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet test … --filter "OutboxAtomicity\|InboxIdempotency\|Qa003Outbox\|RabbitDownOutbox\|Int001\|Int002"` | أخضر أو إخفاقات موثقة في `reports/plan40-baseline.md` |
| 0.3 | جرد آلي للناشرين | ماسح Roslyn مؤقت (يصبح الحارس في §5) على `src/Services/**` و`src/Shared/**` | `reports/plan40-publisher-inventory.md`، وكل موقع مصنّف: **A** نشر بلا حفظ لاحق؛ **B** حفظ ← نشر ← حفظ بلا معاملة؛ **C** نشر من مستهلك أو saga بلا outbox مستهلك؛ **OK** |
| 0.4 | تأكيد حي للصنف A | على مكدّس القبول: تنفيذ ترفيع، ثم `SELECT count(*) FROM "OutboxMessage"` في `hr_movements_db` قبل التسليم وبعده، وإيصال `IPromotionExecuted` في `hr_core_db` | الرقم مسجّل؛ والمتوقع: لا صف ولا إيصال |
| 0.5 | مقارنة بالجرد الموثق | مقابل [EVENT_PUBLISHER_INVENTORY.md:30-82](../EVENT_PUBLISHER_INVENTORY.md) | قائمة الأحداث الغائبة عن الجرد |

## In scope

### 1. أداة وحدة العمل الذرية

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `OutboxUnitOfWork` | `src/Shared/HRManagement.ServiceDefaults/Messaging/OutboxUnitOfWork.cs` (جديد) | `ExecuteAsync(DbContext db, Func<CancellationToken, Task> body, ct)`: داخل `db.Database.CreateExecutionStrategy().ExecuteAsync` يفتح معاملة، وينفذ الجسم (يسمح بحفظ وسيط وبـ `ExecuteUpdate`)، ثم `SaveChangesAsync` و`Commit`. إن وُجدت معاملة محيطة ينضم إليها ولا يلتزم |
| `[StagesOutboxMessages]` | `src/Shared/HRManagement.ServiceDefaults/Messaging/StagesOutboxMessagesAttribute.cs` (جديد) | يوسم دالة تنشر ولا تحفظ عمداً (الغلاف)، فيصبح الحفظ واجب المستدعي، ويفحص الحارس الثابت المستدعين |
| `DeterministicMessageId` | `src/Shared/HRManagement.ServiceDefaults/Messaging/DeterministicMessageId.cs` (جديد) | UUIDv5 لـ D-40-3، ويُمرَّر بـ `ctx => ctx.MessageId = …` في `Publish` |

### 2. الصنف A: نشر بعد آخر حفظ (G-WF-03، G-MOV-05، POL، G-OPS-08)

**المشكلة:** في bus outbox ([MassTransitExtensions.cs:112-121](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)) لا تُكتب الرسالة إلا عند `SaveChanges` لاحق ([EVENT_PUBLISHER_INVENTORY.md:95](../EVENT_PUBLISHER_INVENTORY.md)). المواقع:

| الحدث | الموقع | الإصلاح |
|-------|--------|---------|
| `IPromotionExecuted` | [MovementsExtendedControllers.cs:1032-1048](../../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs) | النشر قبل الحفظ الوحيد، بمعرّف حتمي من `PromotionRequestId` |
| `IDelegationExecuted` | [MovementsExtendedControllers.cs:1189-1202](../../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs) | كذلك من `DelegationRequestId` |
| `IAssetAssignmentApproved/Rejected`، `IAssetLossApproved/Rejected` | `ApproveAsync`/`RejectAsync` يحفظان ثم ينشران ([WorkflowInstanceActionService.cs:117-120,164-166](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs))، وفرع الأصول يعود دون حفظ ([:171-187,208-266](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)) | تغليف `ApproveAsync` و`RejectAsync` في `OutboxUnitOfWork`. المعالجات (التوظيف والملاك) تبقى تحفظ داخلها، والحفظ الختامي يلتزم الـ outbox في المعاملة نفسها. معرّف حتمي من `WorkflowInstanceId` |
| `IOrgUnitChanged` (المزامنة الكاملة) | `PublishAllOrgUnitsAsync` بلا حفظ ([OrgUnitIntegrationService.cs:31-36](../../src/Services/HRCore/HR.Core.Infrastructure/Integration/OrgUnitIntegrationService.cs)). عند الإقلاع هو آخر استدعاء في النطاق، فتضيع كل الرسائل ([HR.Core Program.cs:309](../../src/Services/HRCore/HR.Core.Web/Program.cs)). أما في التهيئة ([AgencyOrgStructureProvisioner.cs:75-76](../../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyOrgStructureProvisioner.cs)) فيلتزمها حفظ لاحق في النطاق نفسه ([AgencyProvisioningService.cs:167](../../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs))، لكن بلا معاملة واحدة. ومستدعو المتحكمات «حفظ ← نشر ← حفظ» ([OrgUnitsController.cs:187-192,257-268,274-277](../../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)، [OrgStructureApiController.cs:62-67,73-76](../../src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs))، وهؤلاء صنف B (§3) | `SaveChangesAsync` في نهاية `PublishAllOrgUnitsAsync`. ووسم `PublishOrgUnitChangedAsync` و`PublishManyOrgUnitsChangedAsync` بـ `[StagesOutboxMessages]` |
| `IAgencyOnboarded` | [AgencyProvisioningService.cs:167-178](../../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)، ولا مستهلك له اليوم | النشر قبل الحفظ |
| `IPolicyPublished` | ينشره `PoliciesController.Publish` بعد حفظ `PublishDraftAsync` ([PoliciesController.cs:117-127](../../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs))؛ والمسار من الواجهة لا ينشره أصلاً ([PolicyTemplatesController.cs:194](../../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)) | الحد الأدنى: نقل النشر إلى داخل `PolicyVersionPublisher.PublishDraftAsync` قبل حفظه ([PolicyVersionPublisher.cs:58](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyVersionPublisher.cs))، فيغطي المسارين، وحذفه من المتحكم. أما خدمة النشر الواحدة، والحمولة الموسعة (`TenantId`، `VersionNumber`، `EffectiveFrom`)، وإبطال الكاش في كل نسخ HR.Core، فهي لـ PLAN-47 §8 الذي يبني على هذا الموقع |
| أي موقع إضافي من جرد 0.3 | — | الإصلاح نفسه، ويُسجَّل في الجرد |

**القواعد:**
1. في كل موقع: لا تُرد الواجهة «تم النشر» قبل أن يلتزم صف `OutboxMessage`.
2. لا تغيير على مستهلكي هذه الأحداث، عدا ما في §4. وحماية العهدة من الإعادة قائمة أصلاً ([AssetAssignmentWorkflowConsumers.cs:26,57](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/AssetAssignmentWorkflowConsumers.cs)).
3. **دلالة** الترفيع (درجة الموظف في HR.Core، ومستهلك Payroll) ليست هنا (انظر Out of scope). هنا يصل الحدث فقط.

### 3. الصنف B: «حفظ ← نشر ← حفظ» بلا معاملة (G-OPS-08)

**المشكلة:**
- 21 ناشراً موثقاً بهذا النمط ([EVENT_PUBLISHER_INVENTORY.md:34-60](../EVENT_PUBLISHER_INVENTORY.md)). والوثيقة تجعله «النمط المطلوب» ([:82-95](../EVENT_PUBLISHER_INVENTORY.md)).
- مثاله ترحيل الرواتب ([PayrollRunHandlers.cs:260-264](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)).
- والنمط نفسه في `EmployeeIntegrationService.CreateEmployeeWithEventAsync` ([EmployeeIntegrationService.cs:16-52](../../src/Services/HRCore/HR.Core.Infrastructure/Integration/EmployeeIntegrationService.cs))، وفي مستدعي `PublishStatusChangedIfNeededAsync`، مثل [EmployeeLifecycleController.cs:60-61,97-98,126-127](../../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeLifecycleController.cs).
- انقطاع بين الحفظين يترك الحالة ملتزمة والحدث مفقوداً، وإعادة المحاولة تُرفض لأن الحالة تغيرت.

**التصميم:**
- لكل موقع في الجرد: «تعديل ← نشر ← حفظ واحد» حيث لا تلزم قيمة من القاعدة.
- وإلا `OutboxUnitOfWork.ExecuteAsync`. أمثلة: `HireCaseHandlers` (ExecuteUpdate ← نشر ← حفظ)، والمواقع التي تولّد رقماً من COD قبل الحدث.
- `EmployeeIntegrationService`: `CreateEmployeeWithEventAsync` يصبح حفظاً واحداً.
- `PublishStatusChangedIfNeededAsync` يُوسم `[StagesOutboxMessages]` ويكف عن الحفظ، فيحفظ المستدعون مرة واحدة بعده.
- تحديث [EVENT_PUBLISHER_INVENTORY.md](../EVENT_PUBLISHER_INVENTORY.md): يُحذف «Save → Publish → Save» من «النمط المطلوب»، وعمود «النمط» لكل صف يصبح `SingleSave` أو `OutboxUnitOfWork`، ويُضاف كل حدث غاب عن الجرد.

**القواعد:**
1. لا موقع يحفظ الحالة في معاملة ويكتب الـ outbox في معاملة أخرى.
2. فشل أي جزء يعيد الحالة كما كانت. يثبت ذلك اختبار تكامل بحاقن فشل على الحفظ الأخير.

### 4. الصنف C: النشر من المستهلكين والـ sagas (تقاطع IDA→OPS، G-OPS-08)

**المشكلة:**
- إقرارات الأطراف تُنشر من `ConsumeContext` ([PartyAckConsumerBase.cs:30-51](../../src/Shared/HRManagement.ServiceDefaults/PartyAckConsumerBase.cs))، وتستخدمها 7 خدمات. والنمط نفسه في:
  - [ProcessPartyAckConsumers.cs:43-117](../../src/Services/HRCore/HR.Core.Infrastructure/Consumers/ProcessPartyAckConsumers.cs)؛
  - [PayrollEventConsumers.cs:68-202](../../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)؛
  - [SagaSupportConsumers.cs:139,152](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs)؛
  - [LeaveAndAdvanceApprovedConsumer.cs:28,59](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs).
- لا outbox مستهلك مهيأ في أي خدمة. bus outbox وحده ([MassTransitExtensions.cs:112-121](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)) لا يغطي `ConsumeContext`. فالإقرار يُرسل فوراً **داخل** معاملة `InboxConsumeFilter` ([IdempotentConsumerFilter.cs:63-101](../../src/Shared/HRManagement.ServiceDefaults/IdempotentConsumerFilter.cs)) وقبل التزامها.
- Admin.Logistics وAuth.Directory وIT.Infra وRecords.Archive وDev.Analytics بلا outbox أصلاً. والأخيرتان تنشران `IExportJobRequested` من طلبات الويب بعد الحفظ مباشرة إلى الوسيط ([ArchiveExportJobService.cs:59-67](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/ExportJobs/ArchiveExportJobService.cs)، [AnalyticsExportJobService.cs:62-70](../../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/ExportJobs/AnalyticsExportJobService.cs)).
- قاعدة الأطراف تنشر `*PartyFaulted` ثم تعيد الرمي، فيتكرر النشر مع كل محاولة.
- `TrainingDbContext` بلا إيصالات ([TrainingDbContext.cs:132-134](../../src/Services/HRTraining/HR.Training.Infrastructure/Data/TrainingDbContext.cs))، فيتجاوز الفلتر مستهلكيه ([IdempotentConsumerFilter.cs:49-55](../../src/Shared/HRManagement.ServiceDefaults/IdempotentConsumerFilter.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| outbox المستهلك | `AddPostgresOutbox<TDbContext>(consumerOutbox: true)` في `MassTransitExtensions.cs` | `x.AddConfigureEndpointsCallback((ctx, name, cfg) => cfg.UseEntityFrameworkOutbox<TDbContext>(ctx))` بجانب استدعاء quorum الحالي ([:51-58](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)). يشمل نقاط الـ sagas، وهي تستخدم `ExistingDbContext` للخدمة نفسها ([HR.Contracts Program.cs:65-72](../../src/Services/HRContracts/HR.Contracts.Web/Program.cs)، [HR.Movements Program.cs:49-60](../../src/Services/HRMovements/HR.Movements.Web/Program.cs)) |
| outbox للخدمات الخمس | `Program.cs` و`*DbContext.cs` لـ AdminLogistics وAuthDirectory وITInfrastructure وRecordsArchive وDevAnalytics | `configureOutbox: x => x.AddPostgresOutbox<…>(consumerOutbox: true)`، و`modelBuilder.ConfigureMassTransitOutbox()`، وترحيل `MSG040_AddMassTransitOutbox` لكل منها |
| الثماني القائمة | `Program.cs` لـ HRCore وHRContracts وPayroll وHRMovements وHRHealth وIDCard وCompliance وHRTraining | `consumerOutbox: true` |
| `InboxConsumeFilter` بوضع الانضمام | [IdempotentConsumerFilter.cs](../../src/Shared/HRManagement.ServiceDefaults/IdempotentConsumerFilter.cs) | حين يكون outbox المستهلك مفعلاً لهذا الـ DbContext: لا `BeginTransaction`/`Commit`. يفحص الإيصال ويضيفه **قبل** `next.Send`، فيحفظه `SaveChanges` الذي يجريه outbox MassTransit في معاملته. التكرار يمنعه `InboxState` والفهرس الفريد للإيصالات معاً. الوضع القديم يبقى للخدمات بلا outbox (لا شيء بعد هذه الخطة) |
| إيصالات Training | `TrainingDbContext.cs` | `modelBuilder.ConfigureIntegrationMessageReceipts()` وترحيل `MSG040_IntegrationMessageReceipts` |
| قاعدة الأطراف | [PartyAckConsumerBase.cs](../../src/Shared/HRManagement.ServiceDefaults/PartyAckConsumerBase.cs) (الأصناف الأربعة) | `MessagingFaults.IsTransient(ex)` (جديد: `DbUpdateConcurrencyException`، `NpgsqlException` العابرة، `TimeoutException`، `TaskCanceledException` بلا إلغاء من المستدعي، و`HttpRequestException`). العابر يُعاد رميه دون نشر ما دام `context.GetRetryAttempt()` أقل من `MassTransit:RetryCount` (افتراضه 3، [MassTransitExtensions.cs:127-155](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs))؛ وفي المحاولة الأخيرة يُعامل معاملة التجاري. التجاري ينشر `*PartyFaulted` مرة ويعود بلا رمي، فيُلتزم مع الإيصال. واليوم يُنشر `Faulted` مع كل محاولة ثم يُعاد الرمي ([PartyAckConsumerBase.cs:40-51](../../src/Shared/HRManagement.ServiceDefaults/PartyAckConsumerBase.cs)) |

**القواعد:**
1. لا رسالة من مستهلك تغادر قبل التزام معاملته. وفشل الالتزام لا يترك رسالة على الوسيط.
2. رسالة مكررة لا تنتج أثراً ثانياً ولا إقراراً ثانياً.
3. ترتيب المرشحات (إيصال داخل outbox المستهلك أو خارجه) يُثبت باختبار تكامل حقيقي. أي تعارض معاملات (`already an active transaction`) يفشل الاختبار ويوقف الخطة عند المرحلة 4.

### 5. الحارسان (منع العودة)

| Artifact | المسار | الغرض |
|----------|--------|--------|
| الحارس الثابت | `src/Tests/HRManagement.UnitTests/Architecture/OutboxPublishOrderingArchitectureTests.cs` (جديد) | تحليل بأشجار Roslyn (`Microsoft.CodeAnalysis.CSharp`، مثبت في `Directory.Packages.props`)، مع بديل نصي بعدّ الأقواس على نمط [AcceptanceSkipPatternArchitectureTests.cs](../../src/Tests/HRManagement.UnitTests/Architecture/AcceptanceSkipPatternArchitectureTests.cs) إن تعذر تثبيت الحزمة |
| القواعد المفحوصة | ↑ | **R1:** استدعاء `Publish`/`Send` على `IPublishEndpoint`/`ISendEndpointProvider`، أو دالة موسومة `[StagesOutboxMessages]`، يليه `SaveChanges*` في الدالة نفسها، أو الدالة نفسها موسومة. **R2:** لا تسلسل حفظ ← نشر ← حفظ خارج `OutboxUnitOfWork.ExecuteAsync`. **R3:** `context.Publish`/`ctx.Publish` في مستهلك مسموح فقط في خدمة `consumerOutbox: true` (يُقرأ من `Program.cs`). **R4:** كل نوع حدث منشور له صف في `EVENT_PUBLISHER_INVENTORY.md` |
| قائمة الاستثناء | `tests/architecture/outbox-publish-allowlist.txt` (جديد) | كل استثناء بسبب مكتوب. الهدف صفر بعد الخطة، والاختبار يفشل إن نما العدد |
| حارس وقت التشغيل | `src/Shared/HRManagement.ServiceDefaults/Messaging/UnsavedOutboxGuardMiddleware.cs` (جديد) و`UseHrOutboxFlushGuard()` | بعد تنفيذ الطلب يفحص `DbContext` (الاسم المستعار المسجل في `AddRepositories`، [ServiceCollectionExtensions.cs:33](../../src/Shared/HRManagement.ServiceDefaults/ServiceCollectionExtensions.cs)) عن مدخلات `OutboxMessage` بحالة `Added`. يزيد المقياس `outbox_unsaved_messages_total{service,route}`، ويسجّل خطأً، ويرمي إن كان `Messaging:FailOnUnsavedOutbox=true` |
| مساعد النطاقات الخلفية | `OutboxScope.EnsureFlushedAsync(db)` | يُستدعى في نهاية نطاقات الإقلاع والمهام الخلفية التي تنشر، مثل [HR.Core Program.cs:289-314](../../src/Services/HRCore/HR.Core.Web/Program.cs) |

**القواعد:**
1. الحارس الثابت جزء من `UnitTests` فيعمل في CI (PLAN-35).
2. حارس وقت التشغيل مفعّل في كل الخدمات الـ13 ذات الـ outbox بعد الخطة، وفي القبول يرمي.
3. **عقد للخطط اللاحقة:** كل خطة تضيف حدثاً أو ناشراً (مثل `IWorkflowOutcomeDecided` في PLAN-46، وأحداث PLAN-53/56/57/61/62/63) تضيف صفه إلى `EVENT_PUBLISHER_INVENTORY.md` في الـ commit نفسه (R4)، وتنشر بالأنماط هنا: نشر ثم حفظ واحد، أو `OutboxUnitOfWork`، أو outbox المستهلك. ولا تُضاف أسطر إلى `outbox-publish-allowlist.txt` إلا بسبب مكتوب.

### 6. إعادة نشر ما فُقد (D-40-4)

| Artifact | المسار | الغرض |
|----------|--------|--------|
| واجهة Movements | `HR.Movements.Web/Controllers/MessagingRepairController.cs` (جديد، `POST api/admin/messaging/replay/{promotions\|delegations}?dryRun=true`) | سياسة إدارة الوحدة + `platform-admin`. يسرد طلبات الترفيع والندب المنفذة (`MovementExecutionRecords`)، ويعيد نشر الحدث بالمعرّف الحتمي عبر `OutboxUnitOfWork` |
| واجهة HR.Core | `HR.Core.Web/Controllers/MessagingRepairController.cs` (جديد) | `replay/asset-outcomes`: نسخ `WorkflowInstances` المكتملة أو المرفوضة لـ `AssetAssignment`/`AssetLossReport` ← إعادة نشر النتيجة (المستهلك يتجاهل غير `PendingApproval`). و`resync/org-units`: إعادة نشر كل الوحدات (المستهلكون upsert). الصلاحية `HrCoreDelete` + `platform-admin` |
| السكربت | `scripts/msg040-replay-lost-events.ps1` (جديد) | `-DryRun` (افتراضي) ثم `-Apply`. يكتب `reports/msg040-replay-<utc>.json` (المعرّفات والأعداد، بلا بيانات شخصية) |
| تدقيق | `EntityAuditLog` | كل تشغيل غير تجريبي يُسجَّل بالفاعل والعدد |

**القواعد:**
1. التشغيل الثاني بعد الإعادة يسرد صفراً للأحداث المعاد نشرها، لأن الإيصال بالمعرّف الحتمي موجود.
2. لا إعادة لحدث لم يُلتزم مصدره.
3. لا إعادة تلقائية: الأداة تُشغَّل يدوياً مرة بعد النشر.

## Out of scope

- دلالة الترفيع والندب: درجة الموظف في HR.Core، ومستهلك Payroll لـ `IPromotionExecuted`، والأهلية ← خطة الحركات في الموجة 2 (G-MOV-05 الباقي).
- حلقة اعتماد العهدة (الواجهة، والأدوار، والأصل المحجوز) ← PLAN-45. ربط الخدمات الأخرى بالمحرك ← PLAN-46. هنا وصول الحدث فقط.
- خدمة نشر السياسات الواحدة، وحمولة `IPolicyPublished` الموسعة، وإبطال الكاش في كل نسخ HR.Core ← PLAN-47 §8. هنا نقل موقع النشر قبل الحفظ فقط.
- تنفيذ أطراف الفصل والتعيين نفسها (قطع الوصول في Keycloak، والتعويض) ← PLAN-62 وPLAN-63. هنا قاعدة الإقرار ومسار نشره فقط.
- سياسة إعادة المحاولة (`InvalidOperationException` إلى `_error` مباشرة، [MassTransitExtensions.cs:151](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs))، والإعادة من المستوى الثاني، وأداة تشغيل طوابير `_error` للمشغّل، والتنبيه على عمقها ← الموجة 2 (OPS-F الباقي، OPS-H).
- إضافة الرسائل المؤجلة في RabbitMQ ← PLAN-38.
- إعادة محاولة إنشاء حسابات Keycloak في `EmployeeCreatedConsumer` ← PLAN-62.
- مصالحة الانحراف بين الخدمات ← الموجة 2 (OPS-H).
- RLS المفروض وأثره على الإيصالات والمهام الخلفية ← الموجة 2 (OPS-D).
- ترتيب الرسائل في مستهلكي Training (`EmployeeCreated` متأخر بعد `TransferExecuted`) ← الموجة 2.

## Acceptance scenarios

على الملف الشبيه بالإنتاج (PLAN-37)، وكل سيناريو `[RequiredAcceptance]` مع `EvidenceRecorder("AC-MSG-40")` و`AcceptanceGate`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-MSG-40a | تنفيذ ترفيع ← صف `OutboxMessage` في `hr_movements_db` يُسلَّم ← إيصال `IPromotionExecuted` وملاحظة في سجل التوظيف في HR.Core خلال 60 ثانية | إعادة تسليم الرسالة نفسها (أداة AC-16) ← لا ملاحظة ثانية |
| AC-MSG-40b | اعتماد الخطوة الأخيرة لتسليم عهدة ← التسليم `Active` والأصل `InUse` في Admin.Logistics خلال 60 ثانية | رفض تسليم آخر ← `Rejected`، والأصل غير `InUse` |
| AC-MSG-40c | نشر نسخة سياسة من الواجهة ومن API ← إيصال `IPolicyPublished` في IDCard (المستهلك الوحيد اليوم، [IDCard Program.cs:43](../../src/Services/IDCard/IDCard.Web/Program.cs)) لكل منهما | نشر يرفضه شرط العينين ← لا رسالة |
| AC-MSG-40d | AC-16 وAC-17 وAC-18 الحية = Pass: إيقاف RabbitMQ أثناء اعتماد إجازة ← الحالة ملتزمة وصف outbox قائم ← عودة الوسيط ← تسليم واحد | لا إيصال مكرر ولا حالة بلا حدث |
| AC-MSG-40e | رحلة تعيين شبيهة بالإنتاج (AC-HCM-33) تكتمل، وإقرارات أطراف Admin.Logistics وAuth.Directory تمر بـ `OutboxMessage` في قواعدها | — (السالب في اختبار التكامل: فشل الالتزام لا يترك إقراراً على الوسيط) |
| AC-MSG-40f | بعد تشغيل القبول الكامل: `outbox_unsaved_messages_total` = 0 في الخدمات الـ13 (عبر `/metrics` داخل الشبكة) | — (السالب في اختبار تكامل بمتحكم يتعمد النشر بلا حفظ) |
| AC-MSG-40g | بيانات «ما قبل الإصلاح» (ترفيع منفذ وعهدة معتمدة بلا أثر، تُحقن بـ SQL) ← التشغيل التجريبي يسردها ← التطبيق ← الأثر يظهر | التشغيل الثاني يسرد صفراً |
| AC-MSG-40h | وحدة تُضاف بـ SQL مباشرة في `hr_core_db` (فلا يُنشر لها حدث) ← إعادة إقلاع HR.Core ← لقطتها في `OrgUnitSnapshots` في Payroll وHR.Contracts خلال 120 ثانية، وصفوف `IOrgUnitChanged` في outbox HR.Core مُسلَّمة | إعادة إقلاع ثانية ← لا لقطة مكررة، لأن المستهلكين يحدّثون بالمعرّف ([OrgUnitChangedConsumer.cs:14-23](../../src/Services/Payroll/Payroll.Infrastructure/Consumers/OrgUnitChangedConsumer.cs)) |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~OutboxPublishOrdering|FullyQualifiedName~Msg040|FullyQualifiedName~PartyAck"
# تكامل حقيقي: PostgreSQL + RabbitMQ (Testcontainers)، ثم الانحدار القائم (SQLite وChaos)
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Category=Testcontainers&(FullyQualifiedName~Msg040|FullyQualifiedName~Qa003Outbox)"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~OutboxAtomicity|FullyQualifiedName~InboxIdempotency|Category=Chaos"
# الترحيلات الجديدة
foreach ($p in "AdminLogistics/Admin.Logistics","AuthDirectory/Auth.Directory","ITInfrastructure/IT.Infra","RecordsArchive/Records.Archive","DevAnalytics/Dev.Analytics","HRTraining/HR.Training") {
  $n = $p.Split('/')[1]
  dotnet ef migrations has-pending-model-changes -p "src/Services/$($p.Split('/')[0])/$n.Infrastructure" -s "src/Services/$($p.Split('/')[0])/$n.Web"
}
# القبول على الملف الشبيه بالإنتاج (PLAN-37)
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcMsg40|FullyQualifiedName~Ac16|FullyQualifiedName~Ac17|FullyQualifiedName~Ac18"
# انحدار الرحلات التي تمر بالـ sagas
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcHcm29"
dotnet test src/Tests/HRManagement.E2E --filter "Category=JourneyProdLike"   # AC-HCM-33/prodlike
pwsh scripts/msg040-replay-lost-events.ps1 -DryRun
```

## Definition of done

- جرد الناشرين بلا موقع A أو B أو C غير مصلح، و`outbox-publish-allowlist.txt` فارغ أو كل سطر فيه مبرر.
- الحارس الثابت أخضر ويفشل على عينة نمط معطوب (اختبار للحارس نفسه). وحارس وقت التشغيل مفعل في 13 خدمة.
- outbox المستهلك مفعل في 13 خدمة، وترحيلات الخمس الجديدة وTraining مطبقة.
- AC-MSG-40a..h وAC-16/17/18 = Pass في `acceptance-evidence/AC-MSG-40/` ومجلدات AC-16/17/18. ويحل ذلك محل دليل AC-16 الفاشل الحالي (`"ok": false`، `"receiptCount": -1` في [db-checks.json](../../acceptance-evidence/AC-16/db-checks.json)).
- انحدار AcHcm29 ورحلة AC-HCM-33 الشبيهة بالإنتاج (`Category=JourneyProdLike`) أخضر.
- أداة الإعادة شُغّلت تجريبياً على قاعدة القبول، والتقرير محفوظ.
- `EVENT_PUBLISHER_INVENTORY.md` و`RABBITMQ_OPERATIONS.md` (قسم Outbox، `:67-69`) مصححان.
- أسطر «الحالة» محدثة: G-OPS-08 (الجزء المغطى) في دراسة العمليات، وG-WF-03 في دراسة سير العمل، وسطر جزئي في G-MOV-05 (النشر فقط).

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-40-msg-atomic-event-publishing.md` |
| Shared | `Messaging/OutboxUnitOfWork.cs`، `StagesOutboxMessagesAttribute.cs`، `DeterministicMessageId.cs`، `UnsavedOutboxGuardMiddleware.cs`، `OutboxScope.cs`، `MessagingFaults.cs`؛ `MassTransitExtensions.cs` (`consumerOutbox`)؛ `IdempotentConsumerFilter.cs` (وضع الانضمام)؛ `PartyAckConsumerBase.cs` |
| Fixes | `MovementsExtendedControllers.cs`، `WorkflowInstanceActionService.cs`، `OrgUnitIntegrationService.cs`، `AgencyProvisioningService.cs`، `AgencyOrgStructureProvisioner.cs`، `PolicyVersionPublisher.cs`، `PoliciesController.cs`، `EmployeeIntegrationService.cs` ومستدعوه، وكل موقع B في الجرد |
| Outbox/Migrations | `Program.cs` و`*DbContext.cs` للخدمات الخمس + Training، وترحيلات `MSG040_*` |
| Repair | `MessagingRepairController.cs` (HR.Core، HR.Movements)، `scripts/msg040-replay-lost-events.ps1` |
| Unit | `OutboxPublishOrderingArchitectureTests.cs` (+ اختبار للحارس على عينات)، `DeterministicMessageIdTests.cs`، `PartyAckConsumerBaseTests.cs` |
| Integration | `Msg040OutboxOrderingIntegrationTests.cs` (Testcontainers PostgreSQL + RabbitMQ): نشر ← حفظ ← صف outbox ← تسليم؛ حاقن فشل على الحفظ الأخير ← لا حالة ولا رسالة؛ مستهلك ينشر ثم يفشل الالتزام ← لا رسالة على الوسيط؛ إيصال بوضع الانضمام مع رسالة مكررة ← أثر واحد؛ `UnsavedOutboxGuardMiddleware` يرمي في وضع الاختبار |
| Acceptance | `AcMsg40AtomicPublishingTests.cs`، وإعادة تشغيل `Ac16`/`Ac17`/`Ac18` |
| Docs | `EVENT_PUBLISHER_INVENTORY.md`، `RABBITMQ_OPERATIONS.md`، `reports/plan40-publisher-inventory.md`، وأسطر «الحالة» في الدراسات الثلاث |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تعارض معاملة `InboxConsumeFilter` مع معاملة outbox المستهلك | وضع الانضمام لا يفتح معاملة. واختبار التكامل يثبت الترتيب قبل تعميم التفعيل على الخدمات |
| outbox المستهلك يضيف `InboxState` و`OutboxMessage` لكل رسالة، فيزيد الكتابة | مقبول للحجم الحالي. وتنظيف `InboxState` بمهلة MassTransit الافتراضية، و`InboxRetentionHostedService` للإيصالات |
| `ApproveAsync` داخل معاملة واحدة مع معالجات تحفظ داخلها | `OutboxUnitOfWork` ينضم ولا يلتزم مرتين. اختبارات PLAN-25 للبوابات والمعالجات انحدار إلزامي |
| حزمة Roslyn مع `--locked-mode` | تحديث `packages.lock.json` في الـ commit نفسه؛ والبديل النصي جاهز |
| الإعادة تعيد أحداثاً قديمة لم يُرد أثرها | التشغيل التجريبي أولاً، وسرد صريح، وتطبيق يدوي مرة واحدة، وتدقيق |
| تغيير ترتيب الحفظ في 21+ موقعاً يكسر سلوكاً خفياً (معرّفات من القاعدة) | الجرد يصنّف كل موقع، و`OutboxUnitOfWork` حيث يلزم، وتشغيل اختبارات الوحدة والتكامل لكل خدمة معدلة |

## Effort estimate

9–10 أيام عمل:
- المرحلة 0 والجرد: 1
- الأداة والمعرّف الحتمي: 0.5
- الصنف A: 1
- الصنف B: 2
- الصنف C (outbox المستهلك، والخدمات الخمس، والفلتر، والأطراف، وTraining): 2
- الحارسان: 1
- الإعادة: 1
- الاختبارات والقبول والوثائق: 1.5
