# PLAN-44 — سلامة نسخة المسار تحت التزامن والفشل (WF-E)

**Scope:** WF-001 — [دراسة الفجوات](../WF_001_GAP_STUDY.md): G-WF-12, G-WF-19  
**Status:** Proposed — 2026-10-30  
**Priority:** P1 (G-WF-12) / P3 (G-WF-19)  
**Depends on:** PLAN-43 (أعمدة النسخة الجديدة، و`WorkflowSodRules`)، PLAN-40 (تمسّ الخطتان `WorkflowInstanceActionService.ApproveAsync/RejectAsync`. PLAN-40 أسبق، فتبني هذه الخطة معاملتها على `OutboxUnitOfWork` وترتيب «نشر ثم حفظ» منه)  
**Related cards:** WF-001، WF-002، DATA-001 (التزامن)، PLAN-25 (الملاحظتان 7 و8، و`ApprovalRepairService`)

## Goal

ألا تفقد نسخة المسار قراراً، وألا تتكرر، وألا تنفصل عن أثرها:

- اعتمادان متزامنان للنسخة نفسها: ينجح أحدهما، ويتلقى الآخر رسالة تعارض واضحة.
- تقديمان متزامنان للكيان نفسه: نسخة جارية واحدة.
- اكتمال النسخة وأثرها على الكيان يُحفظان معاً أو لا يُحفظان.
- ما فات من آثار قبل هذه الخطة يُكشف ويُصلح بأداة مدقَّقة، مع مقياس.
- الصندوق والخادم يعرّفان «المنشئ» بالطريقة نفسها، والمسار القديم لا يتجاوز دور الخطوة.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-WF-8 (جزء البوابات المحلية) تعريف «المنشئ» في بوابات HR.Core | المنشئ (`CreatedBy`) ∪ المقدِّم (`InitiatedBy`) ∪ الطالب (`RequestedByUserId` من PLAN-43)، ممنوعون من **كل** الخطوات في بوابات الشاغر والعرض والملاك والمسابقة والنتائج. اللجنة والاستئناف يبقيان على قواعدهما الخاصة. الإعداد `Workflow:Maker:IncludeSubmitter=true` | §4 |
| تطبيق المصالحة آلياً | لا. المهمة الدورية تكشف وتسجّل وتحدّث المقياس فقط (`Workflow:Reconciliation:AutoApply=false`). والإصلاح عبر نقطة المشغّل القائمة (`repair-approvals`) بتشغيل جاف أولاً | §3 |
| النسخ المكررة القائمة عند الترحيل | يبقى المرتبط بالكيان، وإلا الأحدث. والباقي `Cancelled` بقيد «PLAN-44: نسخة مكررة» في التاريخ. وتقرير قبل الترحيل وبعده | §2 |
| ترحيل `ApprovedCV` إلى المحرك | يؤجَّل إلى الموجة 2 (WF-D). هنا يُحصَّن المسار القديم فقط (§5) | Out of scope |

---

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-44-wf-instance-integrity` | — |
| 0.2 | التحقق من PLAN-43 | أعمدة `RequestedByUserId` و`SourceClientId` على `WorkflowInstance`، و`WorkflowSodRules` | موجودة، وإلا توقف وأبلغ |
| 0.3 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 أخطاء |
| 0.4 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm025\|Wf0\|Data001\|Est024"` و`dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm025\|Data001\|Wf043"` | أخضر أو إخفاقات موثقة في `reports/plan44-baseline.md` |
| 0.5 | جرد النسخ | SQL على قاعدة القبول: النسخ الجارية المكررة لكل `(TenantId, EntityType, EntityId)`، والنسخ المنتهية التي كيانها ما زال `PendingApproval` ومرتبطاً بها | الأرقام في `reports/plan44-baseline.md` (تغذي §2 و§3) |
| 0.6 | جرد سيناريوهات المقدِّم | في `AcHcm25*` و`AcEst24*`: أي خطوة 1 يعتمدها المقدِّم نفسه وهو غير المنشئ | قائمة في التقرير. أي حالة توجد تُعدَّل في الاختبار بشخصية أخرى أو تُعلَّق القاعدة بالإعداد مع إبلاغ |

---

## In scope

### 1. رمز التزامن على النسخة (G-WF-12)

**المشكلة:** لا رمز تزامن على `WorkflowInstance` ([HRCoreDbContext.cs:875-882](../../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)). في HR.Core يحمل `xmin` كيانان فقط: `Employee` و`ApprovalRequest` القديم (`:152`، `:579`). فاعتمادان متزامنان يقرآن الحالة نفسها ويكتب كلاهما ([WorkflowInstanceActionService.cs:72-123](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)). والنتيجة: قيد مفقود من `StepHistoryJson`، أو اكتمال مزدوج يشغّل المعالج مرتين.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| تهيئة الكيان | `HRCoreDbContext.cs` (كتلة `WorkflowInstance`) | `entity.ConfigureOptimisticConcurrency(this)` ([ConcurrencyExtensions.cs:16-28](../../src/Shared/HRManagement.ServiceDefaults/ConcurrencyExtensions.cs)) |
| التقاط التعارض | `WorkflowInstanceActionService.ApproveAsync/RejectAsync` و`ExternalApprovalService.CancelAsync` (PLAN-43) | `DbUpdateConcurrencyException` ← `WorkflowActionResult(false, ConflictMessage)`، والمعاملة تُلغى (§3) |
| `WorkflowConcurrency.ConflictMessage` | `HR.Core.Infrastructure/Services/Workflow/WorkflowConcurrency.cs` | «تغيّرت حالة الطلب أثناء الإجراء؛ أعد تحميل الصفحة وحاول مجدداً» |
| الاختبار المعماري | [Data001ConcurrencyArchitectureTests.cs:17-23](../../src/Tests/HRManagement.UnitTests/Data001ConcurrencyArchitectureTests.cs) | سطر `InlineData` لـ `Entity<WorkflowInstance>` |

**القواعد:**
1. التعارض لا يُعاد آلياً. يعود القرار إلى المستخدم بعد إعادة التحميل، لأن الخطوة قد تكون تقدمت.
2. التوقيع (`signatures.RecordStepActionAsync`) داخل المعاملة نفسها، فلا يبقى توقيع لقرار أُلغي.

### 2. نسخة جارية واحدة لكل كيان (G-WF-12)

**المشكلة:**
- الفهرس `(EntityType, EntityId)` غير فريد ([HRCoreDbContext.cs:877](../../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)).
- المنسّق يقرأ ثم يُدرج ([ApprovalStartCoordinator.cs:172-192](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs))، فتقديمان متزامنان ينشئان نسختين جاريتين.
- الصندوق يعرض الاثنتين، لأن شرط «بانتظار الاعتماد» لا يقارن النسخة بالكيان ([WorkflowInboxQueryService.cs:204، 223، 266، 283](../../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs)).
- من يعتمد النسخة الشبح يضيع جهده، لأن المعالج يتجاهلها ([RecruitmentRequisitionWorkflowHandler.cs:17-19](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionWorkflowHandler.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| فهرس فريد جزئي | `HRCoreDbContext.cs` + الترحيل | `IX_WorkflowInstances_Tenant_Entity_Active` على `(TenantId, EntityType, EntityId)` بمرشح `"Status" = 'InProgress'` (الحالة مخزّنة نصاً، `:881`) |
| الترحيل | `HR.Core.Infrastructure/Migrations/<ts>_WF044_WorkflowInstanceIntegrity.cs` | SQL حتمي قبل إنشاء الفهرس: لكل مجموعة مكررة، تبقى النسخة المرتبطة بالكيان (من جداول الشاغر والعرض والملاك والمسابقة والنتائج، وحقل `WorkflowInstanceId` للكيانات الخارجية غير متاح هنا فيبقى الأحدث)، والباقي `Cancelled` مع `CompletedAt = now()` وقيد تاريخ «PLAN-44: نسخة مكررة» |
| `ApprovalStartCoordinator` | الملف القائم | التقاط انتهاك الفهرس (`PostgresException.SqlState == "23505"` باسم الفهرس) ← إعادة قراءة النسخة الجارية وإعادتها إن كانت قابلة لإعادة الاستخدام (`FindReusableInstanceAsync`)، وإلا رسالة «يوجد مسار جارٍ لهذا الطلب» |
| `ExternalApprovalService.StartAsync` (PLAN-43) | الملف القائم | الالتقاط نفسه: نفس المفتاح ← النسخة نفسها، ومفتاح آخر ← 409 |
| شرط الارتباط في الصندوق | `WorkflowInboxQueryService.cs` (الحالات الخمس، `:191-292`) | `IsAwaitingApproval` يشترط `entity.WorkflowInstanceId == instance.Id` للأنواع التي تحمل الحقل. واللجنة (`:245`) والاستئناف (`:266`) بالمطابقة المتاحة لكل منهما |
| سكربت التقرير | `scripts/wf044-duplicate-instances-report.ps1` | يطبع المكررات قبل الترحيل وبعده (قراءة فقط) |

**القواعد:**
1. لا يُلغى الترحيل نسخة عليها قرار (`StepHistoryJson` غير فارغ) إن كانت هي المرتبطة. وإن لم تكن، تُلغى ويُذكر ذلك في التقرير.
2. الفهرس لا يمس النسخ المنتهية، فإعادة التقديم بعد الرفض تبدأ نسخة جديدة كالمعتاد.

### 3. ذرّية الاعتماد وأثره، والمصالحة (G-WF-12)

**المشكلة:**
- النسخة تُحفظ مكتملة ([WorkflowInstanceActionService.cs:117](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs))، ثم يحفظ المعالج الكيان في حفظ منفصل ([RecruitmentRequisitionWorkflowHandler.cs:32](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionWorkflowHandler.cs)). والرفض كذلك (`:164-166`).
- فشل المعالج يترك نسخة `Completed` وكياناً `PendingApproval` بلا مخرج.
- الإصلاح القائم يعالج «كياناً بلا نسخة» فقط ([ApprovalRepairService.cs:55-110](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalRepairService.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| معاملة واحدة | `WorkflowInstanceActionService.ApproveAsync/RejectAsync` | الغلاف موجود منذ PLAN-40: `OutboxUnitOfWork.ExecuteAsync` (ينضم إلى معاملة قائمة كنمط [ApprovalStartCoordinator.cs:136-164](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs)). هنا لا يُبنى غلاف ثانٍ، بل يُتحقق أن كل شيء داخله: حفظ النسخة، والتوقيع، والمعالج، والنشر قبل الحفظ الأخير، ثم `Commit`. ويُضاف التقاط التعارض (§1) واختبار حقن الفشل في المعالج |
| `ApprovalRepairService` | الملف القائم | فئة إصلاح ثانية «نسخة منتهية وكيان معلّق»: نسخة `Completed`/`Rejected` والكيان `PendingApproval` و`WorkflowInstanceId == instance.Id` ← إعادة تشغيل المعالج نفسه (المعالجات تفحص الحالة والارتباط، فالتكرار آمن). التشغيل الجاف هو الافتراضي (القائم) |
| `WorkflowOutcomeReconciliationJob` | `HR.Core.Infrastructure/Services/Workflow/WorkflowOutcomeReconciliationJob.cs` + تسجيل في `Program.cs` (بجانب `:146-168`) | كل `Workflow:Reconciliation:IntervalMinutes` (افتراضياً 15): يشغّل `RunAsync(dryRun: !AutoApply)` لكل جهة، ويحدّث المقياس، ويسجّل تحذيراً لكل بند |
| المقياس | `HR.Core.Infrastructure/Services/Workflow/WorkflowIntegrityMetrics.cs` | `workflow_outcome_unapplied_total{entity_type}` و`workflow_instance_conflict_total{action}`، ويُضاف إلى لوحة المراقبة القائمة إن وُجدت |
| نقطة المشغّل | [RecruitmentAdminController.cs:14-27](../../src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentAdminController.cs) | دون تغيير في المسار. التقرير يضيف الفئة الجديدة. والسكربت `scripts/hcm025-repair-stuck-approvals.ps1` يعرضها |

**القواعد:**
1. لا مسار يحفظ نسخة منتهية دون أثرها في المعاملة نفسها. واختبار تكامل يحقن فشلاً في المعالج ويثبت التراجع.
2. الأنواع الخارجية (العهدة، وأنواع PLAN-46) أثرها حدث في الـ Outbox داخل المعاملة نفسها، فلا تدخل المصالحة هنا. مصالحتها في جهة المالك (PLAN-45/46).
3. المصالحة لا تنشئ نسخاً ولا تغيّر نسخاً. تطبّق أثراً فات فقط، بفاعل المشغّل في التدقيق.

### 4. تعريف واحد للمنشئ بين البوابة والصندوق (G-WF-19)

**المشكلة:**
- الصندوق يستخدم `CreatedBy ?? InitiatedBy` ([WorkflowInboxQueryService.cs:203، 222، 243، 282](../../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs))، والبوابات `CreatedBy` وحده ([RecruitmentApprovalGates.cs:22-32](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/RecruitmentApprovalGates.cs)).
- إن كان `CreatedBy` فارغاً يخفي الصندوق العنصر عن المقدِّم، ويسمح له الخادم باعتماد الخطوة 1. والمقدِّم غير المنشئ لا يُمنع من الخطوة 1 في الحالتين، لأن قاعدة المنشئ في المحرك تبدأ من الخطوة 2 ([WorkflowStageGuard.cs:104-107](../../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `WorkflowMakerRules.Makers(entityCreatedBy, instance)` | `src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowSodRules.cs` (ملف PLAN-43) | المجموعة `{CreatedBy, InitiatedBy (إن IncludeSubmitter), RequestedByUserId}` دون الفارغ |
| البوابات | `RecruitmentApprovalGates.cs`، وبوابات العرض والملاك والمسابقة والنتائج في `Gates/` | `actor ∈ Makers` ← `CreatorMessage` في كل الخطوات |
| الصندوق | `WorkflowInboxQueryService.cs` | `EntityCreatedBy` يصبح `EntityMakers` من الدالة نفسها |
| اختبار التطابق | [Hcm025PanelServerParityTests.cs](../../src/Tests/HRManagement.UnitTests/Hcm025PanelServerParityTests.cs) | حالات جديدة: `CreatedBy` فارغ، ومقدِّم ≠ منشئ، لكل نوع. الصندوق والخادم يتفقان |

### 5. تحصين المسار القديم وحذف الشيفرة الميتة (G-WF-19)

**المشكلة:**
- `ApprovalsController.Approve` لا يفحص `RoleRequired` للخطوة ([ApprovalsController.cs:102-149](../../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs)، [ApprovalWorkflowService.cs:104-181](../../src/Services/HRCore/HR.Core.Domain/Services/ApprovalWorkflowService.cs))، مع أن `ApprovalStep.RoleRequired` موجود.
- `Cancel` متاح لأي `HrCoreWrite`، لا لصاحب الطلب (`:184-203`).
- `CreateViaEngineOrLegacy` بلا مستدعٍ ([ApprovalWorkflowService.cs:21-57](../../src/Services/HRCore/HR.Core.Domain/Services/ApprovalWorkflowService.cs)). والبذرتان `AssetAssignmentWorkflowSeeder` و`AssetLossWorkflowSeeder` بلا مستدعٍ ([AssetAssignmentWorkflowSeeder.cs:10](../../src/Services/HRCore/HR.Core.Infrastructure/Data/AssetAssignmentWorkflowSeeder.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| فحص الدور | `ApprovalWorkflowService.TryApproveStep` و`TryReject` | معامل `IReadOnlyCollection<string> actorRoles`. الخطوة الحالية تتطلب دوراً يحمله الفاعل، أو `admin` ما دام `Workflow:AdminBypass=true`. الرسالة «هذه الخطوة تتطلب دور <الدور>» |
| `ApprovalsController.Approve/Reject` | الملف القائم | تمرير أدوار المستخدم من المطالبات |
| `ApprovalsController.Cancel` | الملف القائم | صاحب الطلب (`RequestedBy`) أو `admin` فقط، وإلا رفض «لا يلغي الطلب إلا صاحبه» |
| حذف | `ApprovalWorkflowService.CreateViaEngineOrLegacy`، `AssetAssignmentWorkflowSeeder.cs`، `AssetLossWorkflowSeeder.cs`، واختباراتها | إزالة الشيفرة الميتة. واختبار معماري يمنع عودة بذرة تعريفات خارج `WorkflowPackSeeder` |

---

## Out of scope

- ترحيل `ApprovedCV` إلى المحرك وإلغاء المسار القديم كلياً ← الموجة 2 (WF-D)
- نشر الأحداث بعد آخر حفظ (G-WF-03) وحارسه المعماري ← PLAN-40
- البدء المحلي أولاً وإعادة المحاولة للعهدة، والنسخ اليتيمة من `AssetsController` ← PLAN-45
- مصالحة جهة المالك للأنواع العابرة (الفصل والبطاقة والتأديب) ← PLAN-46 وPLAN-60
- التفويض والمهل والتصعيد ← PLAN-50 والموجة 2 (WF-F)
- صندوق العمل الموحد المخزَّن ← الموجة 2 (WF-G)

## Acceptance scenarios

على الملف الشبيه بالإنتاج (`docker-compose.acceptance-prodlike.yml`)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، في `AcWf44InstanceIntegrityTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-WF-44", …)`، `AcceptanceGate.RequireData`). البيانات الشاذة في (c) و(e) تُزرع بـ SQL من أداة الاختبار في قاعدة القبول، ويُسجَّل ذلك في الدليل.

| ID | Positive | Negative |
|----|----------|----------|
| AC-WF-44a | `central-a` يرسل تقديمَين متزامنَين لطلب تغيير ملاك ← نسخة جارية واحدة في القاعدة، والاستجابتان تشيران إليها | — |
| AC-WF-44b | `central-a2` ينقر «اعتماد» مرتين بالتوازي على الخطوة 1 ← نجاح واحد | الثاني يتلقى رسالة التعارض العربية. `StepHistoryJson` فيه قيد واحد، وتوقيع واحد |
| AC-WF-44c | حالة مزروعة «نسخة مكتملة وطلب شاغر معلّق مرتبط» ← التشغيل الجاف يسردها ← التطبيق يجعل الطلب `Approved` ← المقياس يعود 0 | نسخة مكتملة غير مرتبطة بالكيان ← لا تُطبَّق وتُذكر «غير مرتبطة» |
| AC-WF-44d | — | مستخدم بلا دور الخطوة يعتمد طلباً قديماً (`ApprovedCV`) عبر `Approvals/Approve` ← رفض الدور. ومستخدم `hr-core` غير صاحب الطلب يلغيه ← رفض |
| AC-WF-44e | الصندوق والخادم متطابقان لطلب شاغر بـ `CreatedBy` فارغ: لا يظهر للمقدِّم | المقدِّم يرسل POST اعتماد الخطوة 1 ← رفض «لا يجوز لمنشئ الطلب…» |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web
dotnet test src/Tests/HRManagement.UnitTests --filter "Wf044|Data001|Hcm025PanelServerParity|WorkflowStageGuard"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Wf044|Hcm025ProductionApproval|Data001"
# قبول حي
pwsh scripts/wf044-duplicate-instances-report.ps1     # قبل الترحيل
pwsh scripts/acceptance-reset.ps1
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --build --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcWf44|FullyQualifiedName~AcHcm25|FullyQualifiedName~AcEst24"
pwsh scripts/wf044-duplicate-instances-report.ps1     # بعد الترحيل: صفر مكررات
```

**اختبارات جديدة:**
- **Unit:** `Wf044MakerRulesTests` (كل نوع × منشئ/مقدِّم/طالب)، `Wf044LegacyApprovalRoleTests`، `Wf044InboxLinkageTests`، وسطر `Data001ConcurrencyArchitectureTests`.
- **Integration على PostgreSQL** (Testcontainers، `[Collection("IntegrationSerial")]`)، `Wf044InstanceIntegrityPostgresTests`:
  - `xmin`: سياقان يحمّلان النسخة ويعتمدان، فينجح واحد ويرمي الآخر `DbUpdateConcurrencyException`، ويعيد الخدمة رسالة التعارض.
  - الفهرس الجزئي: إدراج نسختين جاريتين للكيان نفسه يُرفض، ويُقبل بعد إلغاء الأولى. وتقديمان متوازيان عبر المنسّق ← نسخة واحدة.
  - المعاملة: معالج محقون بفشل ← لا نسخة `Completed`، ولا توقيع، ولا صف `OutboxMessage`.
  - ترحيل الدمج: بيانات مزروعة بمكررات (مرتبطة وغير مرتبطة) ← النتيجة المتوقعة.

## Definition of done

- `WorkflowInstance` يحمل `xmin`، والتعارض يعيد رسالة عربية ولا يكتب شيئاً.
- لا نسختان جاريتان للكيان نفسه في أي جهة (فهرس + تقرير صفري بعد الترحيل).
- الاعتماد وأثره المحلي والـ Outbox في معاملة واحدة، والمصالحة تكشف ما فات قبل الخطة.
- البوابات والصندوق يستعملان `WorkflowMakerRules` نفسها، و`Hcm025PanelServerParityTests` موسَّع وأخضر.
- المسار القديم يفحص دور الخطوة، والإلغاء لصاحب الطلب. والشيفرة الميتة محذوفة.
- AC-WF-44a..e = Pass في `acceptance-evidence/AC-WF-44/`، وانحدار AC-HCM-25 وAC-EST-24 أخضر.
- سطر «الحالة» لـ G-WF-12 وG-WF-19 في الدراسة محدّث. و`WF_001_WORKFLOW_ENGINE.md` يصف التزامن والمصالحة، و`DATA_001` (إن وُجد جدول الكيانات المحمية) يضيف `WorkflowInstance`.
- الالتزامات بلا أسرار (القوالب تبقى بالعناصر النائبة `__SVC_SECRET__`/`${…}`)، ولا `global.json`، ولا `testdata/*/out/*.json` أعاد الاختبار كتابتها. ولا دفع إلى البعيد.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-44-wf-instance-integrity.md` |
| Infrastructure | تهيئة `WorkflowInstance` في `HRCoreDbContext.cs`، الترحيل `WF044_WorkflowInstanceIntegrity`، `WorkflowConcurrency.cs`، `WorkflowOutcomeReconciliationJob.cs`، `WorkflowIntegrityMetrics.cs`، تحديث `ApprovalStartCoordinator` و`WorkflowInstanceActionService` و`ApprovalRepairService` والبوابات |
| Shared | `WorkflowMakerRules` في `WorkflowSodRules.cs` |
| Domain | `ApprovalWorkflowService` (فحص الدور، وحذف الجسر) |
| Web | `WorkflowInboxQueryService.cs`، `ApprovalsController.cs` |
| حذف | `AssetAssignmentWorkflowSeeder.cs`، `AssetLossWorkflowSeeder.cs` |
| Tests | الاختبارات أعلاه، و`AcWf44InstanceIntegrityTests.cs` |
| Scripts | `scripts/wf044-duplicate-instances-report.ps1`، تحديث `scripts/hcm025-repair-stuck-approvals.ps1` |
| Config | `Workflow:Reconciliation` و`Workflow:Maker` في `HR.Core.Web/appsettings*.json` |
| Docs | `WF_001_WORKFLOW_ENGINE.md`، `WF_002_WORK_INBOX.md` (شرط الارتباط)، `SOD_MATRIX.md` (تعريف المنشئ)، وسطور «الحالة» في الدراسة |
| CI | `tests/acceptance/ci-scope.json`: تسجيل كل صنف قبول جديد في هذه الخطة (مطلوباً أو مستبعداً بمالك)، وإلا تفشل وظيفة القبول (قاعدة PLAN-41) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| منع المقدِّم من الخطوة 1 يكسر سيناريو قبول قائماً | جرد 0.6، والإعداد `Workflow:Maker:IncludeSubmitter` لتعليق القاعدة مع إبلاغ المالك |
| الترحيل يلغي نسخة عليها قرار | قاعدة 2.1 والتقرير قبل وبعد. والترحيل يرفض المتابعة إن وجد نسخة غير مرتبطة عليها قرار في مجموعة بلا نسخة مرتبطة، ويطلب قرار المشغّل |
| تعارض مع تغيير PLAN-40 على `ApproveAsync` | PLAN-40 مدموج قبل هذه الخطة، وهي تبني على غلافه ولا تكرره. اختبار التكامل المشترك (اعتماد ← Outbox) يثبت الترتيب |
| المعاملة تطيل القفل على الكيان | المعالجات قصيرة، ولا استدعاء HTTP داخل المعاملة (الأنواع الخارجية حدث في الـ Outbox) |
| `xmin` غير موجود في مزودات الاختبار غير Npgsql | الإثبات في اختبار PostgreSQL، كما في DATA-001 |

## Effort estimate

5–6 أيام عمل: المرحلة 0 (0.5)، الرمز والفهرس والترحيل (1.5)، المعاملة والمصالحة والمقياس (1.5)، المنشئ الموحد والمسار القديم والحذف (1)، التكامل والقبول والتوثيق (1–1.5).
