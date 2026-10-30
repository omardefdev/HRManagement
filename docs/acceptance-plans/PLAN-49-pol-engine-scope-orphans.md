# PLAN-49 — نطاق محرك السياسات: ما يحكمه فعلاً، وحقائق تطابق مخططاتها، وسحب القواعد اليتيمة والميتة (POL-E)

**Scope:** POL — [دراسة الفجوات](../POL_001_GAP_STUDY.md): G-POL-02، G-POL-04، G-POL-11. وأُدخلت G-POL-09 لأنها جزء من «ما يحكمه المحرك»: السياسة لا تختار رمز مسار غير الرمز الذي تطلبه الخدمة.  
**Status:** Proposed — 2026-10-30  
**Priority:** P1  
**Depends on:** PLAN-42 (عميل مغلق عند الفشل، ودلالات Shadow، ونقطة الإنفاذ الداخلية)، PLAN-46 (اعتمادات عابرة للخدمات للبطاقات والانفصال والتأديب، والتعريفات اليتيمة وقائمة السماح)، PLAN-47 (المدقق ونقطة امتداد القدرات ومجموعات تغيير البذور)، PLAN-48 (رؤية القوالب الوطنية لكل الجهات)  
**Related cards:** PROD-001 ([07](../completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md) §PROD-001)، [POLICY_DOMAIN_REGISTRY.md](../POLICY_DOMAIN_REGISTRY.md)، G-14 في [HCM-001](../HCM_001_GAP_STUDY.md) (نمط `R-REC-HIRE`)، G-WF-17 في [WF-001](../WF_001_GAP_STUDY.md)

## Goal

أن يقول النظام **الحقيقة عن محرك السياسات**، وأن يعمل ما يقوله:

- كتالوج واحد في الشيفرة يحدد ما يحكمه المحرك. لكل مجال: مالكه، وأنواع كياناته وإجراءاته، والآثار المسموحة، والمسارات التي يجوز له بدؤها، ونقاط الاستدعاء.
- المجالات التي لها نقاط استدعاء وقواعد مبذورة تعمل في Shadow في Production/Staging، فيُسجَّل قرار المحرك دون أن يغيّر النتيجة. وهي: `Core.Recruitment`، `Core.Establishment`، `Assets.Custody`، `IDCard.Lifecycle`، `Contracts.Leave`.
- المجالات الخمسة عشر الأخرى تُسحب من البذور والواجهة، ويُوثَّق أنها خارج النطاق.
- لا قاعدة منشورة بأثر لا يستهلكه أحد، ولا بقيمة قانونية نائبة، ولا بـ`TriggerWorkflow` يتيم.
- حقائق كل باني تطابق مخطط مجاله، والإجازة تمر بالمحرك من كل قنواتها.
- في Enforce لا تستطيع السياسة استبدال مسار اعتماد أضعف بالمسار الذي تطلبه الخدمة.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-POL-1: ما يحكمه المحرك | **محكومة (Shadow):** `Core.Recruitment`، `Core.Establishment`، `Assets.Custody`، `IDCard.Lifecycle`، `Contracts.Leave`. **خارج النطاق (تُسحب وتُوثَّق):** `Assets.Procurement`، `Contracts.Advance`، `Contracts.Hire`، `Contracts.Separation`، `Contracts.Contract`، `Movements.Transfer`، `Movements.Workforce`، `Core.Employment`، `Health.Insurance`، `Compliance.Disciplinary`، `Payroll.Period`، `IT.Tickets`، `Archive.Retention`، `Training.Learning`، `Analytics.ETL`. التوسيع لاحقاً = إضافة واصف في الكتالوج ونقطة استدعاء ومجموعة تغيير | §1، §2 |
| D-POL-5 (جزء الإجازة): القيم القانونية لمدد الإجازة | لا قيمة نائبة في المحرك: تُسحب `R-LEAVE-MAX` (30 يوماً لكل الأنواع) و`R-LEAVE-LONG` (`RequireApproval` بلا مستهلك). الحدود المحلية `GetMaxDuration` و`LeavePolicy` تبقى كما هي حتى قرار المالك القانوني | §2 |
| D-POL-13: الانتقال من Shadow إلى Enforce | لا انتقال في هذه الخطة. يبقى القرار للمالك بعد تقرير Shadow (الموجة 2، G-POL-10) | — |
| D-POL-14: رفض المحرك لطلب إجازة وارد من ESS في Enforce | يُنشأ الطلب بحالة `Rejected` مع سبب السياسة في `Notes`، ولا يُسقَط بصمت. يُنفَّذ الآن ولا يعمل إلا في Enforce | §4 |

## المرحلة 0 — خط الأساس (نصف يوم)

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-49-pol-engine-scope-orphans` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet test src/Tests/HRManagement.UnitTests --filter "PolicyEngine\|Pol047\|S2s042\|AssetClassifier\|WorkflowDefinitionsHaveHandlers\|Hcm025\|Hcm027\|Est024"` + `IntegrationTests --filter "Pol047\|S2s042\|Fun002\|Est002"` | أخضر أو قائمة في `reports/plan49-baseline.md` |
| 0.3 | جرد الواقع | `rg "EnforceAsync\(" src --glob "!**/Tests/**"`؛ `rg "AddHrPolicyEnforceClient" src`؛ استعلام المجالات والقواعد النافذة على قاعدة القبول | جدول «مجال × نقطة استدعاء × قواعد نافذة × آثار» في التقرير، يطابق مصفوفة الدراسة §2.1 أو يشرح الفرق |
| 0.4 | ما سلّمته PLAN-46/47/48 | `IPolicyDomainCapabilities` و`PolicySeedChangeSetApplier` من PLAN-47، وتحميل القالب الوطني لجهة غير افتراضية من PLAN-48، وقائمة سماح التعريفات بعد PLAN-46 | مُسجَّل في التقرير |

---

## In scope

### 1. كتالوج المجالات مصدراً واحداً للحقيقة (G-POL-02)

**المشكلة:**
- **15 من 20 مجالاً بلا أي نقطة استدعاء.** النقاط الوحيدة: `AssetsController.cs:411`، `:815`، `AssetClassifier.cs:13-20`، `LeaveRequestHandlers.cs:64-68`، `CardRequestsController.cs:132`، وسبع نقاط داخل HR.Core للتوظيف والملاك.
- **خدمات تسجّل العميل ولا تستدعيه:** ثماني خدمات، ومعها HR.Core نفسه (`HR.Core.Web/Program.cs:277`).
- **سجلّان متناقضان:** `PolicyDomainRegistry` يعرض «Shadow» لـ15 مجالاً، منها 11 بلا استدعاء، ويُسقط `Core.Establishment` (`PolicyTemplatesController.cs:218-241`). و[POLICY_DOMAIN_REGISTRY.md](../POLICY_DOMAIN_REGISTRY.md) يقول «Static».

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `PolicyDomainCatalog` + `PolicyDomainDescriptor` | `src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainCatalog.cs` | لكل مجال من `PolicyDomains.All` (`PolicyDomains.cs:27-35`): `Status` (`Governed`/`OutOfScope`)، `OwnerService`، `OwnerClientId` (`null` لـ`Core.*`)، `SubjectActions` (نوع ← إجراءات)، `AllowedEffects`، `SetFields`، `WorkflowTriggers` (نوع ← رموز)، `CallSiteFiles`، `OutOfScopeReasonAr` |
| استبدال خرائط PLAN-42 | `PolicyDomainOwners.cs`، `PolicyWorkflowTriggerAllowlist.cs` | تُشتق من الكتالوج، فلا مصدر ثانٍ |
| `CatalogPolicyDomainCapabilities` | `src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/CatalogPolicyDomainCapabilities.cs` | تنفيذ `IPolicyDomainCapabilities` (PLAN-47) من الكتالوج. المدقق يرفض أثراً أو حقل `SetField` أو رمز مسار خارج الواصف، ويرفض أي مسودة أو نشر لمجال `OutOfScope` |
| حماية وقت التشغيل | `PolicyOrchestrator.EvaluateAsync` (`PolicyOrchestrator.cs:89-98`)، نقطة الإنفاذ الداخلية (PLAN-42) | مجال `OutOfScope` ← قرار محايد «المجال خارج نطاق المحرك» بلا دفتر، والنقطة الداخلية تعيد 400 |
| تحقق الإقلاع | `PolicyEngineConfigurationValidator` (PLAN-42) | مجال `OutOfScope` بوضع غير `Off` في أي خدمة ← فشل الإقلاع |
| الواجهة | `PolicyTemplatesController.cs:19-27`، `:218-241`، `Views/PolicyTemplates/Index.cshtml` | حذف `PolicyDomainRegistry`. يعرض الجدول من الكتالوج: الحالة («محكوم»/«خارج النطاق»)، والخدمة المالكة، والوضع الفعّال من `IPolicyModeResolver`، وآخر صف دفتر للمجال، ونقاط الاستدعاء، وسبب الاستبعاد. و`CreateDraft` لمجال خارج النطاق مرفوض |
| إزالة العميل غير المستعمل | `Program.cs` في HR.Training (`:36`)، HR.Health (`:38`)، Compliance (`:44`)، Records.Archive (`:38`)، HR.Movements (`:36`)، Payroll (`:115`)، IT.Infra (`:52`)، Dev.Analytics (`:37`)؛ وHR.Core (`:277`) حذفه PLAN-37 (§1، القاعدة 4) فيُتحقق منه فقط | حذف `AddHrPolicyEnforceClient` حيث لا استدعاء، وحذف تسجيله (ومفتاح العنوان إن لم يعد له قارئ) من `deploy/wiring/service-wiring.json` (PLAN-37 §1، القاعدة 5)، فيبقى `ProductionWiringMatrixTests` أخضر |
| حارس نقاط الاستدعاء | `src/Tests/HRManagement.UnitTests/Architecture/Pol049PolicyDomainCatalogTests.cs` | (1) كل مجال `Governed` له ملف واحد على الأقل في `CallSiteFiles` يحوي `EnforceAsync(` واسم باني المجال. (2) لا ملف خارج بنية المحرك يشير إلى مجال `OutOfScope`. (3) كل خدمة تسجّل العميل تملك مجالاً محكوماً |
| التوثيق | `docs/POLICY_DOMAIN_REGISTRY.md` | جدول مولّد يدوياً من الكتالوج، وفحص نصي في الاختبار نفسه يطابقه |

### 2. القواعد: سحب الميت والنائب واليتيم، وتقييد الإجراء (G-POL-02، G-POL-04، G-POL-11)

**المشكلة:**
- **آثار بلا مستهلك:** `RequireApproval` و`RequireDualApproval` و`RequirePrivacyMask` و`BlockUntilChecklist` و`RequireOriginReceipt` لا يستهلكها أحد. وتنتجها 7 قواعد مبذورة: R-LEAVE-LONG (`PolicyEngineDomainSeeds.cs:25-27`)، R-ADV-HIGH (`:33-35`)، R-CON-RENEW (`:57-59`)، R-INS-MASK (`:114-116`)، R-PAY-CLOSE (`:130-132`)، R-TRN (`:154-156`)، R-ETL (`:162-164`).
- **قيمة نائبة:** R-LEAVE-MAX = 30 يوماً لكل الأنواع (`:179-182`).
- **`TriggerWorkflow` يتيم:**
  - `R-ID-PROCESS` (`:15-21`) حيّ عند التفعيل. IDCard يتجاهل النسخة ويصدر البطاقة فوراً (`CardRequestsController.cs:132-161`).
  - خمس قواعد ميتة: `R-HIRE-START`، `R-SEP-START`، `R-TRF`، `R-DISC`، `R-TKT` (`:39-53`، `:74-80`، `:120-126`، `:136-142`).
- **قواعد بلا شرط `action`:** 12 قاعدة، تشمل `R-PRIV-EVIDENCE`/`R-HIGH-APPROVAL`/`R-PRIV-WF` (`PolicyEngineSeeder.cs:98-126`)، و`R-LEAVE-SELF` (`PolicyEngineDomainSeeds.cs:174-177`).

**التصميم:** مجموعات تغيير بآلية PLAN-47 في `src/Services/HRCore/HR.Core.Infrastructure/Data/PolicySeedChangeSets.cs`. كل مجموعة تُنتج إصداراً جديداً منشوراً بنوع `SystemSeed`، ولا يُعدَّل إصدار قائم.

| المجموعة | العمليات |
|----------|----------|
| `POL-049-SCOPE-RETIRE` | عملية جديدة `RetireDomain(reasonAr)` في `PolicySeedChangeSetApplier`: إصدار أخير بلا قواعد لكل قالب في المجالات الخمسة عشر، مع `PolicyTemplate.RetiredAt`/`RetiredReason` (ترحيل `POL049_PolicyDomainRetirement`). على قاعدة جديدة: NoOp، لأن §3 يحذف بذرها |
| `POL-049-ASSETS-ACTION` | `ReplaceRule` لـ`R-PRIV-EVIDENCE` و`R-HIGH-APPROVAL` و`R-PRIV-WF` بإضافة `{"field":"action","operator":"Equals","value":"Assign"}`. المستدعي لا يستهلك هذه الآثار إلا في `Assign` (`AssetsController.cs:418-423`)، فلا يتغير السلوك |
| `POL-049-IDCARD-ORPHAN` | `RetireRule("R-ID-PROCESS")`. بدء اعتماد البطاقة من PLAN-46 لا من السياسة |
| `POL-049-LEAVE` | `RetireRule("R-LEAVE-MAX")` و`RetireRule("R-LEAVE-LONG")` (D-POL-5). و`ReplaceRule("R-LEAVE-SELF")` بإضافة `action Equals Approve` |

**القواعد:**

1. بعد المجموعات لا يبقى في أي إصدار نافذ أثر خارج `AllowedEffects` لمجاله، ولا `TriggerWorkflow` خارج `WorkflowTriggers`. يثبت ذلك `Pol049EffectiveRulesConformTests` على قاعدة مبذورة ومُطبَّقة.
2. تبقى الإصدارات التاريخية كما هي، ويبقى الدفتر يشير إليها.
3. التعريفات اليتيمة وقائمة السماح في `WorkflowDefinitionsHaveHandlersTests.cs:15-23` تملكها PLAN-46. وهنا يُضاف إلى الاختبار نفسه شرط: لا قاعدة في إصدار نافذ تطلق رمزاً نوع كيانه بلا معالج في `WorkflowInstanceActionService.HandledEntityTypes` ولا ربط عابر للخدمات من PLAN-46.

### 3. البذور الجديدة للمجالات المحكومة فقط (G-POL-02)

**المشكلة:** `SeedAllDomainsAsync` تنشئ قوالب المجالات العشرين على كل قاعدة جديدة (`PolicyEngineDomainSeeds.cs:13-169`)، وتربط تعريفات مسار لمجالات ميتة (`PolicyEngineSeeder.cs:172-185`).

**التصميم:**
- حذف كتل المجالات الخمسة عشر من `SeedAllDomainsAsync`.
- الإبقاء على ربط `PolicyWorkflowBinding` للرموز المحكومة فقط: `ASSET-ASSIGN-APPROVAL` والتوظيف والملاك.
- تبقى بقية الروابط في القواعد القائمة دون حذف، لأن PLAN-46 يستعمل `ID-CARD-ISSUANCE` و`DISCIPLINARY-APPROVAL` عبر مساره العابر للخدمات، ويسحب `SEPARATION-WIZARD` بنفسه (`RetiredCodes`).
- `Pol049FreshSeedTests`: قاعدة جديدة ← 5 قوالب فقط، وكل قاعدة فيها تجتاز المدقق بقدرات الكتالوج.

### 4. حقائق تطابق المخططات، وكل قنوات الإجازة (G-POL-04)

**المشكلة:**
- **مفتاح خارج المخطط:** البناة يرسلون `employeeId` (`PolicyDomainFactsBuilders.cs:19`، `:36`، `:54`)، والمخططات لا تحويه (`PolicyEngineDomainSeeds.cs:21`، `:29`). والمقيّم يرفض المفتاح غير المسجل (`PolicyFactsSchemaModels.cs:29-30`).
- **حقيقة لا تُرسل:** `isSelfApproval` لا يرسلها أحد.
- **قيمة ثابتة خاطئة:** `hasOrgScope: false` ثابت في الملاك (`EstablishmentChangeLifecycleService.cs:109`).
- **الإجازة تتجاوز المحرك من قناتين:**
  - طلب ESS الوارد (`InboundEventCommands.cs:252-289`)، وهو يتجاوز `ValidateLeaveRequest` أيضاً.
  - التعديل (`LeaveRequestHandlers.cs:103-131`).
  - والإنشاء يرسل `TenantId = null` (`:65`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| البناة | `src/Shared/HRManagement.ServiceDefaults/Policy/PolicyDomainFactsBuilders.cs:8-58` | حذف `employeeId` من البطاقة والإجازة. حذف `ForAdvanceRequest` (مجال خارج النطاق ولا مستدعي). `ForLeaveRequest(..., action, bool? isSelfApproval = null)` يرسل `isSelfApproval` في `Approve` فقط. `TenantId` من الكيان |
| الإجازة: التعديل | `LeaveRequestHandlers.cs:103-131` | `EnforceAsync` بـ`action=Update` قبل الحفظ، والرفض في Enforce ← `ValidationException` كالإنشاء |
| الإجازة: الاعتماد | `ApproveLeaveRequestHandler` (`LeaveRequestHandlers.cs:231-248`) | `EnforceAsync` بـ`action=Approve` و`isSelfApproval = (ApproverId == CreatedBy)` بعد فحص SoD المحلي وقبل المطالبة بالحالة |
| الإجازة: ESS | `ProcessLeaveRequestedHandler` (`InboundEventCommands.cs:252-289`) | استدعاء `LeaveAndAdvanceRules.ValidateLeaveRequest` ثم `EnforceAsync` بـ`action=Create`. الرفض في Enforce (D-POL-14) أو فشل التحقق المحلي ← الطلب `Rejected` مع السبب. والمستأجر من `TenantIdFlow` الذي يضبطه مرشح المستهلك |
| الملاك | `EstablishmentChangeLifecycleService.cs:72-110`، والمستدعيان `EstablishmentChangesController.cs:146` و`EstablishmentPlanningApiController.cs:147` | معامل جديد `actorIsBranchScoped` من `HrOrgScopeVisibility.IsBranchScoped(User)`، يُمرَّر إلى `hasOrgScope` |
| حارس المطابقة | `src/Tests/HRManagement.UnitTests/Architecture/Pol049FactsBuildersMatchSchemasTests.cs` | لكل باني في `PolicyDomainFactsBuilders` و`AssetPolicyFactsBuilder` ولكل إجراء في الكتالوج: مفاتيح الحقائق ⊆ حقول مخطط المجال المبذور، وكل حقل في قاعدة نافذة يصل إليه باني الإجراء |

### 5. أوضاع الإنتاج للمجالات المحكومة: Shadow (G-POL-02)

**المشكلة:**
- المفتاح الوحيد في الإنتاج `PolicyEngine:Core.Recruitment=Shadow` (`HR.Core.Web/appsettings.Production.json:232-233`)، والبقية `Off`.
- ملف prod-like يكرره وحده (`docker-compose.acceptance-prodlike.yml`).

**التصميم:**

| الملف | المفاتيح |
|-------|----------|
| `src/Services/HRCore/HR.Core.Web/appsettings.Production.json` و`appsettings.Staging.json` | المجالات المحكومة الخمسة = `Shadow` |
| `src/Services/AdminLogistics/Admin.Logistics.Web/appsettings.Production.json` | `Assets.Custody` = `Shadow` |
| `src/Services/IDCard/IDCard.Web/appsettings.Production.json` | `IDCard.Lifecycle` = `Shadow` |
| `src/Services/HRContracts/HR.Contracts.Web/appsettings.Production.json` و`appsettings.Staging.json` | `Contracts.Leave` = `Shadow` |
| `docker-compose.acceptance-prodlike.yml` | المفاتيح نفسها لـ`hr-core-web` و`admin-logistics-web` و`idcard-web` و`hr-contracts-web` |
| `Pol049PolicyModeConsistencyTests` | لكل مجال: القيمة في خدمة المالك = القيمة في HR.Core، في Production وStaging وprod-like. ولا مجال خارج النطاق بغير `Off`. و`scripts/hcm025-validate.ps1` يبقى صحيحاً |

**القواعد:**

1. Shadow لا يغيّر النتيجة، بدلالات PLAN-42: المستدعي يتلقى قرار Off، وقرار الخادم في `ShadowDecision` والدفتر.
2. لا مجال في `Enforce` في أي ملف إنتاج بعد هذه الخطة.

### 6. السياسة لا تستبدل مسار الاعتماد المطلوب (G-POL-09)

**المشكلة:**
- المنسّق يقبل أي نسخة بدأتها السياسة دون مقارنة رمزها بالرمز المطلوب (`ApprovalStartCoordinator.cs:95-99`). الرمز المطلوب مثلاً `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` للعرض الاستثنائي (`RecruitmentOfferService.cs:223`).
- المعالجات تتحقق فقط من `WorkflowInstanceId == instance.Id` (`OfferLetterWorkflowHandler.cs:24`).
- الثغرة كامنة في Enforce.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `ApprovalStartCoordinator.StartAsync` | `ApprovalStartCoordinator.cs:95-99` | إن أعادت السياسة نسخة يُقرأ رمز تعريفها. إن اختلف عن `request.WorkflowCode` تُلغى النسخة (`Status=Cancelled` مع سبب في السجل)، ويُعاد الخطأ «السياسة اختارت مسار اعتماد غير مسموح لهذا الإجراء»، ولا تتغير حالة الكيان |
| المدقق | `CatalogPolicyDomainCapabilities` | رموز `TriggerWorkflow` المسموحة لنوع الكيان من الكتالوج. والمنسّق يفرض التطابق التام مع الرمز الذي حسبته الخدمة بحسب حالة الكيان |

**القواعد:**

1. تملك السياسة أن ترفض، أو أن تبدأ **الرمز المطلوب نفسه**. ولا تملك تغييره.
2. النسخة الملغاة لا تظهر في صندوق العمل، ولا يعاد استعمالها (`FindReusableInstanceAsync` يقصر البحث على `InProgress`، `ApprovalStartCoordinator.cs:172-191`).

---

## Out of scope

| البند | الخطة |
|-------|-------|
| عميل الإنفاذ المغلق عند الفشل، ودلالات Shadow، ونقطة الإنفاذ الداخلية | PLAN-42 |
| المدقق وأربع العيون وثبات الإصدار وآلية مجموعات التغيير نفسها | PLAN-47 |
| رؤية القوالب الوطنية لكل الجهات، وسقف الجهة | PLAN-48 |
| ربط اعتماد البطاقة والانفصال والتأديب بمسار عابر للخدمات، وإصلاح التعريفات اليتيمة وقائمة السماح | PLAN-46 |
| الانتقال إلى Enforce لأي مجال، ودفتر Shadow ذو معنى (G-POL-10) | الموجة 2 |
| ربط القيم القانونية بـ`legal_source_id` وحدود الإجازات القانونية (G-POL-12) | الموجة 2. وأنواع الفصل في PLAN-61 |
| نقل عتبات تصنيف الأصول (10,000/5,000) من الـshim إلى القواعد في وضع Off | الموجة 2 |
| تصحيح AC-09/AC-10 ووثائق PROD-001 والأطروحة ودليل العرض | PLAN-65 |

## Acceptance scenarios

ملف القبول: الملف الشبيه بالإنتاج بعد PLAN-37/42/46/47/48، بالمفاتيح الجديدة في §5. ولسيناريو G-POL-09 فقط طبقة `docker-compose.acceptance-pol49.yml` تضبط `PolicyEngine__Core.Recruitment=Enforce`. ومعها مجموعة تغيير خاصة بملف القبول `ACC-POL49-WEAK-OFFER`، تُطبَّق على القالب الوطني حين `SeedProfile=Acceptance` فقط: قاعدة بأولوية 1، `action=Issue` ← `TriggerWorkflow RECRUITMENT-OFFER-APPROVAL`. كل سيناريو في `AcPol49EngineScopeTests` بـ`[RequiredAcceptance]` و`AcceptanceGate` و`EvidenceRecorder("AC-POL-49…")`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-POL-49a | `central-a` يفتح `ui/hr-core/PolicyTemplates`: 5 مجالات «محكوم» بوضع Shadow، و15 «خارج النطاق» بسبب الاستبعاد | `CreateDraft` لـ`IT.Tickets` ← رفض «المجال خارج نطاق المحرك». و`scripts/pol049-outofscope-mode-boot-check.ps1` يشغّل `hr-core-web` بـ`PolicyEngine__IT.Tickets=Shadow` ← خروج غير صفري برسالة |
| AC-POL-49b | الإجازة من ثلاث قنوات وبالاعتماد: إنشاء عبر HR (`a-contracts-maker`)، وطلب ESS (`employee-a`)، وتعديل، ثم اعتماد (`a-contracts-checker`). النتيجة 4 صفوف دفتر `Shadow` بـ`CallerClientId=svc-hr-contracts`، كلها بلا «حقل غير مسموح». وصف الاعتماد يحمل `isSelfApproval=false` | إجازة أمومة 60 يوماً لا تُمنع (Shadow)، والإصدار النافذ لـ`Contracts.Leave` بلا `R-LEAVE-MAX`/`R-LEAVE-LONG` (psql). وطلب ESS متداخل مع إجازة معتمدة ← `Rejected` بسبب «توجد إجازة معتمدة متداخلة» |
| AC-POL-49c | `a-idcard` يعالج طلب بطاقة ← صف دفتر `Shadow` | لا `WorkflowInstance` بنوع `CardRequest` بدأته السياسة (عدد قبل/بعد مع استبعاد نسخة PLAN-46)، و`R-ID-PROCESS` غائبة عن الإصدار النافذ |
| AC-POL-49d | `a-logistics-maker` يسلّم أصلاً مميزاً: الدليل إلزامي ومسار العهدة يبدأ كما في Off، وصف دفتر `Shadow` بآثار الخادم (`RequireEvidence`، `TriggerWorkflow`) | إنشاء أصل `أصول ثابتة` بقيمة 6000: `IsPrivileged` كما في Off، ولا صف دفتر يحمل `TriggerWorkflow` لإجراء `Create` |
| AC-POL-49e | `br1-mgr` يقدّم تغيير ملاك: الدفتر يسجل تطابق `R-EST-DENY-BRANCH` (قرار رفض في Shadow)، والتقديم يمضي بمساره | `central-a2` يقدّم: لا تطابق لقاعدة الرفض |
| AC-POL-49f | (طبقة Enforce) عرض عادي يُقدَّم ← نسخة `RECRUITMENT-OFFER-APPROVAL` (الرمز المطلوب نفسه) | عرض استثنائي يُقدَّم ← رفض «السياسة اختارت مسار اعتماد غير مسموح»، والعرض `Draft`، ونسخة السياسة `Cancelled`، ولا نسخة `InProgress` له (psql) |
| AC-POL-49g | مسودة في `Assets.Custody` بقاعدة `RequireEvidence` مقيدة بـ`Assign` ← تُقبل | مسودة في `Contracts.Leave` بأثر `RequireDualApproval`، وفي `IDCard.Lifecycle` بـ`TriggerWorkflow ID-CARD-ISSUANCE`، وفي `Assets.Custody` بـ`SetField value` ← 400 برسالة القدرة |

## Verification

```powershell
git switch -c feature/plan-49-pol-engine-scope-orphans
dotnet build HRManagement.slnx -warnaserror
dotnet ef migrations add POL049_PolicyDomainRetirement -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web
dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web

# وحدة ومعمارية
dotnet test src/Tests/HRManagement.UnitTests --filter "Pol049|Pol047|S2s042|PolicyEngine|AssetClassifier|WorkflowDefinitionsHaveHandlers|Architecture"
# تكامل (PostgreSQL): تطبيق مجموعات التغيير على قاعدة مُرحَّلة بالكامل، وسحب المجالات، وإلغاء النسخة في المنسّق داخل المعاملة
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Pol049|Pol047|Fun002|Est002|Hcm025ProductionApproval"
# انحدار
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm025|Hcm027|Est024|Fun002|Hcm002"

# قبول على الملف الشبيه بالإنتاج
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcPol49&FullyQualifiedName!~Ac49_f"
# طبقة Enforce لسيناريو G-POL-09 فقط
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  -f docker-compose.acceptance-pol49.yml --profile full-stack up -d --no-deps --no-build --force-recreate hr-core-web
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~Ac49_f"
# إعادة hr-core-web إلى الملف الشبيه بالإنتاج بعد السيناريو (الأمر نفسه بلا طبقة pol49)
pwsh scripts/pol049-outofscope-mode-boot-check.ps1
pwsh scripts/pol049-validate.ps1   # الكتالوج = الواجهة = POLICY_DOMAIN_REGISTRY.md، ولا AddHrPolicyEnforceClient بلا استدعاء
```

اختبارات الوحدة `Pol049*`:
- `Pol049PolicyDomainCatalogTests`.
- `Pol049FactsBuildersMatchSchemasTests`.
- `Pol049EffectiveRulesConformTests`.
- `Pol049FreshSeedTests`.
- `Pol049PolicyModeConsistencyTests`.
- `Pol049LeaveChannelsTests`: الإنشاء والتعديل والاعتماد وESS، مع عميل وهمي في Shadow وEnforce.
- `Pol049EstablishmentOrgScopeFactTests`.
- `Pol049CoordinatorWorkflowCodeTests`.

اختبارات التكامل `Pol049*`:
- `Pol049ScopeChangeSetsPostgresTests`: إصدارات جديدة لا تعديل في المكان، و`RetiredAt`.
- `Pol049CoordinatorCancelPostgresTests`: الإلغاء والرفض في معاملة واحدة.

## Definition of done

- كتالوج واحد يحكم الواجهة والمدقق ونقطة الإنفاذ وتحقق الإقلاع والتوثيق، ولا سجل ثانٍ.
- المجالات الخمسة المحكومة في Shadow في Production/Staging وprod-like، ولكل منها نقطة استدعاء مُثبتة وصفوف دفتر حية. والخمسة عشر الأخرى مسحوبة بإصدار أخير وموثّقة، ولا يسجّل العميل إلا من يستدعيه.
- لا أثر بلا مستهلك ولا قيمة نائبة ولا `TriggerWorkflow` يتيم في أي إصدار نافذ.
- حقائق كل باني تطابق مخططها، والإجازة تمر بالمحرك من الإنشاء والتعديل والاعتماد وESS.
- المنسّق لا يقبل رمز مسار غير المطلوب.
- AC-POL-49a..g = Pass في `acceptance-evidence/AC-POL-49/`، وانحدار المرحلة 0 أخضر.
- أسطر «الحالة» محدّثة لـ G-POL-02 وG-POL-04 وG-POL-09 وG-POL-11 في `POL_001_GAP_STUDY.md`.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-49-pol-engine-scope-orphans.md` |
| Shared | `PolicyDomainCatalog.cs`، تعديل `PolicyDomainFactsBuilders.cs` و`PolicyDomainOwners.cs` (يُشتق من الكتالوج) |
| HR.Core | `CatalogPolicyDomainCapabilities.cs`، تعديل `PolicySeedChangeSets.cs` و`PolicySeedChangeSetApplier.cs` (`RetireDomain`)، و`PolicyEngineDomainSeeds.cs` و`PolicyEngineSeeder.cs` و`ApprovalStartCoordinator.cs` و`EstablishmentChangeLifecycleService.cs` و`PolicyTemplatesController.cs` و`Views/PolicyTemplates/Index.cshtml` |
| Migration | `POL049_PolicyDomainRetirement` (`RetiredAt`، `RetiredReason`) |
| HR.Contracts | تعديل `LeaveRequestHandlers.cs` و`InboundEventCommands.cs` |
| Config | `appsettings.Production.json`/`Staging.json` في HR.Core وHR.Contracts، و`appsettings.Production.json` في Admin.Logistics وIDCard، و`docker-compose.acceptance-prodlike.yml`، و`docker-compose.acceptance-pol49.yml`. وحذف `AddHrPolicyEnforceClient` من 8 ملفات `Program.cs` (والتاسع، HR.Core، حذفه PLAN-37)، و`deploy/wiring/service-wiring.json` |
| Scripts | `scripts/pol049-outofscope-mode-boot-check.ps1`، `scripts/pol049-validate.ps1` |
| Tests | الوحدة والتكامل أعلاه، و`AcPol49EngineScopeTests.cs`، وشرط القواعد في `WorkflowDefinitionsHaveHandlersTests.cs` |
| Docs | `docs/POLICY_DOMAIN_REGISTRY.md`، `docs/policy-rules-inventory.csv` (عمود «محكوم/محلي»)، و`docs/POLICY_ENGINE_UAT.md`، وأسطر «الحالة» في `POL_001_GAP_STUDY.md` |
| Report | `reports/plan49-baseline.md`، `reports/plan49-final-report.md` (جدول قبل/بعد للمجالات والقواعد النافذة) |
| CI | `tests/acceptance/ci-scope.json`: تسجيل كل صنف قبول جديد في هذه الخطة (مطلوباً أو مستبعداً بمالك)، وإلا تفشل وظيفة القبول (قاعدة PLAN-41) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| Shadow يضيف استدعاءً متزامناً إلى HR.Core في تصنيف الأصل والتسليم والإجازة والبطاقة | Shadow لا يمنع عند الفشل (PLAN-42)، والمهلة من `InterServiceTimeout`. ويُقاس الزمن في AC-POL-49b/d ويُسجَّل في التقرير |
| مستهلك ESS في Enforce مستقبلاً يرفض طلباً لم يره الموظف بعد | D-POL-14: الطلب يُنشأ `Rejected` بسبب ظاهر. وEnforce غير مفعّل هنا |
| سحب مجال يحتاجه المالك لاحقاً | الإصدارات التاريخية باقية. وإعادة المجال = واصف `Governed` ونقطة استدعاء ومجموعة تغيير، بلا تعديل في المكان |
| تعارض مع PLAN-46 حول `ID-CARD-ISSUANCE` و`SEPARATION-WIZARD` | هذه الخطة تسحب **القواعد** فقط، وتترك التعريفات والروابط لـ PLAN-46. والمرحلة 0.4 تسجّل ما سلّمه |
| حذف `ForAdvanceRequest` وتسجيلات العميل يكسر اختباراً قديماً | `rg` في المرحلة 0.3 يسرد المراجع قبل الحذف |

## Effort estimate

8–9 أيام عمل: المرحلة 0 (0.5)، الكتالوج والواجهة وتحقق الإقلاع (2)، مجموعات التغيير والبذور (1.5)، الحقائق وقنوات الإجازة والملاك (1.5)، أوضاع الإنتاج والاتساق (0.5)، المنسّق (0.5)، القبول والتوثيق (1.5–2).
