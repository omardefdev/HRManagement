# PLAN-25 — ربط اعتمادات التوظيف بالإنتاج وفرض الصلاحيات في الخادم (PLAN-HCM-C)

**Scope:** HCM-001 / WF-001 — إغلاق فجوات P0 في [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-01, G-02, G-04, G-05, G-14, G-15  
**Status:** Implemented (CI not executed) — 2026-10-29  
**Priority:** P0  
**Depends on:** PLAN-26..33 منفذة  
**Related cards:** HCM-001, WF-001, IAM-002, PLAN-03 (SoD)  
**Execution prompt:** [PLAN-25-AGENT-PROMPT.md](execution/PLAN-25-AGENT-PROMPT.md)

## تعديلات قبل التنفيذ (2026-10-29)

كُتبت هذه الخطة قبل PLAN-26..33، فتغيّر كثير مما تفترضه. حيث يختلف هذا القسم عن بقية الوثيقة **يُعتمد هذا القسم**.

**ما أغلقته الخطط السابقة:**

- **G-05** أُغلق في PLAN-28: سياسة `HrCoreAppealReview`، و`RecruitmentAppealsController`، واستثناء الفلتر العام، وصندوق العمل. يُتحقق منه فقط.
- `SelectionCommitteeSession.SubmittedAt` موجود (PLAN-28).
- بدء مسار الاستئناف دائم (`instanceId ??= Start…` في PLAN-28)، وبدء مسار المسابقة والنتائج غير مشروط (PLAN-31).
- المرحلة 0 (إيداع الشيفرة) منجزة محلياً؛ الـ CI لم يُشغَّل لأن `main` غير مدفوع (G-01).

**القرارات:**

1. **D-1 معتمد:** مسار الاعتماد إلزامي. محرك السياسات يرفض أو يختار رمز مسار آخر، لكنه لا يلغي المسار. `NationalScope:IsLabScale` لم يعد يقرر بدء الاعتماد.
2. لا تنتقل الحالة إلى `PendingApproval` (ولا تُقدَّم اللجنة) إلا مع `WorkflowInstanceId` غير فارغ في الحفظ نفسه. غياب التعريف = خطأ عربي ولا تغيير في الحالة.
3. نطاق إصلاح البدء: تقديم طلب الشاغر، وتقديم العرض (عادي وتجاوز)، وتقديم اللجنة، و**طلب تغيير الملاك** (العيب نفسه: في غير المختبر كان يُطبَّق مباشرة دون اعتماد). الاستئناف والمسابقة يُتحقق منهما فقط.
4. الخادم يفرض دور الخطوة في الاعتماد **والرفض**، والفاعل يحمل كل أدواره. تجاوز المدير `Workflow:AdminBypass` معطّل. الفرض على أنواع التوظيف و`EstablishmentChangeRequest` (`Workflow:EnforceStepRoleEntityTypes`)؛ بقية النطاقات متابعة لاحقة.
5. مصدر واحد للقرار: بوابات `IWorkflowApprovalGate` لكل كيان، يستخدمها `WorkflowInstanceActionService` و`WorkflowPanelBuilder` معاً. تُنقل إليها `ValidateSelfApprovalAsync` وفحوص العضوية والنصاب.
6. الرفض يمر بالدور والبوابات نفسها، إلا النصاب: يجوز رفض اللجنة دون نصاب.
7. **G-14:** التعيين محكوم باعتماد العرض. تُعطَّل `R-REC-HIRE`، ويُلغى نشر `RECRUITMENT-HIRE`، وتُلغى نسخه اليتيمة بخطوة الإصلاح.
8. **G-15 مع D10 (PLAN-32):** سياسات كتابة صريحة على الإعلانات والشواغر الداخلية، ونطاق تنظيمي على شاشات التوظيف للمستخدم المقيّد بفرع. المسابقات للمركزية فقط.
9. **G-01:** لا دفع. التقرير النهائي يتضمن أوامر الدفع والـ CI للمستخدم.
10. الإنتاج/Staging: `PolicyEngine:Core.Recruitment = "Shadow"` و`Recruitment:Approvals:AlwaysStartWorkflow = true`.

التفاصيل والقرارات الإضافية (R1–R7) في [`reports/plan25-progress.md`](../../reports/plan25-progress.md).

## ملاحظات التنفيذ والانحرافات (2026-10-29)

التقرير الختامي: [`reports/plan25-final-report.md`](../../reports/plan25-final-report.md)؛ دليل التنفيذ:
[PLAN-25-EXECUTION.md](execution/PLAN-25-EXECUTION.md).

| البند | ما نُفّذ | انحراف/ملاحظة |
|-------|----------|----------------|
| بدء المسار (G-02) | `ApprovalStartCoordinator` + `RecruitmentApprovalOptions` (`Recruitment:Approvals`) في HR.Core.Infrastructure/Services/Workflow؛ يُستدعى قبل أي تعديل على الكيان، وتُحفظ الحالة مع النسخة في معاملة واحدة على المزوّدات العلائقية؛ يعيد استخدام نسخة قيد التنفيذ للكيان نفسه | الاسم عام لا «Recruitment» لأنه يخدم الملاك أيضاً. الاستئناف والمسابقة لم يُمرَّرا عبره (كانا يبدآن دائماً، ولهما سلوك مقصود: H5 للاستئناف) |
| الملاك | طلب تغيير الملاك لم يعد يُطبَّق فوراً خارج المختبر؛ ينتظر الاعتماد | كان العيب نفسه (وأشد) |
| الواجهة | زر «إعادة بدء مسار الاعتماد» (`HrCoreWrite`) بدل تحذير «فعّل PolicyEngine… أو IsLabScale» في الطلب والعرض واللجنة والملاك | — |
| الإصلاح | `POST /api/recruitment/admin/repair-approvals` (`HrCoreDelete`) + `scripts/hcm025-repair-stuck-approvals.ps1` | — |
| الدور والبوابات (G-04) | `WorkflowActionActor` بكل الأدوار و`employee_id` ومطالبة النطاق؛ `WorkflowStageGuard.ValidateStepRole`/`ValidateReject`؛ سبع بوابات `IWorkflowApprovalGate`؛ `WorkflowApprovalEvaluator` مشترك بين الخادم ولوحات التفاصيل | سياق البوابة يتضمن تعريف المسار (لقاعدة عرض التجاوز). ترتيب الفحص: البنية ← الدور ← البوابة ← مُطلق المسار. رسائل الحارس بالعربية. حُذف فحص العضوية/النصاب المكرر من `SelectionCommitteeLifecycleService` (البوابة تغطيه دائماً). صندوق العمل يبقي حسابه المجمّع مع قاعدة الدور نفسها |
| الرفض | الدور والبوابات نفسها | للاعتماد فقط: النصاب، المعتمد المختلف (التجاوز والنتائج)، النتيجة الصريحة للاستئناف، قاعدة مُطلق المسار |
| الفرض | `Workflow:EnforceStepRoleEntityTypes`: أنواع التوظيف الستة + الملاك؛ `Workflow:AdminBypass=false` في كل البيئات | العهدة والفقدان والنقل خارج الفرض (دور الخطوة 1 للعهدة يختلف بحسب ترتيب البذور) — متابعة |
| G-14 | تعطيل `R-REC-HIRE` في مكانه (لا عمود `IsActive`)، وإلغاء نشر `RECRUITMENT-HIRE` بتصحيح عند الإقلاع، وإيقاف `PolicyStaticFallback` عن إطلاقه، و`WorkflowStarter` لا يحيي تعريفاً غير منشور عبر الربط | اختبار معماري `WorkflowDefinitionsHaveHandlersTests` |
| G-15 | سياسات صريحة على الإعلانات والشواغر الداخلية + تدقيق تاريخ الإغلاق ومنع الإغلاق الرجعي مع وجود طلبات؛ اختبار السقف `HrCorePostActionsHaveExplicitPolicyTests` وقائمة مجمّدة بـ 42 إجراءً غير توظيفي | — |
| D10 | `RecruitmentOrgScopeFilter` على طلبات الشاغر والتوظيف والعروض واللجان (قوائم مصفّاة، تفاصيل 404، POST بمعرّف خارج النطاق 404)؛ المسابقات للمركزية فقط (403) | `PromoteFromCitizen`/`PromoteFromInternal` والشواغر الداخلية — متابعة |
| الإعداد | Production/Staging: `PolicyEngine:Core.Recruitment=Shadow`، `Recruitment:Approvals`، `Workflow` | الملف الشبيه بالإنتاج يكرّر القيم صراحةً لأن البيئة تبقى `Acceptance` |
| AC-HCM-25g | الحقائق الحية (القاعدة معطّلة، التعريف غير منشور، لا نسخ قيد التنفيذ) + اختبار وحدة لوضع Enforce | وضع Enforce ليس وضع النشر (الإنتاج Shadow)، فلم يُشغَّل حيّاً |
| G-01 / CI | الشيفرة كلها في commits محلية | لم يُدفع شيء؛ أوامر الدفع والـ CI في التقرير الختامي §12 |

## Goal

أن يعمل مسار التوظيف **بإعدادات Production/Staging كما هي** (`IsLabScale=false`، وPolicyEngine غير مضبوط):

- يُنشأ مسار اعتماد لكل طلب شاغر وخطاب عرض ولجنة واستئناف، فلا يعلق شيء في `PendingApproval`.
- يفرض **الخادم** دور الخطوة وSoD ونصاب اللجنة والمراجع المستقل، لا الواجهة وحدها.
- يصل دور `compliance` إلى مراجعة الاستئناف.
- لا تبقى مسارات عمل يتيمة.
- تُحمى إجراءات الكتابة في الإعلانات والشواغر الداخلية.

---

## المرحلة 0 — خط الأساس (Baseline) — 2–3 أيام

> لا يبدأ أي عمل آخر قبل إغلاق هذه المرحلة.

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/hcm-recruitment-baseline` | — |
| 0.2 | فرز التغييرات غير المُودعة (283 جديد + 140 معدّل تحت HRCore/docs/scripts) إلى commits منطقية | 1) Domain + Enums 2) Migrations HCM001–005, HCM022, EST* 3) Infrastructure/Services/Recruitment + Workflow 4) Web (Controllers/Views/Services) 5) Tests 6) Docs/Scripts | لا ملفات `??` تخص التوظيف |
| 0.3 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 errors |
| 0.4 | اتساق الترحيلات | `dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web` | لا تغييرات معلّقة |
| 0.5 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm\|Recruitment\|OfferWorkflow\|SelectionWorkflow\|Wf02\|Est0\|HireW"` + IntegrationTests بنفس المرشح | أخضر، أو قائمة إخفاقات موثّقة في `reports/hcm025-baseline.md` |
| 0.6 | PR + CI | فتح PR نحو `main`، تشغيل `ci.yml` و`codeql.yml` و`gitleaks.yml` | CI أخضر، وتسجيل SHA في رأس `HCM_001_GAP_STUDY.md` |
| 0.7 | قفل الملفات المؤقتة | التأكد من أن `tmp/` و`TestResults/` و`*.log` في `.gitignore` | لا سجلات بناء في الـ commit |

---

## In scope

### 1. فصل بدء مسار الاعتماد عن وضع محرك السياسات (G-02)

**المشكلة:** `RecruitmentRequisitionLifecycleService:41-69`، `RecruitmentOfferService:101-129`، `SelectionCommitteeLifecycleService:44-62`، `RecruitmentAppealService:72-91` لا تبدأ المسار في وضع `Off` إلا إن كان `IsLabScale=true`.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `RecruitmentApprovalCoordinator` | `HR.Core.Infrastructure/Services/Recruitment/RecruitmentApprovalCoordinator.cs` | نقطة واحدة تقرر: (1) تقييم السياسة إن كان الوضع ≠ Off (2) إن رُفض → خطأ (3) إن سُمح وأُعيد `WorkflowInstanceId` → يُستخدم (4) **وإلا يبدأ المسار دائماً** عبر `IWorkflowStarter.StartByWorkflowCodeAsync` |
| `RecruitmentApprovalOptions` | `Recruitment:Approvals` في appsettings | `AlwaysStartWorkflow=true` (افتراضي)، `FailIfDefinitionMissing=true` |
| إعادة هيكلة 4 خدمات | الخدمات أعلاه | حذف `ResolveMode()`/`IsLabScale()` المكررة واستدعاء المنسق |

**القواعد:**

- لا تتغير الحالة إلى `PendingApproval` إلا بعد الحصول على `WorkflowInstanceId` غير فارغ، داخل الـ transaction نفسها. إن غاب تعريف المسار يُعاد خطأ عربي واضح ولا تتغير الحالة.
- `SelectionCommitteeSession` تحصل على `SubmittedAt` لتمييز «مقدَّمة» عن «مسودة».
- في وضع Shadow/Enforce: السياسة تضيف رفضاً أو تحدد رمز مسار بديلاً، لكنها لا تُلغي إلزامية المسار.

**إصلاح البيانات العالقة:**

- `scripts/hcm025-repair-stuck-approvals.ps1` يستدعي endpoint إداري جديداً `POST /api/recruitment/admin/repair-approvals?dryRun=true` (سياسة `HrCoreDelete`)، يسرد ثم يصلح:
  - `RecruitmentRequisitions` بحالة `PendingApproval` و`WorkflowInstanceId IS NULL`
  - `OfferLetters` بحالة `PendingApproval` و`WorkflowInstanceId IS NULL`
  - `RecruitmentAppeals` بحالة `Pending` و`WorkflowInstanceId IS NULL`
- زر «إعادة بدء مسار الاعتماد» (`HrCoreWrite`) في لوحات Details بدل التحذير الحالي «فعّل PolicyEngine… أو IsLabScale».

**الإعداد:** إضافة صريحة في `appsettings.Production.json` و`appsettings.Staging.json`:

```json
"PolicyEngine": { "Core.Recruitment": "Shadow" },
"Recruitment": { "Approvals": { "AlwaysStartWorkflow": true } }
```

### 2. فرض دور الخطوة والبوابات في الخادم (G-04)

**المشكلة:** `WorkflowStageGuard.ValidateApprove` و`WorkflowEngine.ApproveStep` لا يفحصان `RoleRequired`. نصاب اللجنة والمراجع المستقل مفحوصان في `WorkflowPanelBuilder` و`*WorkflowUiService` فقط. و`RejectAsync` بلا أي حارس.

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `WorkflowActionActor.Roles` | `ServiceDefaults/Workflow` | مجموعة كل أدوار المستخدم بدل `Role` واحد؛ وتحديث `CreateWorkflowActor` في كل المتحكمات لجمع كل `ClaimTypes.Role` و`role` |
| `WorkflowStageGuard.ValidateApprove/ValidateReject` | `ServiceDefaults/Workflow/WorkflowStageGuard.cs` | فحص `step.RoleRequired ∈ actor.Roles` (مع `admin` حسب `Workflow:AdminBypass`، والافتراضي `false` في Production) |
| `IWorkflowApprovalGate` | `HR.Core.Infrastructure/Services/Workflow/Gates/` | واجهة: `EntityType`، `Task<string?> GetBlockReasonAsync(instance, step, actor, ct)` |
| `RequisitionApprovalGate` | ↑ | SoD المنشئ + نطاق الوحدة التنظيمية (نقل من المتحكم) |
| `OfferApprovalGate` | ↑ | SoD المنشئ + نطاق الوحدة |
| `CommitteeApprovalGate` | ↑ | الخطوة 2 تتطلب `HasQuorum` (≥3 أعضاء غير منسحبين)؛ منشئ الجلسة لا يعتمد الخطوة 3؛ أعضاء اللجنة لا يعتمدون الخطوة 3 |
| `AppealApprovalGate` | ↑ | `actor.UserId == appeal.IndependentReviewerUserId` (التوحيد في PLAN-28) |
| `WorkflowInstanceActionService` | `.../Workflow/WorkflowInstanceActionService.cs` | استدعاء البوابات في `ApproveAsync` **و`RejectAsync`**؛ ونقل `ValidateSelfApprovalAsync` إلى البوابات |
| `WorkflowPanelBuilder` | `HR.Core.Web/Services/WorkflowPanelBuilder.cs` | يستدعي البوابات نفسها لحساب `CanApprove`، فيصبح للقرار مصدر واحد للخادم والواجهة |

### 3. وصول `compliance` إلى مراجعة الاستئناف (G-05)

| Artifact | المسار | الغرض |
|----------|--------|--------|
| سياسة `HrCoreAppealReview` | `HrManagementAuthorizationExtensions.cs` | Admin \| HrCore \| Compliance |
| `RecruitmentAppealsController` | `HR.Core.Web/Controllers/` | `Index` (استئنافات المراجع الحالي)، `Details(id)` (ملخص الطلب **دون** الرقم الوطني ومع أسباب الرفض)، `Approve`/`Reject` عبر `IWorkflowInstanceActionService`؛ وحذف `ApproveAppealWorkflow`/`RejectAppealWorkflow` من `RecruitmentApplicationsController` |
| استثناء من الفلتر العام | `HrModuleAuthorizationFilter` | إضافة `RecruitmentAppeals` إلى قائمة المتحكمات ذات السياسة الخاصة |
| صندوق العمل | `WorkInboxController` | سياسة `HrCoreWorkInbox` = HrCoreReadOnly ∪ Compliance؛ يرى `compliance` عناصر `RecruitmentAppeal` المعيّنة له فقط |
| القائمة الجانبية | `_HrSharedLayout`/قائمة HR.Core | رابط «استئنافات التوظيف» يظهر لدور `compliance` |

### 4. إزالة مسار `RECRUITMENT-HIRE` اليتيم (G-14)

- **القرار الموصى به:** التعيين محكوم أصلاً باعتماد العرض، فلا حاجة لمسار ثالث.
  - تعطيل القاعدة `R-REC-HIRE` (TriggerWorkflow) وإبقاء قواعد الرفض `R-REC-ORG` و`R-REC-HIRE-OFFER`.
  - إضافة `DeactivateRuleIfPresentAsync` إلى `PolicyEngineDomainSeeds`، لأن `AddRuleIfMissingAsync` لا يعدّل قاعدة موجودة.
  - وسم تعريف `RECRUITMENT-HIRE` كـ `IsActive=false` في `WorkflowPackSeeder`.
- **إصلاح بيانات:** إلغاء نسخ `WorkflowInstances` بنوع `RecruitmentApplication` وحالة `InProgress` (في سكربت الإصلاح نفسه).
- **حارس معماري:** اختبار يفشل إن وُجد تعريف مسار نشط لـ `EntityType` ليس له handler في `WorkflowInstanceActionService`.

### 5. صلاحيات الإعلانات والشواغر الداخلية (G-15)

- `PublicJobPostingsController` و`TalentMarketplaceController`: إضافة `[Authorize(Policy = HrCoreWrite)]` على `Edit`/`ChangeApplicationStatus`/`DeclareCitizenCoi`، و`HrCoreDelete` على `Delete`.
- `ClosingDate`: تسجيل `EntityAuditLog` عند تغييره، ومنع تقصيره إلى ما قبل اليوم إن وُجدت طلبات. التمديد بعد الإغلاق يتطلب سبباً (التفاصيل في PLAN-26).
- **اختبار معماري** `HrCorePostActionsHaveExplicitPolicyTests` في `UnitTests/Architecture`: كل `[HttpPost]` في `HR.Core.Web` يجب أن يحمل `[Authorize(Policy=…)]` صريحاً أو `[AllowAnonymous]` مبرراً (قائمة استثناء).

---

## Out of scope

- منطق قرار الاستئناف (قبول/رفض/تعديل) وتوحيد هوية المراجع ← PLAN-28
- إدارة معايير التقييم ← PLAN-26
- تفعيل Enforce كامل لكل النطاقات خارج التوظيف

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-25a | ملف Production (`IsLabScale=false`، Policy غير مضبوط): Submit طلب شاغر ← `WorkflowInstanceId ≠ null` ← اعتماد خطوتين ← `Approved` | — |
| AC-HCM-25b | ملف Production: SubmitOffer ← اعتماد خطوتين ← `Issued` | — |
| AC-HCM-25c | — | `acc-br1-mgr` (hr-core) يرسل POST اعتماد للخطوة 2 (`hr-management-approver`) ← رفض «هذه الخطوة تتطلب دور…» ولا تتغير الحالة |
| AC-HCM-25d | لجنة بـ 3 أعضاء ← الخطوة 2 تُعتمد | لجنة بعضوين فعّالين ← POST اعتماد الخطوة 2 ← رفض «النصاب غير مكتمل» |
| AC-HCM-25e | `acc-a-compliance` (المراجع المعيّن) يفتح `RecruitmentAppeals/Details` ويعتمد | مستخدم `compliance` آخر ← 403/رفض البوابة؛ و`hr-core` ← رفض الدور |
| AC-HCM-25f | — | `acc-a-management-approver` يرسل POST `PublicJobPostings/Edit` ← 403 |
| AC-HCM-25g | وضع Enforce: Hire ← لا تُنشأ نسخة `RECRUITMENT-HIRE` | — |
| AC-HCM-25h | سكربت الإصلاح بـ `dryRun` يسرد العالق، ثم بدونه ينشئ المسارات | — |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm025|WorkflowStageGuard|Architecture"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm025|RecruitmentRequisitionWorkflow|SoDRequisition"
# انحدار
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm021|Hcm022|Wf023|Est024|Workflow|SoD"
# قبول بملف شبيه بالإنتاج
docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml up -d --wait
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm25
```

`docker-compose.acceptance-prodlike.yml` (جديد): يضبط `NationalScope__IsLabScale=false` ولا يضبط `PolicyEngine__*`.

## Definition of done

- المرحلة 0 مغلقة: كل شيفرة التوظيف في commits على فرع مدموج، وCI أخضر.
- لا يوجد في الشيفرة مسار يضع كياناً في `PendingApproval` دون `WorkflowInstanceId`.
- `WorkflowStageGuard` يفحص الدور، والبوابات الأربع تعمل في `ApproveAsync` و`RejectAsync`، و`WorkflowPanelBuilder` يستخدمها.
- `compliance` يعتمد استئنافاً معيّناً له من الواجهة.
- AC-HCM-25a..h = Pass مع مجلد `acceptance-evidence/AC-HCM-25/`.
- انحدار PLAN-21/22/23/24 أخضر.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-25-hcm-production-workflow-binding.md` |
| Script | `scripts/hcm025-validate.ps1`, `scripts/hcm025-repair-stuck-approvals.ps1` |
| Unit | `Hcm025ApprovalCoordinatorTests.cs`, `Hcm025ApprovalGatesTests.cs`, `WorkflowStageGuardRoleTests.cs`, `HrCorePostActionsHaveExplicitPolicyTests.cs` |
| Integration | `Hcm025ProductionApprovalIntegrationTests.cs` |
| Acceptance | `AcHcm25ProductionApprovalTests.cs` |
| Compose | `docker-compose.acceptance-prodlike.yml` |
| Docs | تحديث `HIRE_RBAC_MATRIX.md` (compliance + بوابات)، `HIRE_ONBOARDING_GUIDE.md` (إزالة شرط IsLabScale) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تعديل `WorkflowStageGuard` المشترك يؤثر على مسارات أخرى (نقل، بطاقات، ملاك، عهدة) | تشغيل كل اختبارات الـ workflow؛ وعلم `Workflow:EnforceStepRole` يتيح التدرج بالتفعيل لكل نطاق |
| أدوار Keycloak تصل كـ client roles بدل realm roles في بعض البيئات | اختبار تكامل على `realm-hrmanagement-dev.template.json` و`realm-qa004-e2e.json` |
| بيانات عالقة كثيرة في بيئات تجريبية | سكربت الإصلاح بـ `dryRun` أولاً |

## Effort estimate

7–9 أيام عمل: المرحلة 0 (2–3)، المنسق (1.5)، البوابات (2)، compliance (1)، RBAC والتنظيف (1)، القبول (1).
