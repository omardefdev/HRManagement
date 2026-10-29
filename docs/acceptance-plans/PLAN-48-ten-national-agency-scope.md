# PLAN-48 — رؤية الإعدادات الوطنية لكل جهة، وسقف الجهة، وترقيم سليم في كل مستأجر (TEN)

**Scope:** TEN — [دراسة الترميز والهيكل](../COD_ORG_GAP_STUDY.md): G-COD-02، G-COD-03 (ومعهما جزء AC-13 من G-COD-20) · [دراسة السياسات](../POL_001_GAP_STUDY.md): G-POL-05  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-37 (التوصيل والملف الشبيه بالإنتاج)، PLAN-47 (دورة حياة القاعدة ومدقق النشر)، وضمناً PLAN-42 (تجاهل `TenantId` من جسم الطلب وقصر `codes/generate` على حسابات الخدمة، G-COD-05)  
**Related cards:** COD-001، TEN-002/002C/003 (عزل المستأجر وRLS)، PROD-001 (`PROD_001_TENANT_OVERRIDE.md`)، MDM-001، MIG-003 (`MIG_003_COD_CODE_LINEAGE.md`)، PLAN-04 (AC-13)

## Goal

1. **قاعدة رؤية وطنية واحدة صريحة.** صفوف الإعداد الوطنية تقرؤها كل جهة، ولا يكتبها إلا المستأجر الوطني. والصفوف الوطنية هي صفوف `DefaultTenantId` في جداول محددة: السياسات، والمسارات، وسياسات الترميز، والمرجع الوطني. القاعدة تعمل في طبقة EF **وتحت RLS المفروض** (سياسة `SELECT` قاعدية مستقلة)، فلا يحتاج OPS-D (الموجة 2) إلى إعادة تصميمها.
2. **أرقام بصيغتها في كل مستأجر** (G-COD-02). لا احتياط صامت بلا بادئة، ولا `-2026-00031`.
3. **سياسات ترميز لا تتصادم** (G-COD-03). مدقق للسياسة، وتصحيح السياسات الثلاث المعيبة في قواعد البيانات القائمة مع إعادة بذر العدّادات، واختيار قالب حتمي، ومفتاح نطاق واحد.
4. **الجهة ترث السياسة الوطنية ولا تتجاوزها** (G-POL-05). لا «وطني» داخل الجهة، وقواعد الجهة تضيف قيوداً أشد فقط (D-POL-4).

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-TEN-1 هوية المستأجر الوطني | `TenantDefaults.DefaultTenantId` (`PLATFORM`). وهو أيضاً جهة التجربة بحسب D-OPS-3. والصف «الوطني» في الجداول التي لها نطاق (`PolicyTemplate.Scope`، `CodePolicyTemplate.Scope`) هو `Scope = National` فقط، فلا تُكشف تجاوزات جهة التجربة لغيرها في طبقة التطبيق | §1 |
| D-TEN-2 قائمة الجداول الوطنية المشتركة | `PolicyTemplates`، `PolicyTemplateVersions`، `PolicyRules`، `PolicyFactsSchemas`، `PolicyWorkflowBindings`، `WorkflowDefinitions`، `CodePolicyTemplates`، `CodePolicyVersions`، `CodePolicyRules`، `ReferenceCatalogEntries`. لا يُضاف إليها جدول فيه بيانات شخصية | §1 |
| D-TEN-3 (=D-COD-1) مقطع الجهة في الرقم | لا في هذه الموجة. التسلسل لكل مستأجر، والرقم فريد داخل المستأجر. المقطع ← الموجة 2 (COD-H) | §3 |
| D-TEN-4 (=D-COD-3) الأرقام الصادرة بلا بادئة | تقرير فقط (`scripts/ten048-find-prefixless-codes.ps1`)، بلا إعادة كتابة: لا إنتاج بعد، وبيئات الاختبار تُعاد تهيئتها | §2 |
| D-TEN-5 (=D-COD-8) سنة رقم طلب الشاغر | سنة الموازنة تظهر في الرقم: `REQ-{BudgetYear}-#####`، والتسلسل لكل سنة موازنة. المنطقة الزمنية للسنة التقويمية ← الموجة 2 (G-COD-19) | §3 |
| D-TEN-6 ترقيم المنصب الممول عبر السنوات | العدّاد لكل (وحدة، درجة) **دون** سنة الموازنة، فلا يتكرر رمز منصب جديد في سنة لاحقة. والمنصب المرحَّل يحتفظ برمزه كما هو | §3 |
| D-TEN-7 مركز الكلفة | رمز **مشتق** واحد لكل وحدة تنظيمية، تتشاركه مناصبها (هكذا تستعمله المطابقة المالية في PLAN-17). بلا عدّاد، ويُسجَّل إصداره مرة واحدة | §3 |
| D-TEN-8 (=D-POL-4) سقف الجهة | الجهة تضيف `Deny` وآثاراً أشد (`RequireApproval`، `RequireDualApproval`، `BlockUntilChecklist`، `RequirePrivacyMask`، `RequireOriginReceipt`). ولا تغيّر `SetField` لحقل يضبطه الوطني، ولا `TriggerWorkflow` في مجال للوطني فيه `TriggerWorkflow`، ولا `SelectCodePolicy` إطلاقاً | §4 |
| D-TEN-9 غياب سياسة ترميز | `CodeGeneration:MissingPolicy=Refuse` في كل بيئة غير `Development`/`Testing`. وفي هاتين `Fallback` بالبادئة `FallbackPrefix` لا بنص فارغ | §2 |
| D-TEN-10 من يؤلف السياسة الوطنية | دور المؤلف الذي يعرّفه PLAN-47 (`policy-author`) **داخل المستأجر الوطني فقط**. وإن لم يوجد الدور بعد: `HrCoreWrite` داخل المستأجر الوطني فقط | §4 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-48-ten-national-agency-scope` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet test src/Tests/HRManagement.UnitTests --filter "CodeGeneration\|CodSequence\|Policy\|Prod001\|Ten00\|Mdm001\|Est00"`؛ و`dotnet test src/Tests/HRManagement.IntegrationTests --filter "CrossTenantRls\|CodeGenerationConcurrency"` بـ `RUN_TESTCONTAINERS=true` | أخضر أو موثق في `reports/plan48-baseline.md` |
| 0.3 | التحقق من الاعتماديات | PLAN-37: `scripts/acceptance-up.ps1 -ProdLike` يعمل. PLAN-47: اسم مدقق النشر ونقطة استدعائه. PLAN-42: `codes/generate` لحسابات الخدمة وتجاهل `TenantId` من الجسم | تُسجَّل الأسماء الفعلية في التقرير، وتستعملها هذه الخطة |
| 0.4 | جرد مواضع التجاوز | `rg -n "SystemTenantBypassPurpose\.(PolicyResolution\|NationalReferenceLookup)" src` | 14 موضعاً في 10 ملفات (انظر §1)، أو القائمة الفعلية |
| 0.5 | جرد الأرقام بلا بادئة | على مكدّس القبول: `SELECT "EntityKind", count(*) FROM "CodeIssuances" WHERE "Code" LIKE '-%' GROUP BY 1` | العدد في التقرير (دليل G-COD-02 الحالي) |
| 0.6 | جرد السياسات المبذورة | قراءة `CodePolicyVersions` المنشورة لكل نوع في المستأجر الوطني | قائمة النسخ التي سيغيرها §3 |

---

## In scope

### 1. قاعدة الرؤية الوطنية: طبقة EF وRLS (G-POL-05، G-COD-02، الجاهزية لـ OPS-D)

**المشكلة:**
- كيانات الإعداد كلها «مملوكة للمستأجر»، فيُطبَّق عليها `e.TenantId == EffectiveTenantId` ([TenantQueryFilterExtensions.cs:64-69](../../src/Shared/HRManagement.ServiceDefaults/TenantQueryFilterExtensions.cs)). بينما تُبذر الصفوف الوطنية بـ `DefaultTenantId`:
  - سياسات الترميز ([CodeGenerationSeeder.cs:302-316](../../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs)).
  - قوالب السياسة ومخططاتها ([PolicyEngineDomainSeeds.cs:366, 408](../../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)).
- 14 موضعاً تقرأ الصفوف الوطنية بتجاوز عام `IgnoreTenantFilterForSystem(PolicyResolution|NationalReferenceLookup)`، وكل موضع يكتب شرط «جهتي أو الوطنية» بنفسه، أو لا يكتبه:
  - [WorkflowStarter.cs:96-123](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs)؛
  - [WorkflowInstanceActionService.cs:305-311](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)؛
  - [ApprovalStartCoordinator.cs:181-182](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs)؛
  - [ReferenceDataProvider.cs:96-105](../../src/Services/HRCore/HR.Core.Infrastructure/Services/MasterData/ReferenceDataProvider.cs)؛
  - `ReferenceCatalogAdminService` (3 مواضع)؛
  - `RecruitmentMetricsService:582`، `UnifiedIntakeService:222`؛
  - `WorkflowInstancesController:58`، `WorkflowInboxQueryService:159`، `WorkflowPanelBuilder:44`.
- مواضع أخرى لا تتجاوز أصلاً، فلا ترى الوطني:
  - سياسات الترميز ([CodeGeneratorService.cs:82-89](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs))؛
  - قوالب السياسة ([PolicyOrchestrator.cs:130-144](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs))؛
  - المخطط ([PolicyOrchestrator.cs:121](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs)).
- `IgnoreTenantFilterForSystem` يتجاوز مرشح EF فقط ([TenantFilterBypass.cs:66-74](../../src/Shared/HRManagement.ServiceDefaults/TenantFilterBypass.cs)). وفي مسار النشر يفرض [ten002c-harden-tenant-rls.sql](../../scripts/sql/ten002c-harden-tenant-rls.sql) سياسة `tenant_isolation` على `app.current_tenant`. فتحت RLS المفروض لا ترى جلسة الجهة أي صف وطني: لا مسارات، ولا ربط سياسات، ولا مراجع (ملاحظة مدقق OPS على G-OPS-05/12).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `INationalShared` | `src/Shared/HRManagement.SharedKernel/Tenancy/INationalShared.cs` | واجهة علامة على الكيانات العشرة في D-TEN-2 |
| `NationalTenant` | `src/Shared/HRManagement.SharedKernel/Tenancy/NationalTenant.cs` | `Id = TenantDefaults.DefaultTenantId`، و`TableNames` (القائمة المرجعية الوحيدة) |
| `NationalScopeQueryExtensions` | `src/Shared/HRManagement.ServiceDefaults/NationalScopeQueryExtensions.cs` | `OwnOrNational<T>(Guid tenantId)` و`NationalOnly<T>()`، بالقيد `where T : BaseEntity, INationalShared`. كل منهما `IgnoreQueryFilters()` ثم شرط `TenantId` صريح. وقيمة جديدة `SystemTenantBypassPurpose.NationalSharedRead` تُسجَّل في السجل |
| استبدال المواضع الـ14 | الملفات أعلاه | المسارات والربط والمراجع عبر `OwnOrNational`، مع ترتيب «جهتي أولاً». ويُحذف استعمال `PolicyResolution` و`NationalReferenceLookup` للجداول الوطنية |
| `NationalSharedWriteGuard` | `src/Shared/HRManagement.ServiceDefaults/NationalSharedWriteGuard.cs` (SaveChanges interceptor، يُسجَّل في `HRCoreDbContext`) | أي `Added`/`Modified`/`Deleted` لكيان `INationalShared` بـ `TenantId = NationalTenant.Id`، والجلسة ليست وطنية وليست في نطاق بذر صريح، يُرفض بـ `UnauthorizedAccessException`. هذا يعطي المختبر (حساب superuser بلا RLS) سلوك الإنتاج نفسه |
| سياسة RLS للقراءة الوطنية | `TenantRlsMigrationHelper.NationalSharedReadSql` + `scripts/sql/ten048-national-shared-read.sql` | لكل جدول في `NationalTenant.TableNames` موجود: `DROP POLICY IF EXISTS national_shared_read …; CREATE POLICY national_shared_read ON <t> AS PERMISSIVE FOR SELECT USING ("TenantId"::text = '00000000-0000-4000-8000-000000000001')`. السياسة تضاف إلى `tenant_isolation` بـ OR للقراءة فقط. أما `INSERT`/`UPDATE`/`DELETE` فيبقى عليها `WITH CHECK` في `tenant_isolation` وحده. وكتلة تحقق تفشل إن نقصت السياسة عن جدول في القائمة، أو وُجدت على جدول خارجها |
| التطبيق في المسارين | `scripts/run-migrations.sh` (بعد `harden_tenant_rls` لـ `hr-core` فقط) و`DatabaseMigrationExtensions.ApplyTenantRlsHardeningAsync` (لـ `HRCoreDbContext`) | المسار نفسه في k8s وفي الترحيل عند الإقلاع |

**القواعد:**
1. الصف الوطني يُقرأ من أي جهة، ولا يُكتب إلا من جلسة المستأجر الوطني أو من بذر صريح (`TenantStampScope`). التطبيق والقاعدة يفرضان القاعدة نفسها.
2. لا تجاوز عام للجداول الوطنية بعد هذه الخطة. اختبار معماري يمنع `PolicyResolution`/`NationalReferenceLookup` على كيان `INationalShared`، ويمنع `OwnOrNational` على غيره (القيد العام يمنعه عند الترجمة أصلاً).
3. القائمة واحدة: `NationalTenant.TableNames` = أسماء جداول الكيانات المعلَّمة في نموذج EF = جداول السكربت. يتحقق اختبار من التساوي الثلاثي.
4. في الجداول ذات النطاق، «الوطني» في طبقة التطبيق = `TenantId = National` **و** `Scope = National` (D-TEN-1).
5. هذه الخطة **لا** تشغّل التطبيقات بأدوار `*_app` ولا تعالج مهام الخلفية و`/careers` تحت RLS (OPS-D، الموجة 2). ما تتركه جاهزاً: سياسة القراءة الوطنية وتحققها، ومسار قراءة واحد في الشيفرة، واختبار PostgreSQL بدور غير مالك يثبتهما.

### 2. سياسات الترميز مرئية لكل مستأجر، ولا احتياط صامت (G-COD-02)

**المشكلة:**
- القالب الوطني غير مرئي للجهة ([CodeGeneratorService.cs:82-89](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs))، فيُستعمل الاحتياط ([:32-33](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)).
- صيغة الاحتياط `{EntityPrefix}-YYYY-#####` ([:177-190](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). والمقطع الناقص يصير فراغاً ([:257-258](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs))، و`FallbackPrefix` لا يُستعمل.
- الدليل الحي: `"reference": "-2026-00031"` ([repair-dryrun-20261029-202041.json:8](../../acceptance-evidence/AC-HCM-25/repair/repair-dryrun-20261029-202041.json)) و`ReferenceNumber=-2026-00005` ([30b-30b-otp-request-citizen.json:9](../../acceptance-evidence/AC-HCM-30/http/30b-30b-otp-request-citizen.json)).
- الطلب المجهول من `/careers` لا يمرر مستأجر الإعلان ([PublicCareersController.cs:187, 315](../../src/Services/HRCore/HR.Core.Web/Controllers/PublicCareersController.cs)).
- شاشة `/CodePolicies` تعرض للجهة قائمة فارغة ([CodePoliciesController.cs:14-22](../../src/Services/HRCore/HR.Core.Web/Controllers/CodePoliciesController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| حل القالب | `CodeGeneratorService.ResolveSnapshotAsync` | عبر `OwnOrNational`، بأولوية حتمية: (1) طبقة الجهة بالنوع و`PolicyKey` نفسه (و`null` = القالب الافتراضي للنوع)؛ (2) الوطني بالنوع و`PolicyKey` نفسه؛ (3) لا شيء. والطلب بلا `PolicyKey` لا يرى قالباً له مفتاح (يصلح أيضاً G-COD-03 (د)) |
| طبقات الجهة القائمة | `seed-packs/code-overlays/*.json`، `AgencyProvisioningService.ApplyPackAsync` (`:295-341`) | حقل `"default": true` في الحزمة يعني أن الطبقة تحل محل القالب الافتراضي للنوع. وتُحدَّث `code-ministry-finance-v1.json` لتحافظ على سلوكها الحالي (`FIN-ORG-UNIT` افتراضي للوحدات). والمدقق (§3) يرفض أكثر من طبقة افتراضية للنوع في الجهة |
| `CodeGenerationOptions` | `HR.Core.Infrastructure/Services/CodeGeneration/CodeGenerationOptions.cs`، يُربط في `AddHrCodeGenerationEngine(IConfiguration, IHostEnvironment)` | `MissingPolicy = Refuse\|Fallback` (D-TEN-9). `Refuse` يرمي `CodePolicyMissingException` (نوع، مفتاح، مستأجر) برسالة عربية. و`Fallback` يركّب بـ `FallbackPrefix` |
| ترجمة الخطأ | `CodeGenerationController`، `GlobalExceptionFilter` إن وُجد، الواجهات المستدعية | 422 برسالة «لا توجد سياسة ترميز منشورة لـ…» بدل 500 |
| المجهول | `PublicCareersController.cs:187, 315` | `CodeGenerationRequests.CitizenReference()` مع `TenantId = posting.TenantId`. هذا استدعاء داخلي موثوق، والقيد على الجسم في PLAN-42 يخص HTTP فقط |
| ذاكرة اللقطة | `GetPublishedSnapshotAsync` (`:60-75`) | مفتاح الذاكرة يشمل `PolicyKey`. وتُبطَل عند النشر أو الترقية (§3) |
| الشاشة | `CodePoliciesController.Index` و`Views/CodePolicies/Index.cshtml` | القوالب الوطنية الموروثة بشارة «وطني (موروث — للقراءة)»، وطبقات الجهة بشارة «تجاوز الجهة» |
| كاشف الأرقام بلا بادئة | `scripts/ten048-find-prefixless-codes.ps1` | يقرأ `CodeIssuances` وأعمدة الأرقام في الخدمات (`RequisitionNumber`، `OfferNumber`، `ReferenceNumber`، `DecisionNumber`، `SerialNumber`، `TagNumber`، `CaseNumber`) ويكتب `artifacts/ten048/prefixless-codes.csv`. تقرير فقط (D-TEN-4) |

**القواعد:**
1. لا رقم يبدأ بـ `-` في أي بيئة، ولا رقم بلا البادئة المعلنة في سياسته.
2. غياب السياسة في Production خطأ صريح لا يحرق رقماً. فحص السياسة يسبق زيادة العدّاد.
3. الطلب بـ `PolicyKey` غير موجود خطأ، لا رجوع صامت إلى القالب الافتراضي (يصلح العهدة المميزة في الجهات).

### 3. مدقق السياسة، وتصحيح السياسات المعيبة، ومفتاح نطاق واحد (G-COD-03)

**المشكلة:**
- **(أ) مركز الكلفة:** سياسة بلا تسلسل ([CodeGenerationSeeder.cs:153-159](../../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs)) تُولَّد لكل منصب ([FundedPositionCodeService.cs:38-49](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/FundedPositionCodeService.cs)). فالمنصب الثاني في الوحدة يكسر الفهرس الفريد للإصدار ([HRCoreDbContext.cs:1135](../../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)) بعد زيادة العدّاد ([CodeGeneratorService.cs:409-419](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)).
- **(ب) المنصب الممول:** `BudgetYear` في النطاق لا في الرقم ([CodeGenerationSeeder.cs:60-70](../../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs))، والكيان فريد لكل سنة ([HRCoreDbContext.cs:970](../../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)).
- **(ج) طلب الشاغر:** نطاقه سنة الموازنة، والرقم يحمل سنة التقويم ([CodeGenerationSeeder.cs:172-181](../../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs)، [RecruitmentRequisitionsController.cs:203-204](../../src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentRequisitionsController.cs)).
- **(د) AH/VIP:** قالبان وطنيان للنوع نفسه بترتيب متعادل ([CodeGeneratorService.cs:82-96](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)، [CodeGenerationSeeder.cs:72-81, 220-235](../../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs))، وعدّاد مشترك.
- **(هـ) تهيئة العدّادات:** مفتاح نطاق مختلف عن المولّد ([CodSequenceBootstrapService.cs:98-121, 203-208](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodSequenceBootstrapService.cs) مقابل [CodeGeneratorService.cs:222-244](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)).
- الاختبارات تستبدل `IFundedPositionCodeService` بنسخة وهمية ([Est002EstablishmentGovernanceTests.cs:120-129](../../src/Tests/HRManagement.UnitTests/Est002EstablishmentGovernanceTests.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `CodeFormat` | `HR.Core.Infrastructure/Services/CodeGeneration/CodeFormat.cs` | يُبنى من مقاطع النسخة. فيه `Compose(facts, seq)`، و`TryParse(code, out scopeValues, out seq, out year)` بتعبير منتظم مشتق من المقاطع، و`ScopeFields`، و`HasSequence`، و`ShowsYear` |
| `CodeScopeKey` | `.../CodeGeneration/CodeScopeKey.cs` | `Build(tenantId, kind, policyKey, scopeValues, year)` الدالة الوحيدة لمفتاح النطاق. يستعملها المولّد والتهيئة وإعادة البذر. و`policyKey` غير الفارغ جزء من المفتاح، فلكل من AH وVIP عدّاد |
| `CodePolicyValidator` | `.../CodeGeneration/CodePolicyValidator.cs` | القواعد 1–5 أدناه. يُستدعى عند البذر، وعند تطبيق طبقات الجهة، وعند الترقية، وعند حل اللقطة: السياسة المنشورة غير الصالحة تُرفض بخطأ ولا تُستعمل |
| الأنواع المشتقة | `CodeGeneratorService` | نوع في `DerivedKinds = {CostCenter}` بلا مقطع تسلسل: يُركَّب الرمز من الحقائق، بلا عدّاد، ويُسجَّل إصداره مرة واحدة (`INSERT … ON CONFLICT DO NOTHING` على PostgreSQL). والمنصب الثاني في الوحدة يأخذ الرمز نفسه (D-TEN-7) |
| السياسات المصححة | `CodeGenerationSeeder` (للقواعد الجديدة) | `FundedPosition`: نطاق `[OrgUnitCode, GradeCode]` (D-TEN-6). و`RecruitmentRequisition`: `REQ-{Field BudgetYear}-{Sequence 5, scope [BudgetYear]}` مع `PerScope` (D-TEN-5). و`AssetTag`/`ASSET-PRIVILEGED`: بلا تغيير في الصيغة، والعدّاد صار منفصلاً |
| `CodePolicyUpgrades` | `HR.Core.Infrastructure/Data/CodePolicyUpgrades.cs`، يُستدعى من `NationalSeedOrchestrator.ApplyIncrementalPatchesAsync` | لكل سياسة معيبة منشورة (مطابقة نصية مطبّعة لـ `SegmentsJson` القديم): نشر v2 بـ `EffectiveFrom = now`، وأرشفة v1 بـ `EffectiveTo = now`، ثم إعادة بذر العدّادات. يعمل مرة واحدة ويُسجَّل في `SeedPackApplications` بمعرّف `ten048-code-policy-upgrade-v1` |
| `CodeCounterReseeder` | `.../CodeGeneration/CodeCounterReseeder.cs` | يقرأ `CodeIssuances` للنوع (لكل المستأجرين، عبر تجاوز بذر صريح)، و`FundedPositions.PositionCode` للمناصب. يحلل كل رمز بـ `CodeFormat` الجديد، ويضبط كل عدّاد بـ `GREATEST(LastValue, max)`. ويكتب تقريراً بالرموز التي لم تُحلَّل |
| تهيئة الترحيل | `CodSequenceBootstrapService` | يُعاد بناؤه على `CodeFormat` و`CodeScopeKey`، فيطابق المولّد |
| خطأ التصادم | `CodeGeneratorService.RecordIssuanceAsync` | انتهاك الفهرس الفريد (`ConcurrencyExtensions.IsUniqueViolation`) يتحول إلى `CodeCollisionException` بالنوع والرمز والنطاق، ويُسجَّل خطأً. ويُترجم إلى 409 برسالة عربية بدل 500 |

**قواعد المدقق:**
1. مقطع `Sequence` واحد بالضبط، إلا للأنواع المشتقة في `DerivedKinds`. ومقطع التسلسل ممنوع فيها.
2. كل حقل في `scopeFields` يظهر مقطع `Field` في الرقم.
3. `Yearly` يتطلب مقطع `Date` بصيغة `YYYY` أو `YY`.
4. `PerScope` يتطلب `scopeFields` غير فارغة.
5. طبقة جهة افتراضية واحدة على الأكثر لكل (مستأجر، نوع)، وقالب وطني افتراضي واحد على الأكثر لكل نوع.

**القواعد العامة:**
1. كل السياسات المبذورة تجتاز المدقق (اختبار يمر على كل ما يبذره `CodeGenerationSeeder`).
2. الترقية حتمية وقابلة للإعادة. وإعادة البذر لا تنقص عدّاداً أبداً.
3. الاختبارات التي تخص المناصب الممولة تستعمل `FundedPositionCodeService` الحقيقي مع السياسات المبذورة. وتُستبدل النسخ الوهمية في `Est002`/`Est005`/`Est024` بالخدمة الحقيقية حيث يكون الترقيم جزءاً مما يُختبر.

### 4. الجهة ترث السياسة الوطنية ولا تتجاوزها (G-POL-05)

**المشكلة:**
- `LoadPublishedAsync` يقرأ الوطني عبر المرشح فلا يجده ([PolicyOrchestrator.cs:112, 130-144](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs)). فيرجع إلى `PolicyStaticFallback` ([:92-95](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs)).
- `CreateDraft` ينشئ قالباً `National` داخل الجهة ([PolicyTemplatesController.cs:100-119](../../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)).
- الدمج يرتب قواعد الوطني والجهة معاً بالأولوية ([PolicyEvaluator.cs:91-111](../../src/Shared/HRManagement.SharedKernel/Policy/PolicyEvaluator.cs)). فأول `TriggerWorkflow` يفوز، وآخر `SetField` يفوز ([PolicyDecisionModels.cs:13-25](../../src/Shared/HRManagement.SharedKernel/Policy/PolicyDecisionModels.cs)). فتتجاوز الجهة الوطني.
- حلقة تهيئة طبقات الجهة لا تضيف إلا أول قاعدة لكل مجال ([AgencyProvisioningService.cs:228-236](../../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| تحميل الوطني | `PolicyOrchestrator.LoadPublishedAsync` و`:121` | الوطني عبر `NationalOnly` و`Scope = National`. والمخطط عبر `OwnOrNational` بأولوية الجهة |
| `PolicyRuleDefinition.Origin` | `PolicyConditionModels.cs:66-73` | `National` (افتراضي، متوافق مع JSON المخزّن) أو `Agency`. ويضبطه `MergeNationalAndTenant` |
| `PolicyCeiling` | `src/Shared/HRManagement.SharedKernel/Policy/PolicyCeiling.cs`، يُستدعى في `PolicyEvaluator.Evaluate` بعد جمع الآثار | يحذف أثر `SetField` من قاعدة جهة إن ضبط الوطني الحقل نفسه في هذا التقييم، وأثر `TriggerWorkflow` من قاعدة جهة إن أنتج الوطني `TriggerWorkflow`، وكل `SelectCodePolicy` من الجهة. ويضيف شرحاً «أُهمل أثر الجهة بسقف الوطني (D-POL-4)». أما `Deny` والآثار الأشد فتمر |
| فحص النشر | مدقق النشر من PLAN-47: `PolicyRuleValidator` على الإصدار كله داخل `PolicyPublishService` (يُؤكَّد الاسم في 0.3) | قاعدة جهة بـ `SelectCodePolicy`، أو `SetField` لحقل يضبطه القالب الوطني المنشور، أو `TriggerWorkflow` في مجال للوطني فيه `TriggerWorkflow`، تُرفض عند النشر برسالة. وتُطبَّق القاعدة نفسها على طبقات التهيئة |
| تأليف الوطني | `PolicyTemplatesController.CreateDraft` ونقاط API المقابلة في `PoliciesController` | خارج المستأجر الوطني: تُنشأ مسودة `TenantOverride` للجهة دائماً، وأي طلب `Scope=National` يُرفض بـ 403. وداخل الوطني: الدور بحسب D-TEN-10. والفحص في الخادم، والواجهة تعكسه |
| حلقة التهيئة | `AgencyProvisioningService.ApplyPackAsync` (`:228-260`) | إضافة كل قواعد الحزمة إلى نسخة الطبقة نفسها (البحث في المتتبَّع وغير المحفوظ)، ثم تمريرها على مدقق السقف قبل الحفظ |

**القواعد:**
1. جهة بلا تجاوز تتلقى قرار القاعدة الوطنية كما هو.
2. لا يضعف قرار الجهة قراراً وطنياً: لا سماح حيث رفض الوطني، ولا مسار أخف، ولا قيمة حقل مختلفة، ولا سياسة ترميز أخرى.
3. السقف يُفرض مرتين: عند النشر (رفض)، وعند التقييم (حذف مع شرح)، لأن الطبقات القديمة قد تحوي قواعد سابقة على المدقق.

### 5. أدوات القبول

| Artifact | المسار | الغرض |
|----------|--------|-------|
| شخصية في المستأجر الوطني | `testdata/acceptance/personas.json` و`AcceptancePersonaCatalog` (العدد مشتق منذ PLAN-41) | `platform-central` (`hr-core`، `tenantCode=PLATFORM`، `tenantId=00000000-0000-4000-8000-000000000001`)، مع مستخدمها في مزامنة realm القبول. فلا شخصية `hr-core` في المستأجر الوطني اليوم (فيه `platform-admin` و`platform-auditor` فقط) |
| AC-13 مُعاد | `src/Tests/HRManagement.AcceptanceTests/Ac13ParallelCodeGenerationTests.cs` | `[RequiredAcceptance]`. 120 توليداً متوازياً عبر مسار الخدمات (توكن خدمة + ترويسة المستأجر، بحسب PLAN-42) لمستأجرين × ثلاثة أنواع (`AssetTag`، `RecruitmentRequisition` بسنتي موازنة، `ServiceTransaction` بنوعين). يفحص التفرد، والبادئة، و`db-checks.json` حقيقياً: عدّ `CodeIssuances` وقيم العدّادات من القاعدة |

## Out of scope

| البند | الخطة |
|-------|-------|
| تشغيل التطبيقات بأدوار `*_app` وRLS مفروض، ومهام الخلفية و`/careers` تحت RLS | الموجة 2 (OPS-D). هذه الخطة تترك سياسة القراءة الوطنية جاهزة ومختبرة |
| تجاهل `TenantId` من جسم `codes/generate` وقصره على حسابات الخدمة (G-COD-05) | PLAN-42 |
| أدوار المؤلف والناشر الوطنيين، والأربع عيون، والتحقق من صحة القاعدة | PLAN-47 |
| ربط الإصدار بالكيان والإبطال (G-COD-04)، وسجل القرارات الموحّد (G-COD-07) | الموجة 2 (COD-C) |
| مقطع الجهة في الأرقام وشاشة تحرير السياسات (G-COD-17)، والمنطقة الزمنية وطول الرمز (G-COD-19) | الموجة 2 (COD-H) |
| دمج المرجع الوطني مع نسخ الجهات وتوحيد `ReferenceDataProvider` مع شاشة الإدارة (G-COD-14) | الموجة 2 (COD-F). هنا يتغير مسار القراءة فقط، والسلوك «إما/أو» يبقى كما هو |
| دفتر قرارات قابل للتتبع وShadow ذو معنى (G-POL-10) | الموجة 2 (POL-D) |
| قرار ما يحكمه المحرك وإزالة اليتامى | PLAN-49 |
| AC-14 وأدلة AC-EST (بقية G-COD-20) | الموجة 2 (COD-G) |

## Acceptance scenarios

على الملف الشبيه بالإنتاج (`scripts/acceptance-up.ps1 -ProdLike` من PLAN-37)، تحت `ACCEPTANCE_REQUIRE_ALL=true`. الفئة `AcTen48NationalScopeTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-TEN-48")`)، و`Ac13ParallelCodeGenerationTests`. الشخصيات: `central-a` (جهة A)، `b-approver` (جهة B)، `platform-central` (الوطني، جديد)، `a-logistics-maker`، `a-contracts-maker`، ومواطن مجهول عبر `/careers`.

> مكدّس القبول يتصل بحساب superuser فلا تُفرض RLS فيه. لذلك يُثبت جزء RLS باختبار PostgreSQL أدناه (`Ten048NationalRlsIntegrationTests`)، لا بالقبول.

| ID | Positive | Negative |
|----|----------|----------|
| AC-TEN-48a | `central-a` ينشئ طلب شاغر وعرضاً، ومواطن يتقدم لإعلان الجهة A. الأرقام `REQ-<سنة الموازنة>-#####` و`OFF-YYYY-#####` و`CIT-YYYY-######`. و`/CodePolicies` تعرض القوالب الوطنية بشارة «موروث» | `ten048-find-prefixless-codes.ps1` على القاعدة بعد التشغيل ← صفر رمز جديد يبدأ بـ `-` |
| AC-TEN-48b | `platform-central` و`central-a`، كلٌّ في مستأجره: منصبان ممولان في الوحدة نفسها (ومركز كلفة واحد مشترك)، ثم منصب للوحدة والدرجة نفسيهما في سنة موازنة تالية، ثم طلب شاغر لمنصب في موازنة السنة القادمة. كلها تنجح برموز فريدة | — |
| AC-TEN-48c | — | معاينة أو توليد لنوع بلا سياسة منشورة (`ForensicCase`) ← 422 برسالة «لا توجد سياسة ترميز…»، ولا صف `CodeIssuances`، والعدّاد كما هو |
| AC-TEN-48d | `b-approver` (جهة B، بلا تجاوز في `Core.Recruitment`): `GET api/policies/snapshots/Core.Recruitment` يحوي القواعد الوطنية، والتقييم بحقائق تطابق قاعدة رفض وطنية ← رفض بمعرّف القاعدة الوطنية | — |
| AC-TEN-48e | قاعدة جهة A بـ `Deny` إضافي تُنشر وتعمل | نشر قاعدة جهة بـ `SetField isPrivileged=false` في `Assets.Custody`، أو بـ `TriggerWorkflow` بأولوية 1 ← رفض عند النشر. وطبقة قديمة مزروعة بالقاعدة نفسها ← الأثر الوطني يفوز، ويظهر الشرح في القرار |
| AC-TEN-48f | — | `central-a` يطلب `CreateDraft` بنطاق `National` ← 403. وتعديل قالب وطني من جلسة جهة (POST مباشر) ← رفض `NationalSharedWriteGuard`، والقاعدة كما هي |
| AC-TEN-48g | `a-logistics-maker` ينشئ أصلاً عادياً ← `AH-YYYY-######`. وأصلاً مميزاً (`SelectCodePolicy ASSET-PRIVILEGED`) ← `VIP-YYYY-######` بعدّاد مستقل | — |
| AC-TEN-48h (AC-13) | 120 توليداً متوازياً لمستأجرين وثلاثة أنواع ← كلها فريدة ومسبوقة بالبادئة، والعدّادات في القاعدة تساوي العدد لكل نطاق | لا رمز مكرر بين نطاقين (سنتي موازنة، نوعي معاملة)، ولا كسر لنطاق المستأجر |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web
dotnet test src/Tests/HRManagement.UnitTests --filter "Ten048|CodePolicyValidator|CodeFormat|CodeScopeKey|CodeGeneration|CodSequence|PolicyCeiling|PolicyEvaluator|NationalScope|Architecture"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Ten048|CrossTenantRls|CodeGenerationConcurrency"
# انحدار الترقيم والملاك والسياسات
dotnet test src/Tests/HRManagement.UnitTests --filter "Est00|Hcm02|Hcm03|Prod001|Policy|Mdm001|Wf"
# قبول على الملف الشبيه بالإنتاج
pwsh scripts/acceptance-up.ps1 -ProdLike
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcTen48|FullyQualifiedName~Ac13ParallelCodeGeneration"
pwsh scripts/ten048-find-prefixless-codes.ps1 -Since (Get-Date).Date
```

**اختبارات جديدة:**
- **Unit:**
  - `CodePolicyValidatorTests`: القواعد الخمس، وكل سياسات `CodeGenerationSeeder` صالحة.
  - `CodeFormatTests`: تركيب وتحليل ذهاباً وإياباً لكل سياسة مبذورة.
  - `CodeScopeKeyTests`: المولّد والتهيئة يعطيان المفتاح نفسه.
  - `Ten048ResolutionTests`: أولوية الطبقة والوطني، و`PolicyKey`، و`MissingPolicy`.
  - `Ten048DerivedCostCenterTests`.
  - `PolicyCeilingTests`: SetField، وTriggerWorkflow، وSelectCodePolicy، وDeny يمر.
  - `Ten048CreateDraftScopeTests`.
  - `Ten048OverlayProvisioningTests`: كل القواعد تُضاف، والقاعدة المخالفة للسقف تُرفض.
  - `NationalSharedArchitectureTests`: لا تجاوز عام على كيان وطني، وتساوي القوائم الثلاث.
- **Integration (PostgreSQL، `PostgreSqlFixture`):**
  - `Ten048NationalRlsIntegrationTests`: ترحيل HR.Core، ثم `ten002c` و`ten048` بدور مالك، ثم جلسة بدور غير مالك بلا `BYPASSRLS` (على نمط `CrossTenantRlsIntegrationTests`) بـ `app.current_tenant` = جهة B.
    - تُقرأ القوالب الوطنية وتعريفات المسارات والمراجع وسياسات الترميز.
    - ولا تُقرأ صفوف جهة A.
    - و`UPDATE`/`INSERT` على صف وطني يُرفض من القاعدة.
    - وتعمل `OwnOrNational` عبر EF بهذه الجلسة.
  - `Ten048CodePolicyUpgradeIntegrationTests`: قاعدة بالسياسات القديمة وإصدارات قائمة (`REQ-2026-00007`، `01.02-GR-05-003` في سنتين)، ثم الترقية، ثم التوليد التالي لا يتصادم، والعدّادات لا تنقص، والإعادة لا تغيّر شيئاً.
  - `Ten048FundedPositionRealCodeServiceTests`: الخدمة الحقيقية، منصبان في وحدة وسنتا موازنة، تحت توليد متوازٍ.

## Definition of done

- لا موضع في الشيفرة يقرأ جدولاً وطنياً بتجاوز عام. `OwnOrNational`/`NationalOnly` هما المسار الوحيد، وسياسة `national_shared_read` تُطبَّق وتُتحقق في مساري الترحيل.
- `Ten048NationalRlsIntegrationTests` أخضر بدور غير مالك تحت RLS المفروض.
- كل السياسات المبذورة تجتاز المدقق. وقواعد البيانات القائمة رُقّيت بلا تصادم. ولا رقم جديد بلا بادئة في الجهات ولا في الوطني.
- الجهة بلا تجاوز تتلقى القرار الوطني، ولا قاعدة جهة تُضعفه، ولا جهة تؤلف «وطنياً».
- AC-TEN-48a..h = Pass، والأدلة في `acceptance-evidence/AC-TEN-48/` و`acceptance-evidence/AC-13/` بالعناصر الثمانية.
- سطور «الحالة» محدَّثة في الدراسات: G-COD-02 وG-COD-03 وجزء AC-13 من G-COD-20 (COD)، وG-POL-05 (POL).

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-48-ten-national-agency-scope.md` |
| Tenancy | `INationalShared.cs`، `NationalTenant.cs`، `NationalScopeQueryExtensions.cs`، `NationalSharedWriteGuard.cs`، `TenantFilterBypass.cs` (`NationalSharedRead`)، `TenantRlsMigrationHelper.cs`، `DatabaseMigrationExtensions.cs` |
| SQL | `scripts/sql/ten048-national-shared-read.sql`، `scripts/run-migrations.sh` |
| Code generation | `CodeGeneratorService.cs`، `CodeGenerationOptions.cs`، `CodeFormat.cs`، `CodeScopeKey.cs`، `CodePolicyValidator.cs`، `CodeCounterReseeder.cs`، `CodSequenceBootstrapService.cs`، `CodeGenerationSeeder.cs`، `CodePolicyUpgrades.cs`، `CodeGenerationDependencyInjection.cs`، `CodePoliciesController.cs` + `Views/CodePolicies/Index.cshtml`، `PublicCareersController.cs`، `seed-packs/code-overlays/code-ministry-finance-v1.json` |
| Policy | `PolicyOrchestrator.cs`، `PolicyConditionModels.cs` (`Origin`)، `PolicyEvaluator.cs`، `PolicyCeiling.cs`، `PolicyTemplatesController.cs`، `PoliciesController.cs`، `AgencyProvisioningService.cs`، مدقق PLAN-47 |
| Workflow/MDM (مسار القراءة فقط) | `WorkflowStarter.cs`، `WorkflowInstanceActionService.cs`، `ApprovalStartCoordinator.cs`، `WorkflowInstancesController.cs`، `WorkflowInboxQueryService.cs`، `WorkflowPanelBuilder.cs`، `RecruitmentMetricsService.cs`، `UnifiedIntakeService.cs`، `ReferenceDataProvider.cs`، `ReferenceCatalogAdminService.cs` |
| Scripts | `scripts/ten048-find-prefixless-codes.ps1` |
| Tests | الوحدة والتكامل أعلاه، `AcTen48NationalScopeTests.cs`، `Ac13ParallelCodeGenerationTests.cs` (مُعاد) |
| Test data | `testdata/acceptance/personas.json` (`platform-central`) |
| Docs | `COD_001_CODE_GENERATION.md` (الأولوية، `MissingPolicy`، المدقق، الأنواع المشتقة، قائمة الأنواع الكاملة)، `TENANT_ENTITY_CLASSIFICATION.md` (فئة «وطني مشترك»)، `PROD_006_TENANT_RLS.md` (`national_shared_read`)، `PROD_001_TENANT_OVERRIDE.md` (السقف)، `MIG_003_COD_CODE_LINEAGE.md` (التهيئة الموحدة)، وسطور «الحالة» في الدراستين |
| CI | `tests/acceptance/ci-scope.json`: تسجيل كل صنف قبول جديد في هذه الخطة (مطلوباً أو مستبعداً بمالك)، وإلا تفشل وظيفة القبول (قاعدة PLAN-41) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| سياسة القراءة الوطنية تكشف لكل جهة تجاوزات جهة التجربة المخزنة في المستأجر الوطني (D-OPS-3) | القائمة تقتصر على جداول الإعداد بلا بيانات شخصية. وطبقة التطبيق تقرأ `Scope = National` فقط. ويُوثَّق الخطر في `TENANT_THREAT_MODEL.md`، ويُعاد تقييمه عند فصل جهة التجربة عن الوطني (الموجة 2) |
| إعادة بذر العدّادات تخطئ في تحليل رموز قديمة بصيغة غير متوقعة | التحليل بـ `CodeFormat` المشتق من المقاطع. والرموز غير المحللة تُكتب في تقرير ولا تُخمَّن. و`GREATEST` يمنع النقصان. واختبار تكامل على بيانات مزروعة بالصيغ الحقيقية |
| تغيير نطاق عدّاد المنصب الممول يغيّر أرقام المناصب الجديدة عما اعتاده المستخدمون | D-TEN-6 موثّق في `EST_001_ESTABLISHMENT.md`. والمناصب القائمة لا تُمس |
| السقف يغيّر قرارات قائمة في جهات لها طبقات أقدم | الاختبار يسرد الطبقات المتأثرة (`reports/plan48-ceiling-impact.md`) قبل التفعيل. والشرح في القرار يُظهر ما أُهمل |
| `MissingPolicy=Refuse` يوقف نوعاً لم تُبذر سياسته في بيئة قائمة | الترقية تبذر كل الأنواع الناقصة قبل التفعيل. وفحص إقلاع يسرد الأنواع المستعملة في الشيفرة (`CodeGenerationRequests`) بلا سياسة منشورة |
| تعارض مع تغييرات PLAN-43/47 في الملفات نفسها | المرحلة 0.3 تسجل الأسماء الفعلية. والتغيير هنا محصور في مسار القراءة ونقطة نشر واحدة |

## Effort estimate

9–10 أيام عمل:
- قاعدة الرؤية الوطنية (EF وRLS واختبار PostgreSQL): 2.5.
- حل سياسات الترميز ومنع الاحتياط: 1.5.
- المدقق والتصحيح والترقية وإعادة البذر: 2.5.
- السقف والتأليف الوطني وحلقة التهيئة: 1.5.
- القبول وAC-13 والوثائق: 1.5.
